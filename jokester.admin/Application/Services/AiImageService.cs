using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.Points;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;

namespace jokester.admin.Application.Services;

public sealed class AiImageService(
    HttpClient httpClient,
    IAiImageModelConfigService modelConfigService,
    IPointService pointService,
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IAiImageTaskQueue taskQueue,
    IAiImageAdmissionService admissionService,
    IOptions<OpenAiOptions> openAiOptions,
    IOptions<AiImageSizeModeOptions> sizeModeOptions,
    IOptions<PromptLibraryOptions> promptLibraryOptions,
    IAiMediaPathResolver mediaPathResolver,
    IAiPromptFilter promptFilter,
    IUserConsentService userConsentService,
    IMediaAssetService mediaAssetService,
    IAiImageCatalogService catalogService,
    IAiSizeModeRolloutPolicy rolloutPolicy,
    ILogger<AiImageService> logger) : IAiImageService
{
    private const string MimeType = "image/png";
    private const int MaxReferenceImageCount = 6;
    private const long MaxReferenceImageSizeBytes = 10 * 1024 * 1024;
    private const long MaxMaskImageSizeBytes = 4 * 1024 * 1024;
    private const long MaxGeneratedImageSizeBytes = 25 * 1024 * 1024;
    private const int ProviderDimensionQuantum = 16;
    private const int ProviderMaxLongSide = 3840;
    private const int ProviderMinTotalPixels = 655_360;
    private const int ProviderMaxTotalPixels = 8_294_400;
    private static readonly TimeSpan GenerateWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GenerateWaitPollInterval = TimeSpan.FromSeconds(1);
    private static readonly SemaphoreSlim SyncGenerateSemaphore = new(4, 4);
    private const string ResolutionType = "resolution";
    private const string QualityType = "quality";
    private const string AspectRatioType = "aspect_ratio";
    private const string AiImageSiteCode = "ai_image";

    public async Task<PagedResult<AiImageTaskDto>> GetPageAsync(AiImageQuery query, CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var currentUserId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        await ExpireStaleTasksAsync(currentUser.IsSuperAdmin ? null : currentUserId, cancellationToken);

        var prompt = string.IsNullOrWhiteSpace(query.Prompt) ? null : query.Prompt.Trim();
        var modelName = string.IsNullOrWhiteSpace(query.ModelName) ? null : query.ModelName.Trim();
        var startDate = query.StartDate;
        var endDateExclusive = ResolveEndDateExclusive(query.EndDate);
        var favoriteTaskIds = query.IsFavorite.HasValue
            ? await db.Queryable<AiImageFavoriteEntity>()
                .Where(x => x.UserId == currentUserId && !x.IsDeleted)
                .Select(x => x.TaskId)
                .Distinct()
                .ToListAsync(cancellationToken)
            : [];

        if (query.IsFavorite == true && favoriteTaskIds.Count == 0)
        {
            return new PagedResult<AiImageTaskDto>
            {
                Total = 0,
                PageIndex = query.PageIndex,
                PageSize = query.PageSize,
                Items = []
            };
        }

        var dbQuery = db.Queryable<AiImageTaskEntity>()
            .Where(x => !x.IsDeleted)
            .WhereIF(!currentUser.IsSuperAdmin, x => x.UserId == currentUserId)
            .WhereIF(query.SiteId.HasValue, x => x.SiteId == query.SiteId!.Value)
            .WhereIF(query.Status.HasValue, x => x.Status == query.Status!.Value)
            .WhereIF(!string.IsNullOrWhiteSpace(prompt), x => x.Prompt.Contains(prompt!))
            .WhereIF(!string.IsNullOrWhiteSpace(modelName), x => x.ModelName == modelName)
            .WhereIF(startDate.HasValue, x => x.CreatedAt >= startDate!.Value)
            .WhereIF(endDateExclusive.HasValue, x => x.CreatedAt < endDateExclusive!.Value)
            .WhereIF(query.IsFavorite == true, x => favoriteTaskIds.Contains(x.Id))
            .WhereIF(query.IsFavorite == false && favoriteTaskIds.Count > 0, x => !favoriteTaskIds.Contains(x.Id))
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id);

        var entities = await dbQuery.ToPageListAsync(query.PageIndex, query.PageSize, total);
        var favoriteLookup = await GetFavoriteUrlLookupAsync(entities.Select(x => x.Id).ToArray(), currentUserId, cancellationToken);
        var catalogLookup = await GetCatalogVersionLookupAsync(entities, cancellationToken);
        var items = entities.Select(x => MapTaskDto(
            x,
            favoriteLookup.GetValueOrDefault(x.Id, []),
            x.ModelReleaseId.HasValue ? catalogLookup.GetValueOrDefault(x.ModelReleaseId.Value) : null)).ToArray();

        return new PagedResult<AiImageTaskDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<AiImageParameterOptionsDto> GetParameterOptionsAsync(CancellationToken cancellationToken)
    {
        var parameters = await db.Queryable<AiImageParameterEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .Where(x => x.ParamType == ResolutionType || x.ParamType == QualityType || x.ParamType == AspectRatioType)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var pointPriceEntities = await db.Queryable<AiImagePointPriceEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.ModelCode)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var pointPrices = pointPriceEntities
            .Select(x => new AiImagePointPriceDto
            {
                ModelCode = x.ModelCode,
                ResolutionCode = x.ResolutionCode,
                QualityCode = x.QualityCode,
                Points = x.Points,
                PriceAmount = x.PriceAmount,
                PriceMinorUnits = Money.ToMinorUnits(x.PriceAmount),
                Currency = x.Currency,
                Sort = x.Sort
            })
            .ToArray();

        return new AiImageParameterOptionsDto
        {
            Resolutions = MapOptions(parameters, ResolutionType),
            Qualities = MapOptions(parameters, QualityType),
            AspectRatios = MapOptions(parameters, AspectRatioType)
                .Where(x => !string.Equals(x.Code, "auto", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            PointPrices = pointPrices
        };
    }

    public async Task<IReadOnlyList<AiImagePricingOptionDto>> GetPricingOptionsAsync(CancellationToken cancellationToken)
    {
        var prices = await db.Queryable<AiImagePointPriceEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.ModelCode)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var modelConfigs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var modelSorts = modelConfigs
            .GroupBy(x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(item => item.Sort).ThenBy(item => item.Id).First().Sort,
                StringComparer.OrdinalIgnoreCase);

        return prices
            .Select(price =>
            {
                var modelCode = AiImageModelConfigService.NormalizeModelCode(price.ModelCode);

                return new AiImagePricingOptionDto
                {
                    ModelCode = modelCode,
                    ResolutionCode = price.ResolutionCode,
                    QualityCode = price.QualityCode,
                    Points = price.Points,
                    PriceAmount = price.PriceAmount,
                    PriceMinorUnits = Money.ToMinorUnits(price.PriceAmount),
                    Currency = price.Currency,
                    Sort = price.Sort
                };
            })
            .OrderBy(x => modelSorts.GetValueOrDefault(x.ModelCode))
            .ThenBy(x => x.Sort)
            .ThenBy(x => x.ModelCode)
            .ThenBy(x => x.ResolutionCode)
            .ThenBy(x => x.QualityCode)
            .ToArray();
    }

    public async Task<ResolveAiImageParametersResponse> ResolveParametersAsync(ResolveAiImageParametersRequest request, CancellationToken cancellationToken)
    {
        var resolutionCode = NormalizeCode(ResolveCodeAlias(request.Resolution, request.ResolutionCode), "1k");
        var qualityCode = NormalizeCode(request.QualityCode, "med");
        var aspectRatioCode = NormalizeCode(request.AspectRatioCode, "1:1");

        var codes = new[] { resolutionCode, qualityCode, aspectRatioCode };
        var parameters = await db.Queryable<AiImageParameterEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .Where(x => codes.Contains(x.ParamCode))
            .ToListAsync(cancellationToken);

        var quality = RequireParameter(parameters, QualityType, qualityCode);
        var providerQuality = string.IsNullOrWhiteSpace(quality.ProviderValue) ? quality.ParamCode : quality.ProviderValue.Trim();

        if (string.Equals(aspectRatioCode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.AutoSizeNotSupported,
                "当前站点配置不支持自动尺寸");
        }

        var resolution = RequireParameter(parameters, ResolutionType, resolutionCode);
        var aspectRatio = RequireParameter(parameters, AspectRatioType, aspectRatioCode);

        var longSide = resolution.ValueInt1.GetValueOrDefault();
        if (longSide <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid image resolution parameter");
        }

        var ratioWidth = aspectRatio.ValueInt1.GetValueOrDefault();
        var ratioHeight = aspectRatio.ValueInt2.GetValueOrDefault();
        if (ratioWidth <= 0 || ratioHeight <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid image aspect ratio parameter");
        }

        var (width, height) = CalculateProviderSize(longSide, ratioWidth, ratioHeight);

        return new ResolveAiImageParametersResponse
        {
            ResolutionCode = resolution.ParamCode,
            QualityCode = quality.ParamCode,
            AspectRatioCode = aspectRatio.ParamCode,
            Width = width,
            Height = height,
            Size = $"{width}x{height}",
            ProviderQuality = providerQuality
        };
    }

    private static (int Width, int Height) CalculateProviderSize(int requestedLongSide, int ratioWidth, int ratioHeight)
    {
        var cappedLongSide = RoundDownToMultiple(Math.Min(requestedLongSide, ProviderMaxLongSide), ProviderDimensionQuantum);
        if (cappedLongSide <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid image resolution parameter");
        }

        var isLandscapeOrSquare = ratioWidth >= ratioHeight;
        var longRatio = Math.Max(ratioWidth, ratioHeight);
        var shortRatio = Math.Min(ratioWidth, ratioHeight);
        if ((long)longRatio > (long)shortRatio * 3)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid image aspect ratio parameter");
        }

        for (var longSide = cappedLongSide; longSide >= ProviderDimensionQuantum; longSide -= ProviderDimensionQuantum)
        {
            var dimensions = CalculateDimensionsForLongSide(longSide, shortRatio, longRatio, isLandscapeOrSquare);
            if (IsValidProviderSize(dimensions.Width, dimensions.Height))
            {
                return dimensions;
            }
        }

        // Wide 1K ratios can fall below the provider's minimum pixel count. Grow only
        // as much as needed while preserving the requested ratio and 16px alignment.
        for (var longSide = cappedLongSide + ProviderDimensionQuantum;
             longSide <= ProviderMaxLongSide;
             longSide += ProviderDimensionQuantum)
        {
            var dimensions = CalculateDimensionsForLongSide(longSide, shortRatio, longRatio, isLandscapeOrSquare);
            if (IsValidProviderSize(dimensions.Width, dimensions.Height))
            {
                return dimensions;
            }
        }

        throw new AppException(ErrorCodes.BadRequest, "Invalid image size parameter");
    }

    private static (int Width, int Height) CalculateDimensionsForLongSide(
        int longSide,
        int shortRatio,
        int longRatio,
        bool isLandscapeOrSquare)
    {
        var shortSide = RoundToNearestMultiple(
            (double)longSide * shortRatio / longRatio,
            ProviderDimensionQuantum);
        return isLandscapeOrSquare ? (longSide, shortSide) : (shortSide, longSide);
    }

    private static bool IsValidProviderSize(int width, int height)
    {
        var longSide = Math.Max(width, height);
        var shortSide = Math.Min(width, height);
        var totalPixels = (long)width * height;
        return width > 0
            && height > 0
            && width % ProviderDimensionQuantum == 0
            && height % ProviderDimensionQuantum == 0
            && longSide <= ProviderMaxLongSide
            && (long)longSide <= (long)shortSide * 3
            && totalPixels >= ProviderMinTotalPixels
            && totalPixels <= ProviderMaxTotalPixels;
    }

    private static int RoundDownToMultiple(int value, int multiple)
    {
        return value / multiple * multiple;
    }

    private static int RoundToNearestMultiple(double value, int multiple)
    {
        return Math.Max(multiple, (int)Math.Round(value / multiple, MidpointRounding.AwayFromZero) * multiple);
    }

    public async Task<AiImageTaskDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var entity = await db.Queryable<AiImageTaskEntity>()
            .Where(x => x.Id == id && !x.IsDeleted)
            .WhereIF(!currentUser.IsSuperAdmin, x => x.UserId == currentUserId)
            .FirstAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        if (IsTaskExpired(entity, HongKongNow()))
        {
            await ExpireStaleTaskAsync(entity, cancellationToken);
            entity = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.Id == id && !x.IsDeleted)
                .WhereIF(!currentUser.IsSuperAdmin, x => x.UserId == currentUserId)
                .FirstAsync(cancellationToken);
            if (entity is null)
            {
                return null;
            }
        }

        var favoriteLookup = await GetFavoriteUrlLookupAsync([entity.Id], currentUserId, cancellationToken);
        var catalogLookup = await GetCatalogVersionLookupAsync([entity], cancellationToken);
        return MapTaskDto(
            entity,
            favoriteLookup.GetValueOrDefault(entity.Id, []),
            entity.ModelReleaseId.HasValue ? catalogLookup.GetValueOrDefault(entity.ModelReleaseId.Value) : null);
    }

    public async Task<GenerateAiImageResponse> GenerateAsync(GenerateAiImageRequest request, CancellationToken cancellationToken)
    {
        if (!await SyncGenerateSemaphore.WaitAsync(0, cancellationToken))
        {
            throw new AppException(ErrorCodes.TooManyRequests, "同步生图请求过多，请稍后重试");
        }

        try
        {
            var taskIds = await CreateTasksAsync(new CreateAiImageTaskRequest
            {
                IdempotencyKey = request.IdempotencyKey,
                SourcePromptId = request.SourcePromptId,
                Prompt = request.Prompt,
                ModelCode = request.ModelCode,
                ModelName = request.ModelName,
                ImageCount = request.ImageCount,
                SizeMode = request.SizeMode,
                CatalogVersion = request.CatalogVersion,
                Resolution = request.Resolution,
                ResolutionCode = request.ResolutionCode,
                QualityCode = request.QualityCode,
                AspectRatioCode = request.AspectRatioCode,
                ReferenceImageUrls = request.ReferenceImageUrls,
                ReferenceAssetIds = request.ReferenceAssetIds,
                MaskImageUrl = request.MaskImageUrl,
                MaskAssetId = request.MaskAssetId
            }, cancellationToken);

            if (request.SizeMode is not null || request.CatalogVersion is not null)
            {
                return await WaitForVersionedGenerateResponseAsync(taskIds, cancellationToken);
            }

            var generatedTasks = await Task.WhenAll(
                taskIds.Select(taskId => WaitForGeneratedTaskAsync(taskId, cancellationToken)));

            var resultUrls = generatedTasks.SelectMany(x => x.ResultUrls).ToArray();
            if (resultUrls.Length == 0 && generatedTasks.All(x => x.StatusCode == 2))
            {
                throw new AppException(
                    ErrorCodes.BadRequest,
                    generatedTasks.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))?.ErrorMessage
                        ?? "AI image generation failed");
            }

            if (resultUrls.Length == 0)
            {
                throw new AppException(
                    ErrorCodes.ServerError,
                    $"AI image generation timed out after {GenerateWaitTimeout.TotalMinutes:0} minutes. Tasks {string.Join(", ", taskIds)} are still available in history.");
            }

            var firstTask = generatedTasks[0];
            return new GenerateAiImageResponse
            {
                TaskId = firstTask.Id,
                TaskIds = taskIds,
                SourcePromptId = firstTask.SourcePromptId,
                ModelName = firstTask.ModelName,
                ModelCode = firstTask.ModelCode,
                Prompt = firstTask.Prompt,
                ResolutionCode = firstTask.ResolutionCode,
                QualityCode = firstTask.QualityCode,
                AspectRatioCode = firstTask.AspectRatioCode,
                Width = firstTask.Width,
                Height = firstTask.Height,
                Size = firstTask.Size,
                Quality = firstTask.Quality,
                MimeType = MimeType,
                Url = resultUrls[0],
                Urls = resultUrls,
                MaskImageUrl = firstTask.MaskImageUrl,
                ReferenceImageUrls = firstTask.ReferenceImageUrls
            };
        }
        finally
        {
            SyncGenerateSemaphore.Release();
        }
    }

    public async Task<UploadAiImageResponse> UploadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "文件不能为空");
        }

        if (file.Length > MaxReferenceImageSizeBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "文件大小不能超过 10MB");
        }

        var owner = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "Unauthorized");
        return await mediaAssetService.UploadAsync(owner, file, cancellationToken);
    }

    public async Task<GenerateAiImageResponse> GenerateFromResolvedAsync(string prompt, string? modelCode, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl, long ownerUserId, CancellationToken cancellationToken)
    {
        var normalizedPrompt = AiImagePromptValidator.Validate(prompt);
        var normalizedReferenceImageUrls = ValidateReferenceImageUrls(referenceImageUrls);
        var normalizedMaskImageUrl = ValidateMaskImageUrl(maskImageUrl, normalizedReferenceImageUrls);
        var requestedModelCode = AiImageModelConfigService.NormalizeModelCode(modelCode);
        var modelRoutes = await modelConfigService.ResolveRoutesAsync(requestedModelCode, parameters.ResolutionCode, cancellationToken);
        var modelConfig = modelRoutes[0];

        if (!string.Equals(modelConfig.ModelCode, AiImageModelConfigService.DefaultGptModelCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                $"Model {requestedModelCode} is not supported by this endpoint. Use {AiImageModelConfigService.DefaultGptModelCode} on /api/ai/images/generate, or call /api/ai/images/nanoBananaImage/generate for Nano Banana.");
        }

        return await GenerateFromResolvedRoutesAsync(
            normalizedPrompt,
            modelRoutes,
            parameters,
            normalizedReferenceImageUrls,
            normalizedMaskImageUrl,
            ownerUserId,
            cancellationToken);
    }

    public async Task<GenerateAiImageResponse> GenerateFromResolvedRoutesAsync(
        string prompt,
        IReadOnlyList<ResolvedAiImageModelConfig> routes,
        ResolveAiImageParametersResponse parameters,
        IReadOnlyList<string> referenceImageUrls,
        string? maskImageUrl,
        long ownerUserId,
        CancellationToken cancellationToken)
    {
        if (routes.Count == 0)
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "The model release has no executable route.");
        }
        var normalizedPrompt = AiImagePromptValidator.Validate(prompt);
        var normalizedReferenceImageUrls = ValidateReferenceImageUrls(referenceImageUrls);
        var normalizedMaskImageUrl = ValidateMaskImageUrl(maskImageUrl, normalizedReferenceImageUrls);
        EnsureOwnedPrivateMediaUrls(ownerUserId, normalizedReferenceImageUrls, normalizedMaskImageUrl);
        httpClient.DefaultRequestHeaders.Authorization = null;

        var generatedImage = await GenerateImageWithFallbackAsync(
            routes,
            normalizedPrompt,
            parameters,
            normalizedReferenceImageUrls,
            normalizedMaskImageUrl,
            ownerUserId,
            cancellationToken);

        var firstImage = generatedImage.ProviderResult;
        var savedImage = generatedImage.SavedImage;

        return new GenerateAiImageResponse
        {
            TaskId = 0,
            ModelName = generatedImage.Route.ModelName,
            ModelCode = generatedImage.Route.ModelCode,
            SizeContractVersion = parameters.SizeContractVersion,
            CatalogVersion = parameters.CatalogVersion,
            SizeMode = parameters.SizeMode,
            RequestState = "active",
            BatchStatus = "succeeded",
            Results =
            [
                new AiImageGenerateResultDto
                {
                    Ordinal = 0,
                    TaskId = 0,
                    Status = "succeeded",
                    Url = savedImage.Url,
                    OutputWidth = savedImage.Width,
                    OutputHeight = savedImage.Height,
                    OutputSize = $"{savedImage.Width}x{savedImage.Height}",
                    MimeType = savedImage.MimeType,
                    RefundedPoints = 0
                }
            ],
            ProviderModel = generatedImage.Route.ProviderModel,
            Prompt = normalizedPrompt,
            ResolutionCode = parameters.ResolutionCode ?? string.Empty,
            QualityCode = parameters.QualityCode,
            AspectRatioCode = parameters.AspectRatioCode ?? string.Empty,
            Width = parameters.SizeMode == AiImageCatalogService.AutoSizeMode ? savedImage.Width : parameters.Width.GetValueOrDefault(),
            Height = parameters.SizeMode == AiImageCatalogService.AutoSizeMode ? savedImage.Height : parameters.Height.GetValueOrDefault(),
            Size = parameters.SizeMode == AiImageCatalogService.AutoSizeMode ? $"{savedImage.Width}x{savedImage.Height}" : parameters.Size,
            Quality = parameters.ProviderQuality,
            MimeType = savedImage.MimeType,
            Url = savedImage.Url,
            Urls = [savedImage.Url],
            Base64 = savedImage.Base64,
            DataUrl = $"data:{savedImage.MimeType};base64,{savedImage.Base64}",
            MaskImageUrl = normalizedMaskImageUrl,
            ReferenceImageUrls = normalizedReferenceImageUrls,
            RevisedPrompt = firstImage.RevisedPrompt
        };
    }

    private async Task<GeneratedProviderImage> GenerateImageWithFallbackAsync(
        IReadOnlyList<ResolvedAiImageModelConfig> routes,
        string prompt,
        ResolveAiImageParametersResponse parameters,
        IReadOnlyList<string> referenceImageUrls,
        string? maskImageUrl,
        long ownerUserId,
        CancellationToken cancellationToken)
    {
        var selectedRoute = routes[0];
        if (routes.Count > 1)
        {
            using var primaryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            primaryTimeout.CancelAfter(TimeSpan.FromSeconds(openAiOptions.Value.PrimaryTimeoutSeconds));
            try
            {
                return await GenerateImageForRouteAsync(
                    selectedRoute,
                    prompt,
                    parameters,
                    referenceImageUrls,
                    maskImageUrl,
                    ownerUserId,
                    primaryTimeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Primary AI image route failed; switching to the database fallback route. Mode={Mode}, ModelCode={ModelCode}, RouteConfigId={RouteConfigId}, ProviderModel={ProviderModel}, FailureType={FailureType}",
                    referenceImageUrls.Count == 0 ? "generations" : "edits",
                    selectedRoute.ModelCode,
                    selectedRoute.Id,
                    selectedRoute.ProviderModel,
                    ex.GetType().Name);
            }

            selectedRoute = routes[1];
        }

        return await GenerateImageForRouteAsync(
            selectedRoute,
            prompt,
            parameters,
            referenceImageUrls,
            maskImageUrl,
            ownerUserId,
            cancellationToken);
    }

    private async Task<GeneratedProviderImage> GenerateImageForRouteAsync(
        ResolvedAiImageModelConfig modelConfig,
        string prompt,
        ResolveAiImageParametersResponse parameters,
        IReadOnlyList<string> referenceImageUrls,
        string? maskImageUrl,
        long ownerUserId,
        CancellationToken cancellationToken)
    {
        using var document = await SendImageRequestForRouteAsync(
            modelConfig,
            prompt,
            parameters,
            referenceImageUrls,
            maskImageUrl,
            cancellationToken);
        var providerResult = ReadFirstOpenAiImage(document.RootElement);
        var savedImage = string.IsNullOrWhiteSpace(providerResult.Base64)
            ? await DownloadImageAsBase64Async(
                providerResult.Url!,
                modelConfig,
                parameters.SizeContractVersion == AiImageCatalogService.SizeContractVersion,
                ownerUserId,
                cancellationToken)
            : await SaveImageAsync(providerResult.Base64!, ownerUserId, cancellationToken);
        return new GeneratedProviderImage(providerResult, savedImage, modelConfig);
    }

    private async Task<JsonDocument> SendImageRequestForRouteAsync(
        ResolvedAiImageModelConfig modelConfig,
        string prompt,
        ResolveAiImageParametersResponse parameters,
        IReadOnlyList<string> referenceImageUrls,
        string? maskImageUrl,
        CancellationToken cancellationToken)
    {
        var mode = referenceImageUrls.Count == 0 ? "generations" : "edits";
        var primaryImageFieldName = referenceImageUrls.Count == 0 ? null : "image[]";
        var response = await SendImageRequestAsync(modelConfig, prompt, parameters, referenceImageUrls, maskImageUrl, primaryImageFieldName, cancellationToken);
        if (response.IsSuccess && TryReadOpenAiImage(response.Document.RootElement, out _))
        {
            return response.Document;
        }

        if (referenceImageUrls.Count > 0)
        {
            logger.LogWarning(
                "AI image request failed; retrying with alternate multipart image field. Mode={Mode}, ModelCode={ModelCode}, StatusCode={StatusCode}",
                mode,
                modelConfig.ModelCode,
                response.StatusCode);
            response.Document.Dispose();

            var retryResponse = await SendImageRequestAsync(modelConfig, prompt, parameters, referenceImageUrls, maskImageUrl, "image", cancellationToken);
            if (retryResponse.IsSuccess && TryReadOpenAiImage(retryResponse.Document.RootElement, out _))
            {
                return retryResponse.Document;
            }

            LogProviderFailure(retryResponse, mode, modelConfig, "image");
            retryResponse.Document.Dispose();
            throw new AppException(ErrorCodes.BadRequest, "Image generation service temporarily failed. Please try again later.");
        }

        LogProviderFailure(response, mode, modelConfig, primaryImageFieldName);
        response.Document.Dispose();
        throw new AppException(ErrorCodes.BadRequest, "Image generation service temporarily failed. Please try again later.");
    }

    private async Task<ImageProviderResponse> SendImageRequestAsync(
        ResolvedAiImageModelConfig modelConfig,
        string prompt,
        ResolveAiImageParametersResponse parameters,
        IReadOnlyList<string> referenceImageUrls,
        string? maskImageUrl,
        string? imageFieldName,
        CancellationToken cancellationToken)
    {
        using var httpRequest = referenceImageUrls.Count == 0
            ? BuildGenerationRequest(modelConfig, prompt, parameters)
            : BuildEditRequest(modelConfig, prompt, parameters, referenceImageUrls, maskImageUrl, imageFieldName ?? "image[]");
        httpRequest.Headers.Remove("Authorization");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);

        logger.LogInformation(
            "Sending AI image request. Mode={Mode}, ModelCode={ModelCode}, RouteRole={RouteRole}, RouteConfigId={RouteConfigId}, ProviderModel={ProviderModel}, Endpoint={Endpoint}, Size={Size}, Quality={Quality}, ReferenceImageCount={ReferenceImageCount}",
            referenceImageUrls.Count == 0 ? "generations" : "edits",
            modelConfig.ModelCode,
            modelConfig.RouteRole,
            modelConfig.Id,
            modelConfig.ProviderModel,
            httpRequest.RequestUri,
            parameters.Size,
            parameters.ProviderQuality,
            referenceImageUrls.Count);

        using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var maxProviderResponseBytes = checked((int)(Math.Max(MaxGeneratedImageSizeBytes, sizeModeOptions.Value.AutoMaxOutputBytes) * 4 / 3 + 1_048_576));
        var body = await ReadUtf8ContentLimitedAsync(response.Content, maxProviderResponseBytes, cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogError(
                "AI image provider returned non-JSON response. StatusCode={StatusCode}, FailureType={FailureType}",
                response.StatusCode,
                ex.GetType().Name);
            throw new AppException(ErrorCodes.BadRequest, "Image generation failed: provider returned invalid JSON");
        }

        return new ImageProviderResponse(
            response.IsSuccessStatusCode,
            response.StatusCode,
            httpRequest.RequestUri?.ToString() ?? string.Empty,
            body,
            document);
    }

    private static async Task<string> ReadUtf8ContentLimitedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maxBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid, "Image provider response exceeded the allowed size");
        }
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maxBytes)
            {
                throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid, "Image provider response exceeded the allowed size");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private async Task<AiImageTaskDto> WaitForGeneratedTaskAsync(long taskId, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(GenerateWaitTimeout);

        while (!timeout.IsCancellationRequested)
        {
            var task = await GetByIdAsync(taskId, cancellationToken);
            if (task is null)
            {
                throw new NotFoundException($"AI image task does not exist: {taskId}");
            }

            if (!IsTaskActive(task.StatusCode))
            {
                return task;
            }

            try
            {
                await Task.Delay(GenerateWaitPollInterval, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        return await GetByIdAsync(taskId, cancellationToken)
            ?? throw new NotFoundException($"AI image task does not exist: {taskId}");
    }

    private async Task<GenerateAiImageResponse> WaitForVersionedGenerateResponseAsync(
        IReadOnlyList<long> taskIds,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated");
        var orderedIds = taskIds.ToArray();
        var deadline = DateTime.UtcNow.Add(GenerateWaitTimeout);
        List<AiImageTaskEntity> tasks;
        while (true)
        {
            tasks = await db.Queryable<AiImageTaskEntity>()
                .Where(x => x.UserId == userId && orderedIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
            if (tasks.Count != orderedIds.Length)
            {
                throw new AppException(
                    ErrorCodes.Conflict,
                    MachineErrorCodes.LegacyIdempotencyUnverifiable,
                    "The durable AI image request batch is incomplete.");
            }
            if (tasks.All(x => !IsTaskActive(x.Status)) || DateTime.UtcNow >= deadline)
            {
                break;
            }
            await Task.Delay(GenerateWaitPollInterval, cancellationToken);
        }

        var taskLookup = tasks.ToDictionary(x => x.Id);
        var orderedTasks = orderedIds.Select(id => taskLookup[id]).ToArray();
        string? catalogVersion = null;
        var releaseId = orderedTasks[0].ModelReleaseId;
        if (releaseId.HasValue)
        {
            catalogVersion = await db.Queryable<AiImageModelReleaseEntity>()
                .Where(x => x.Id == releaseId.Value)
                .Select(x => x.CatalogVersion)
                .FirstAsync(cancellationToken);
        }

        var results = orderedTasks.Select((task, ordinal) =>
        {
            var url = task.IsDeleted ? null : DeserializeImageUrls(task.ResultUrls).FirstOrDefault();
            var succeeded = task.Status == 1 && !task.IsDeleted;
            var failed = task.Status == 2 && !task.IsDeleted;
            return new AiImageGenerateResultDto
            {
                Ordinal = ordinal,
                TaskId = task.Id,
                Status = MapTaskStatus(task.Status),
                IsDeleted = task.IsDeleted,
                Url = succeeded ? url : null,
                OutputWidth = succeeded ? task.OutputWidth : null,
                OutputHeight = succeeded ? task.OutputHeight : null,
                OutputSize = succeeded ? task.OutputSize : null,
                MimeType = succeeded ? task.OutputMimeType : null,
                FailureCode = failed ? task.FailureCode : null,
                FailureStage = failed ? task.FailureStage : null,
                Retryable = failed ? task.Retryable : null,
                RefundedPoints = failed ? task.RefundedPoints : task.Status == 1 ? 0 : null
            };
        }).ToArray();
        var firstTask = orderedTasks[0];
        var firstSucceeded = results.FirstOrDefault(x => x.Status == "succeeded" && !x.IsDeleted && x.Url is not null);
        var batchStatus = orderedTasks.Any(x => IsTaskActive(x.Status))
            ? "processing"
            : orderedTasks.All(x => x.Status == 1)
                ? "succeeded"
                : orderedTasks.All(x => x.Status == 2)
                    ? "failed"
                    : "partial";
        var compatibilityWidth = firstTask.SizeMode == AiImageCatalogService.AutoSizeMode
            ? firstSucceeded?.OutputWidth ?? 0
            : firstTask.Width;
        var compatibilityHeight = firstTask.SizeMode == AiImageCatalogService.AutoSizeMode
            ? firstSucceeded?.OutputHeight ?? 0
            : firstTask.Height;
        var compatibilitySize = firstTask.SizeMode == AiImageCatalogService.AutoSizeMode
            ? firstSucceeded?.OutputSize ?? AiImageCatalogService.AutoSizeMode
            : firstTask.Size;
        return new GenerateAiImageResponse
        {
            TaskId = firstTask.Id,
            TaskIds = orderedIds,
            SourcePromptId = firstTask.SourcePromptId,
            ModelName = firstTask.ModelName ?? string.Empty,
            ModelCode = firstTask.ModelCode ?? firstTask.ModelName ?? string.Empty,
            SizeContractVersion = firstTask.SizeContractVersion,
            CatalogVersion = catalogVersion,
            SizeMode = firstTask.SizeMode,
            RequestState = GetRequestState(orderedTasks, orderedTasks.Length),
            BatchStatus = batchStatus,
            Results = results,
            Prompt = firstTask.Prompt,
            ResolutionCode = firstTask.ResolutionCode ?? string.Empty,
            QualityCode = firstTask.QualityCode,
            AspectRatioCode = firstTask.SizeMode == AiImageCatalogService.AutoSizeMode ? "auto" : firstTask.AspectRatioCode ?? string.Empty,
            Width = compatibilityWidth,
            Height = compatibilityHeight,
            Size = compatibilitySize,
            Quality = firstTask.Quality,
            MimeType = firstSucceeded?.MimeType ?? string.Empty,
            Url = firstSucceeded?.Url ?? string.Empty,
            Urls = results.Where(x => !x.IsDeleted && x.Status == "succeeded" && x.Url is not null).Select(x => x.Url!).ToArray(),
            MaskImageUrl = firstTask.MaskImageUrl,
            ReferenceImageUrls = DeserializeReferenceImageUrls(firstTask.ReferenceImageUrls)
        };
    }

    public async Task<long> CreateAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken)
    {
        var taskIds = await CreateTasksAsync(request, cancellationToken);
        return taskIds[0];
    }

    public async Task<IReadOnlyList<long>> CreateTasksAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken) =>
        (await CreateTasksResponseAsync(request, cancellationToken)).TaskIds;

    public async Task<CreateAiImageTasksResponse> CreateTasksResponseAsync(
        CreateAiImageTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SizeMode is not null || request.CatalogVersion is not null)
        {
            var versioned = await CreateVersionedTasksAsync(request, cancellationToken);
            return BuildCreateTasksResponse(
                versioned.TaskIds,
                await GetRequestStateAsync(versioned.RequestId, cancellationToken));
        }

        var taskIds = await CreateLegacyTasksAsync(request, cancellationToken);
        return BuildCreateTasksResponse(taskIds, "active");
    }

    private async Task<IReadOnlyList<long>> CreateLegacyTasksAsync(
        CreateAiImageTaskRequest request,
        CancellationToken cancellationToken)
    {

        var prompt = AiImagePromptValidator.Validate(request.Prompt);
        var modelCode = ResolveRequestModelCode(request.ModelCode, request.ModelName, AiImageModelConfigService.DefaultGptModelCode);
        var imageCount = ValidateImageCount(request.ImageCount, modelCode);
        var parameters = await ResolveParametersAsync(new ResolveAiImageParametersRequest
        {
            Resolution = request.Resolution,
            ResolutionCode = request.ResolutionCode,
            QualityCode = request.QualityCode,
            AspectRatioCode = request.AspectRatioCode
        }, cancellationToken);
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var resolvedReferenceUrls = await mediaAssetService.ResolveOwnedReferenceUrlsAsync(
            userId,
            currentUser.IsSuperAdmin,
            request.ReferenceAssetIds,
            request.ReferenceImageUrls,
            cancellationToken);
        var referenceImageUrls = ValidateReferenceImageUrls(resolvedReferenceUrls);
        var resolvedMaskUrl = await mediaAssetService.ResolveOwnedMaskUrlAsync(
            userId,
            currentUser.IsSuperAdmin,
            request.MaskAssetId,
            request.MaskImageUrl,
            cancellationToken);
        var maskImageUrl = ValidateMaskImageUrl(resolvedMaskUrl, referenceImageUrls);
        var sourcePromptId = await ValidateSourcePromptIdAsync(request.SourcePromptId, cancellationToken);
        foreach (var referenceImageUrl in referenceImageUrls)
        {
            _ = ResolveReferenceImageFile(referenceImageUrl);
        }
        if (!string.IsNullOrWhiteSpace(maskImageUrl))
        {
            _ = ResolveMaskImageFile(maskImageUrl);
        }
        var siteId = await ResolveAiImageSiteIdAsync(request.SiteId, cancellationToken);
        var modelConfig = await modelConfigService.ResolveAsync(modelCode, parameters.ResolutionCode, cancellationToken);
        var pointCostPerImage = await pointService.GetImageGenerateCostAsync(
            modelConfig.ModelCode,
            parameters.ResolutionCode ?? string.Empty,
            parameters.QualityCode,
            1,
            cancellationToken);
        var pointCost = checked(pointCostPerImage * imageCount);
        await ExpireStaleTasksAsync(userId, cancellationToken);

        var negativePrompt = string.IsNullOrWhiteSpace(request.NegativePrompt) ? null : request.NegativePrompt.Trim();
        if (negativePrompt?.Length > 2000)
        {
            throw new AppException(ErrorCodes.BadRequest, "Negative prompt is too long");
        }
        object canonicalRequest = sourcePromptId.HasValue
            ? new
            {
                Endpoint = "gpt-image",
                SourcePromptId = sourcePromptId.Value,
                Prompt = prompt,
                NegativePrompt = negativePrompt,
                ModelCode = modelConfig.ModelCode,
                ImageCount = imageCount,
                parameters.ResolutionCode,
                parameters.QualityCode,
                parameters.AspectRatioCode,
                ReferenceImageUrls = referenceImageUrls,
                MaskImageUrl = maskImageUrl
            }
            : new
            {
                Endpoint = "gpt-image",
                Prompt = prompt,
                NegativePrompt = negativePrompt,
                ModelCode = modelConfig.ModelCode,
                ImageCount = imageCount,
                parameters.ResolutionCode,
                parameters.QualityCode,
                parameters.AspectRatioCode,
                ReferenceImageUrls = referenceImageUrls,
                MaskImageUrl = maskImageUrl
            };
        var identity = AiImageIdempotency.Create(request.IdempotencyKey, canonicalRequest);
        var taskKeyHashes = Enumerable.Range(0, imageCount)
            .Select(index => AiImageIdempotency.DeriveTaskKeyHash(identity.KeyHash, index))
            .ToArray();
        var existingTasks = await FindIdempotentTasksAsync(userId, identity, taskKeyHashes, imageCount, cancellationToken);
        if (existingTasks.Count > 0)
        {
            return existingTasks.Select(task => task.Id).ToArray();
        }

        await userConsentService.EnsureAiProcessingConsentAsync(userId, modelConfig.ConsentProviderCode, cancellationToken);

        var promptPolicyVersion = await promptFilter.EnsureAllAllowedAsync(
            [
                new AiPromptFilterText("prompt", prompt),
                new AiPromptFilterText("negativePrompt", negativePrompt)
            ],
            cancellationToken);

        var admission = await admissionService.ReserveAsync(
            userId,
            identity.KeyHash,
            identity.RequestFingerprint,
            imageCount,
            pointCost,
            cancellationToken);
        if (admission.IsDuplicate)
        {
            var duplicateTasks = await WaitForIdempotentTasksAsync(
                userId,
                identity,
                taskKeyHashes,
                imageCount,
                cancellationToken);
            return duplicateTasks.Select(task => task.Id).ToArray();
        }

        var createdAt = HongKongNow();
        var serializedReferenceImageUrls = referenceImageUrls.Count == 0 ? null : JsonSerializer.Serialize(referenceImageUrls);
        var entities = taskKeyHashes.Select(taskKeyHash => new AiImageTaskEntity
        {
            SiteId = siteId,
            UserId = userId,
            SourcePromptId = sourcePromptId,
            Prompt = prompt,
            NegativePrompt = negativePrompt,
            PromptPolicyVersion = promptPolicyVersion,
            PromptCheckedAt = createdAt,
            ModelName = modelConfig.ModelCode,
            ImageCount = 1,
            CompletedImageCount = 0,
            IdempotencyKey = taskKeyHash,
            RequestFingerprint = identity.RequestFingerprint,
            PointCost = pointCostPerImage,
            BillingStatus = 0,
            ResolutionCode = parameters.ResolutionCode,
            QualityCode = parameters.QualityCode,
            AspectRatioCode = parameters.AspectRatioCode,
            Width = parameters.Width.GetValueOrDefault(),
            Height = parameters.Height.GetValueOrDefault(),
            Size = parameters.Size,
            Quality = parameters.ProviderQuality,
            ReferenceImageUrls = serializedReferenceImageUrls,
            MaskImageUrl = maskImageUrl,
            Status = 0,
            CreatedAt = createdAt,
            IsDeleted = false
        })
            .ToArray();

        var persisted = false;
        try
        {
            var reservation = await pointService.ReserveImageTasksAsync(
                entities,
                modelConfig.ModelCode,
                parameters.ResolutionCode ?? string.Empty,
                parameters.QualityCode,
                cancellationToken);
            persisted = reservation.Created;
            if (!reservation.Created)
            {
                await admissionService.CancelAsync(admission);
                return reservation.TaskIds;
            }

            await admissionService.BindTaskAsync(admission, entities[0].Id, cancellationToken);
            foreach (var entity in entities)
            {
                if (!taskQueue.TryQueue(entity.Id))
                {
                    throw new AppException(ErrorCodes.ServiceUnavailable, "AI image task queue is full");
                }
            }

            return entities.Select(entity => entity.Id).ToArray();
        }
        catch (Exception ex)
        {
            if (persisted)
            {
                foreach (var entity in entities)
                {
                    var settlement = await pointService.SettleImageTaskAsync(
                        entity.Id,
                        2,
                        null,
                        ex is AppException ? ex.Message : "AI image task admission failed",
                        0,
                        CancellationToken.None);
                    if (settlement.Transitioned)
                    {
                        await admissionService.CompleteAsync(entity, 0, settlement.RefundedPoints);
                    }
                }
            }
            else
            {
                await admissionService.CancelAsync(admission);
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<AiImageTaskEntity>> FindIdempotentTasksAsync(
        long userId,
        AiImageRequestIdentity identity,
        IReadOnlyList<string> taskKeyHashes,
        int requestedImageCount,
        CancellationToken cancellationToken)
    {
        var keyArray = taskKeyHashes.ToArray();
        var existingTasks = await db.Queryable<AiImageTaskEntity>()
            .Where(x => x.UserId == userId && keyArray.Contains(x.IdempotencyKey))
            .ToListAsync(cancellationToken);
        if (existingTasks.Any(existing =>
                !string.Equals(existing.RequestFingerprint, identity.RequestFingerprint, StringComparison.Ordinal)))
        {
            throw new ConflictException("Idempotency key was already used with a different request");
        }

        if (existingTasks.Count == 0)
        {
            return [];
        }

        // Keep idempotent retries compatible with tasks created before multi-image requests
        // were split into one task per image.
        if (existingTasks.Count == 1
            && existingTasks[0].IdempotencyKey == identity.KeyHash
            && existingTasks[0].ImageCount == requestedImageCount)
        {
            return existingTasks;
        }

        if (existingTasks.Count != taskKeyHashes.Count)
        {
            return [];
        }

        var order = taskKeyHashes
            .Select((key, index) => (key, index))
            .ToDictionary(x => x.key, x => x.index, StringComparer.Ordinal);
        return existingTasks.OrderBy(task => order[task.IdempotencyKey]).ToArray();
    }

    private async Task<IReadOnlyList<AiImageTaskEntity>> WaitForIdempotentTasksAsync(
        long userId,
        AiImageRequestIdentity identity,
        IReadOnlyList<string> taskKeyHashes,
        int requestedImageCount,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var existingTasks = await FindIdempotentTasksAsync(
                userId,
                identity,
                taskKeyHashes,
                requestedImageCount,
                cancellationToken);
            if (existingTasks.Count > 0)
            {
                return existingTasks;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new ConflictException("The idempotent AI image request is still being committed; retry shortly");
    }

    public async Task SetFavoriteAsync(long id, FavoriteAiImageRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var imageUrl = ValidateFavoriteImageUrl(request.ImageUrl);
        var task = await db.Queryable<AiImageTaskEntity>()
            .Where(x => x.Id == id && !x.IsDeleted)
            .WhereIF(!currentUser.IsSuperAdmin, x => x.UserId == currentUserId)
            .FirstAsync(cancellationToken);

        if (task is null)
        {
            throw new NotFoundException($"AI image task does not exist: {id}");
        }

        var resultUrls = DeserializeImageUrls(task.ResultUrls);
        if (!resultUrls.Contains(imageUrl, StringComparer.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.BadRequest, "Image URL does not belong to this task");
        }

        var favorite = await db.Queryable<AiImageFavoriteEntity>()
            .FirstAsync(x => x.TaskId == id && x.UserId == currentUserId && x.ImageUrl == imageUrl, cancellationToken);

        if (request.IsFavorite)
        {
            if (favorite is null)
            {
                await db.Insertable(new AiImageFavoriteEntity
                {
                    TaskId = id,
                    UserId = currentUserId,
                    ImageUrl = imageUrl,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                }).ExecuteCommandAsync();
            }
            else if (favorite.IsDeleted)
            {
                await db.Updateable<AiImageFavoriteEntity>()
                    .SetColumns(x => new AiImageFavoriteEntity { IsDeleted = false, UpdatedAt = DateTime.UtcNow })
                    .Where(x => x.Id == favorite.Id)
                    .ExecuteCommandAsync();
            }

            return;
        }

        if (favorite is not null && !favorite.IsDeleted)
        {
            await db.Updateable<AiImageFavoriteEntity>()
                .SetColumns(x => new AiImageFavoriteEntity { IsDeleted = true, UpdatedAt = DateTime.UtcNow })
                .Where(x => x.Id == favorite.Id)
                .ExecuteCommandAsync();
        }
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var task = await db.Queryable<AiImageTaskEntity>()
            .Where(x => x.Id == id && !x.IsDeleted)
            .WhereIF(!currentUser.IsSuperAdmin, x => x.UserId == currentUserId)
            .FirstAsync(cancellationToken);
        if (task is null)
        {
            throw new NotFoundException($"AI image task does not exist: {id}");
        }

        if (IsTaskActive(task.Status))
        {
            throw new ConflictException("An active AI image task cannot be deleted");
        }

        var affected = await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity { IsDeleted = true, UpdatedAt = HongKongNow() })
            .Where(x => x.Id == id && !x.IsDeleted)
            .WhereIF(!currentUser.IsSuperAdmin, x => x.UserId == currentUserId)
            .ExecuteCommandAsync();

        if (affected == 0)
        {
            throw new NotFoundException($"AI image task does not exist: {id}");
        }
    }

    private async Task ExpireStaleTasksAsync(long? userId, CancellationToken cancellationToken)
    {
        var now = HongKongNow();
        var candidates = await db.Queryable<AiImageTaskEntity>()
            .Where(x => !x.IsDeleted && (x.Status == 0 || x.Status == 3))
            .WhereIF(userId.HasValue, x => x.UserId == userId!.Value)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var task in candidates.Where(x => IsTaskExpired(x, now)))
        {
            await ExpireStaleTaskAsync(task, cancellationToken);
        }
    }

    private async Task ExpireStaleTaskAsync(AiImageTaskEntity task, CancellationToken cancellationToken)
    {
        if (!IsTaskExpired(task, HongKongNow()))
        {
            return;
        }

        var resultUrls = DeserializeImageUrls(task.ResultUrls);
        var timeout = task.Status == 0
            ? AiImageTaskProcessor.ResolvePendingTaskTimeout()
            : AiImageTaskProcessor.ResolveTaskTimeout(task.ImageCount);
        var message = $"AI image generation expired after {timeout.TotalMinutes:0} minutes.";
        var settlement = await pointService.SettleImageTaskAsync(
            task.Id,
            2,
            resultUrls.Count == 0 ? task.ResultUrls : JsonSerializer.Serialize(resultUrls),
            message,
            resultUrls.Count,
            cancellationToken);
        if (settlement.Transitioned)
        {
            await admissionService.CompleteAsync(task, settlement.CompletedImageCount, settlement.RefundedPoints);
        }
    }

    private static bool IsTaskExpired(AiImageTaskEntity task, DateTime now)
    {
        return task.Status switch
        {
            0 => task.CreatedAt.Add(AiImageTaskProcessor.ResolvePendingTaskTimeout()) <= now,
            3 => (task.StartedAt ?? task.CreatedAt).Add(AiImageTaskProcessor.ResolveTaskTimeout(task.ImageCount)) <= now,
            _ => false
        };
    }

    private static DateTime HongKongNow()
    {
        return DateTime.UtcNow.AddHours(8);
    }

    private async Task<Dictionary<long, IReadOnlyList<string>>> GetFavoriteUrlLookupAsync(IReadOnlyCollection<long> taskIds, long userId, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var favorites = await db.Queryable<AiImageFavoriteEntity>()
            .Where(x => taskIds.Contains(x.TaskId) && x.UserId == userId && !x.IsDeleted)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return favorites
            .GroupBy(x => x.TaskId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(y => y.ImageUrl).ToArray());
    }

    private static DateTime? ResolveEndDateExclusive(DateTime? endDate)
    {
        if (!endDate.HasValue)
        {
            return null;
        }

        return endDate.Value.TimeOfDay == TimeSpan.Zero
            ? endDate.Value.Date.AddDays(1)
            : endDate.Value;
    }

    private static string ValidateFavoriteImageUrl(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new AppException(ErrorCodes.BadRequest, "Image URL is required");
        }

        var trimmed = imageUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.BadRequest, "Image URL must be an internal URL");
        }

        return uri.OriginalString;
    }

    private async Task<long> ResolveAiImageSiteIdAsync(long requestedSiteId, CancellationToken cancellationToken)
    {
        if (requestedSiteId > 0)
        {
            var exists = await db.Queryable<SysSiteEntity>()
                .AnyAsync(x => x.Id == requestedSiteId && !x.IsDeleted, cancellationToken);
            if (!exists)
            {
                throw new NotFoundException($"站点不存在: {requestedSiteId}");
            }

            return requestedSiteId;
        }

        var siteId = await db.Queryable<SysSiteEntity>()
            .Where(x => x.SiteCode == AiImageSiteCode && !x.IsDeleted)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);
        if (siteId <= 0)
        {
            throw new NotFoundException($"站点不存在: {AiImageSiteCode}");
        }

        return siteId;
    }

    private async Task<long?> ValidateSourcePromptIdAsync(long? sourcePromptId, CancellationToken cancellationToken)
    {
        if (!sourcePromptId.HasValue)
        {
            return null;
        }
        if (sourcePromptId.Value <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "sourcePromptId must be a positive integer");
        }

        var exists = await db.Queryable<PromptLibraryItemEntity>()
            .AnyAsync(
                x => x.Id == sourcePromptId.Value
                    && x.Source == promptLibraryOptions.Value.Source
                    && x.IsActive,
                cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Prompt does not exist: {sourcePromptId.Value}");
        }

        return sourcePromptId.Value;
    }

    private async Task<SavedImage> SaveImageAsync(string base64, long ownerUserId, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image generation returned invalid base64 image data");
        }

        return await SaveImageBytesAsync(bytes, ownerUserId, cancellationToken);
    }

    private async Task<SavedImage> DownloadImageAsBase64Async(
        string imageUrl,
        ResolvedAiImageModelConfig route,
        bool versioned,
        long ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid, "Image provider returned an invalid result URL");
        }
        var allowedHosts = sizeModeOptions.Value.ResultAllowedHosts;
        if (versioned && allowedHosts.Length == 0
            || allowedHosts.Length > 0 && !allowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid, "Image provider result URL is not approved");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = null;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image generation returned an image URL that could not be downloaded");
        }
        var maxBytes = versioned ? sizeModeOptions.Value.AutoMaxOutputBytes : MaxGeneratedImageSizeBytes;
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > maxBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid, "Image provider result exceeded the allowed size");
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maxBytes)
            {
                throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid, "Image provider result exceeded the allowed size");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image generation returned empty image data");
        }

        return await SaveImageBytesAsync(buffer.ToArray(), ownerUserId, cancellationToken);
    }

    private static bool IsTaskActive(int status) => status is 0 or 3;

    private async Task<SavedImage> SaveImageBytesAsync(byte[] bytes, long ownerUserId, CancellationToken cancellationToken)
    {
        var image = await ImageUploadValidator.ValidateAsync(bytes, MaxGeneratedImageSizeBytes, cancellationToken);
        var storageKey = $"{ownerUserId}/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{image.Extension}";
        var savePath = mediaPathResolver.ResolveFilePath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await File.WriteAllBytesAsync(savePath, image.Content, cancellationToken);

        return new SavedImage(
            $"/api/media/ai/{storageKey.Replace('\\', '/')}",
            Convert.ToBase64String(image.Content),
            image.MimeType,
            image.Width,
            image.Height);
    }

    private static AiImageTaskDto MapTaskDto(
        AiImageTaskEntity entity,
        IReadOnlyList<string>? favoriteUrls = null,
        string? catalogVersion = null)
    {
        var normalizedFavoriteUrls = favoriteUrls ?? [];
        var resultUrls = DeserializeImageUrls(entity.ResultUrls);
        return new AiImageTaskDto
        {
            Id = entity.Id,
            TaskId = entity.Id,
            SiteId = entity.SiteId,
            SourcePromptId = entity.SourcePromptId,
            Prompt = entity.Prompt,
            ModelName = entity.ModelName ?? string.Empty,
            ModelCode = entity.ModelCode ?? entity.ModelName ?? string.Empty,
            SizeContractVersion = entity.SizeContractVersion,
            CatalogVersion = catalogVersion,
            SizeMode = entity.SizeMode,
            RequestedSize = entity.RequestedSize,
            RequestedResolutionCode = entity.ResolutionCode,
            RequestedAspectRatioCode = entity.SizeContractVersion == AiImageCatalogService.SizeContractVersion
                ? entity.AspectRatioCode
                : entity.AspectRatioCode,
            RequestedWidth = entity.RequestedWidth,
            RequestedHeight = entity.RequestedHeight,
            OutputWidth = entity.OutputWidth,
            OutputHeight = entity.OutputHeight,
            OutputSize = entity.OutputSize,
            OutputMimeType = entity.OutputMimeType,
            ImageCount = entity.ImageCount,
            CompletedImageCount = entity.CompletedImageCount,
            PointCost = entity.PointCost,
            BillingStatus = entity.BillingStatus,
            RefundedPoints = entity.RefundedPoints,
            ResolutionCode = entity.ResolutionCode ?? string.Empty,
            QualityCode = entity.QualityCode,
            AspectRatioCode = entity.AspectRatioCode ?? string.Empty,
            Width = entity.Width,
            Height = entity.Height,
            Size = entity.Size,
            Quality = entity.Quality,
            ReferenceImageUrls = DeserializeReferenceImageUrls(entity.ReferenceImageUrls),
            MaskImageUrl = entity.MaskImageUrl,
            ResultUrls = resultUrls,
            FavoriteUrls = normalizedFavoriteUrls,
            IsFavorite = normalizedFavoriteUrls.Count > 0,
            ErrorMessage = entity.ErrorMessage,
            FailureCode = entity.FailureCode,
            FailureStage = entity.FailureStage,
            Retryable = entity.Retryable,
            CreatedAt = HongKongTimeToUtc(entity.CreatedAt),
            UpdatedAt = entity.UpdatedAt.HasValue
                ? HongKongTimeToUtc(entity.UpdatedAt.Value)
                : null,
            Status = MapTaskStatus(entity.Status),
            StatusCode = entity.Status,
            Progress = CalculateTaskProgress(entity),
            PollAfterSeconds = entity.Status switch { 0 => 3, 3 => 5, _ => 0 },
            ExpiresAt = HongKongTimeToUtc(entity.CreatedAt.AddMinutes(120)),
            Assets = resultUrls.Select(url => new AiImageTaskAssetDto { Url = url }).ToArray()
        };
    }

    private async Task<IReadOnlyDictionary<long, string>> GetCatalogVersionLookupAsync(
        IReadOnlyList<AiImageTaskEntity> tasks,
        CancellationToken cancellationToken)
    {
        var releaseIds = tasks
            .Where(x => x.ModelReleaseId.HasValue)
            .Select(x => x.ModelReleaseId!.Value)
            .Distinct()
            .ToArray();
        if (releaseIds.Length == 0)
        {
            return new Dictionary<long, string>();
        }
        var releases = await db.Queryable<AiImageModelReleaseEntity>()
            .Where(x => releaseIds.Contains(x.Id))
            .Select(x => new AiImageModelReleaseEntity { Id = x.Id, CatalogVersion = x.CatalogVersion })
            .ToListAsync(cancellationToken);
        return releases.ToDictionary(x => x.Id, x => x.CatalogVersion);
    }

    private static CreateAiImageTasksResponse BuildCreateTasksResponse(
        IReadOnlyList<long> taskIds,
        string requestState) => new()
    {
        Id = taskIds[0],
        TaskId = taskIds[0],
        Ids = taskIds,
        TaskIds = taskIds,
        RequestState = requestState
    };

    private async Task<string> GetRequestStateAsync(long requestId, CancellationToken cancellationToken)
    {
        var taskIds = await db.Queryable<AiImageRequestTaskEntity>()
            .Where(x => x.RequestId == requestId)
            .Select(x => x.TaskId)
            .ToListAsync(cancellationToken);
        if (taskIds.Count == 0)
        {
            return "deleted";
        }
        var tasks = await db.Queryable<AiImageTaskEntity>()
            .Where(x => taskIds.Contains(x.Id))
            .Select(x => new AiImageTaskEntity { Id = x.Id, IsDeleted = x.IsDeleted })
            .ToListAsync(cancellationToken);
        return GetRequestState(tasks, taskIds.Count);
    }

    private static string GetRequestState(IReadOnlyList<AiImageTaskEntity> tasks, int expectedCount)
    {
        var deletedCount = tasks.Count(x => x.IsDeleted) + Math.Max(0, expectedCount - tasks.Count);
        return deletedCount == 0 ? "active" : deletedCount >= expectedCount ? "deleted" : "partially_deleted";
    }

    private async Task<VersionedImageTaskBatchReservationResult> CreateVersionedTasksAsync(
        CreateAiImageTaskRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "User is not authenticated");
        var keyHash = AiImageIdempotency.HashKey(request.IdempotencyKey);
        var existingRequest = await db.Queryable<AiImageRequestEntity>()
            .FirstAsync(x => x.UserId == userId && x.IdempotencyKeyHash == keyHash, cancellationToken);
        var intent = NormalizeVersionedIntent(request);
        var identity = AiImageIdempotency.Create(request.IdempotencyKey, intent.CanonicalPayload);
        if (existingRequest is not null)
        {
            return await LoadVersionedBatchAsync(existingRequest, identity.RequestFingerprint, cancellationToken);
        }

        var resolution = await catalogService.ResolveAsync(
            new ResolveAiImageParametersRequest
            {
                ModelCode = intent.ModelCode,
                SizeMode = intent.SizeMode,
                CatalogVersion = request.CatalogVersion,
                Resolution = request.Resolution,
                ResolutionCode = request.ResolutionCode,
                QualityCode = intent.QualityCode,
                AspectRatioCode = request.AspectRatioCode
            },
            rolloutPolicy.GetClientContext(),
            cancellationToken);

        var resolvedReferenceUrls = await mediaAssetService.ResolveOwnedReferenceUrlsAsync(
            userId,
            false,
            request.ReferenceAssetIds,
            request.ReferenceImageUrls,
            cancellationToken);
        var referenceImageUrls = ValidateReferenceImageUrls(resolvedReferenceUrls);
        var resolvedMaskUrl = await mediaAssetService.ResolveOwnedMaskUrlAsync(
            userId,
            false,
            request.MaskAssetId,
            request.MaskImageUrl,
            cancellationToken);
        var maskImageUrl = ValidateMaskImageUrl(resolvedMaskUrl, referenceImageUrls);
        var sourcePromptId = await ValidateSourcePromptIdAsync(request.SourcePromptId, cancellationToken);
        foreach (var referenceImageUrl in referenceImageUrls)
        {
            _ = ResolveReferenceImageFile(referenceImageUrl);
        }
        if (!string.IsNullOrWhiteSpace(maskImageUrl))
        {
            _ = ResolveMaskImageFile(maskImageUrl);
        }

        foreach (var providerCode in resolution.ConsentProviderCodes)
        {
            await userConsentService.EnsureAiProcessingConsentAsync(userId, providerCode, cancellationToken);
        }
        var promptPolicyVersion = await promptFilter.EnsureAllAllowedAsync(
            [
                new AiPromptFilterText("prompt", intent.Prompt),
                new AiPromptFilterText("negativePrompt", intent.NegativePrompt)
            ],
            cancellationToken);
        var siteId = await ResolveAiImageSiteIdAsync(request.SiteId, cancellationToken);
        await ExpireStaleTasksAsync(userId, cancellationToken);

        var pointCost = checked(resolution.Price.Points * intent.ImageCount);
        var admission = await admissionService.ReserveAsync(
            userId,
            identity.KeyHash,
            identity.RequestFingerprint,
            intent.ImageCount,
            pointCost,
            cancellationToken);
        if (admission.IsDuplicate)
        {
            return await WaitForVersionedBatchAsync(userId, identity, cancellationToken);
        }

        var createdAt = HongKongNow();
        var taskInputs = await BuildVersionedTaskInputsAsync(
            request,
            userId,
            intent.ImageCount,
            createdAt,
            cancellationToken);
        var requestEntity = new AiImageRequestEntity
        {
            UserId = userId,
            IdempotencyKeyHash = identity.KeyHash,
            CanonicalPayloadHash = identity.RequestFingerprint,
            CanonicalizationVersion = AiImageCatalogService.SizeContractVersion,
            NormalizationProfile = "native-v1",
            SizeContractVersion = AiImageCatalogService.SizeContractVersion,
            ModelReleaseId = resolution.Release.Id,
            AdmissionReservationId = admission.OwnerToken,
            AdmissionQuotaDate = admission.QuotaDate,
            ReservedPointCost = pointCost,
            RequestedImageCount = intent.ImageCount,
            TaskCount = intent.ImageCount,
            LegacyBatchShape = "split-task-per-image",
            Status = "active",
            CreatedAt = createdAt
        };
        var taskKeyHashes = Enumerable.Range(0, intent.ImageCount)
            .Select(index => AiImageIdempotency.DeriveTaskKeyHash(identity.KeyHash, index))
            .ToArray();
        var serializedReferences = referenceImageUrls.Count == 0 ? null : JsonSerializer.Serialize(referenceImageUrls);
        var parameters = resolution.Parameters;
        var entities = taskKeyHashes.Select(taskKeyHash => new AiImageTaskEntity
        {
            SiteId = siteId,
            UserId = userId,
            SourcePromptId = sourcePromptId,
            Prompt = intent.Prompt,
            NegativePrompt = intent.NegativePrompt,
            PromptPolicyVersion = promptPolicyVersion,
            PromptCheckedAt = createdAt,
            ModelName = resolution.Release.ModelCode,
            ModelCode = resolution.Release.ModelCode,
            SizeContractVersion = AiImageCatalogService.SizeContractVersion,
            SizeMode = parameters.SizeMode,
            RequestedSize = parameters.RequestedSize,
            RequestedWidth = parameters.Width,
            RequestedHeight = parameters.Height,
            ModelReleaseId = resolution.Release.Id,
            PriceId = resolution.Price.Id,
            PriceReleaseId = resolution.Release.Id,
            UnitPointCost = resolution.Price.Points,
            ImageCount = 1,
            CompletedImageCount = 0,
            IdempotencyKey = taskKeyHash,
            RequestFingerprint = identity.RequestFingerprint,
            PointCost = resolution.Price.Points,
            BillingStatus = 0,
            RefundedPoints = 0,
            ResolutionCode = parameters.ResolutionCode,
            QualityCode = parameters.QualityCode,
            AspectRatioCode = parameters.AspectRatioCode,
            Width = parameters.Width.GetValueOrDefault(),
            Height = parameters.Height.GetValueOrDefault(),
            Size = parameters.Size,
            Quality = parameters.ProviderQuality,
            ReferenceImageUrls = serializedReferences,
            MaskImageUrl = maskImageUrl,
            Status = 0,
            CreatedAt = createdAt,
            IsDeleted = false
        }).ToArray();

        try
        {
            var reservation = await pointService.ReserveVersionedImageTasksAsync(
                requestEntity,
                entities,
                taskInputs,
                resolution.Price.Id,
                cancellationToken);
            if (!reservation.Created)
            {
                await admissionService.CancelAsync(admission);
                return reservation;
            }

            try
            {
                await admissionService.BindBatchAsync(
                    admission,
                    reservation.RequestId,
                    entities.Select((entity, ordinal) => new AiImageAdmissionTask(
                        entity.Id,
                        ordinal,
                        entity.ImageCount,
                        entity.PointCost)).ToArray(),
                    cancellationToken);
            }
            catch (AppException ex)
            {
                logger.LogWarning(
                    "Versioned AI request was committed but Redis binding is pending. RequestId={RequestId}, FailureCode={FailureCode}",
                    requestEntity.Id,
                    ex.MachineCode);
                return reservation;
            }

            foreach (var entity in entities)
            {
                if (!taskQueue.TryQueue(entity.Id))
                {
                    break;
                }
                await MarkOutboxDispatchedAsync(entity.Id, cancellationToken);
            }
            return reservation;
        }
        catch
        {
            await admissionService.CancelAsync(admission);
            var winner = await db.Queryable<AiImageRequestEntity>()
                .FirstAsync(x => x.UserId == userId && x.IdempotencyKeyHash == identity.KeyHash, CancellationToken.None);
            if (winner is not null)
            {
                return await LoadVersionedBatchAsync(winner, identity.RequestFingerprint, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<VersionedImageTaskBatchReservationResult> LoadVersionedBatchAsync(
        AiImageRequestEntity request,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CanonicalPayloadHash, fingerprint, StringComparison.Ordinal))
        {
            throw new AppException(
                ErrorCodes.Conflict,
                MachineErrorCodes.IdempotencyConflict,
                "The idempotency key was already used with a different AI image request.");
        }
        var taskIds = await db.Queryable<AiImageRequestTaskEntity>()
            .Where(x => x.RequestId == request.Id)
            .OrderBy(x => x.TaskOrdinal)
            .Select(x => x.TaskId)
            .ToListAsync(cancellationToken);
        if (taskIds.Count != request.TaskCount)
        {
            throw new AppException(
                ErrorCodes.Conflict,
                MachineErrorCodes.LegacyIdempotencyUnverifiable,
                "The durable AI image request batch is incomplete.");
        }
        return new VersionedImageTaskBatchReservationResult(request.Id, taskIds, false);
    }

    private async Task<VersionedImageTaskBatchReservationResult> WaitForVersionedBatchAsync(
        long userId,
        AiImageRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var request = await db.Queryable<AiImageRequestEntity>()
                .FirstAsync(x => x.UserId == userId && x.IdempotencyKeyHash == identity.KeyHash, cancellationToken);
            if (request is not null)
            {
                return await LoadVersionedBatchAsync(request, identity.RequestFingerprint, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        throw new ConflictException("The idempotent AI image request is still being committed; retry shortly");
    }

    private async Task MarkOutboxDispatchedAsync(long taskId, CancellationToken cancellationToken)
    {
        await db.Updateable<AiImageTaskOutboxEntity>()
            .SetColumns(x => new AiImageTaskOutboxEntity
            {
                Status = "dispatched",
                UpdatedAt = HongKongNow()
            })
            .Where(x => x.TaskId == taskId && x.Status == "pending")
            .ExecuteCommandAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AiImageTaskInputEntity>> BuildVersionedTaskInputsAsync(
        CreateAiImageTaskRequest request,
        long userId,
        int taskCount,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var referenceAssetIds = NormalizeOrderedValues(request.ReferenceAssetIds);
        var maskAssetId = string.IsNullOrWhiteSpace(request.MaskAssetId) ? null : request.MaskAssetId.Trim();
        var allAssetIds = referenceAssetIds
            .Concat(maskAssetId is null ? [] : new[] { maskAssetId })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var assets = allAssetIds.Length == 0
            ? []
            : await db.Queryable<MediaAssetEntity>()
                .Where(x => allAssetIds.Contains(x.AssetId) && x.OwnerUserId == userId && !x.IsDeleted)
                .ToListAsync(cancellationToken);
        var assetLookup = assets.ToDictionary(x => x.AssetId, StringComparer.Ordinal);
        if (assetLookup.Count != allAssetIds.Length)
        {
            throw new AppException(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "AI image input does not exist");
        }

        var legacyReferences = NormalizeOrderedValues(request.ReferenceImageUrls);
        var legacyMask = string.IsNullOrWhiteSpace(request.MaskImageUrl) ? null : request.MaskImageUrl.Trim();
        var inputs = new List<AiImageTaskInputEntity>();
        for (var taskOrdinal = 0; taskOrdinal < taskCount; taskOrdinal++)
        {
            var inputOrdinal = 0;
            foreach (var assetId in referenceAssetIds)
            {
                var asset = assetLookup[assetId];
                inputs.Add(new AiImageTaskInputEntity
                {
                    RequestTaskOrdinal = taskOrdinal,
                    Role = "reference",
                    InputOrdinal = inputOrdinal++,
                    InputKind = "asset",
                    AssetId = asset.AssetId,
                    OwnerUserId = userId,
                    StorageKey = asset.StorageKey,
                    ContentSha256 = asset.Sha256,
                    CreatedAt = createdAt
                });
            }
            foreach (var legacyUrl in legacyReferences)
            {
                inputs.Add(new AiImageTaskInputEntity
                {
                    RequestTaskOrdinal = taskOrdinal,
                    Role = "reference",
                    InputOrdinal = inputOrdinal++,
                    InputKind = "legacy_url",
                    OwnerUserId = userId,
                    LegacyUrl = legacyUrl,
                    CreatedAt = createdAt
                });
            }
            if (maskAssetId is not null)
            {
                var asset = assetLookup[maskAssetId];
                inputs.Add(new AiImageTaskInputEntity
                {
                    RequestTaskOrdinal = taskOrdinal,
                    Role = "mask",
                    InputOrdinal = 0,
                    InputKind = "asset",
                    AssetId = asset.AssetId,
                    OwnerUserId = userId,
                    StorageKey = asset.StorageKey,
                    ContentSha256 = asset.Sha256,
                    CreatedAt = createdAt
                });
            }
            else if (legacyMask is not null)
            {
                inputs.Add(new AiImageTaskInputEntity
                {
                    RequestTaskOrdinal = taskOrdinal,
                    Role = "mask",
                    InputOrdinal = 0,
                    InputKind = "legacy_url",
                    OwnerUserId = userId,
                    LegacyUrl = legacyMask,
                    CreatedAt = createdAt
                });
            }
        }
        return inputs;
    }

    private static VersionedRequestIntent NormalizeVersionedIntent(CreateAiImageTaskRequest request)
    {
        if (request.SizeMode is null)
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.InvalidSizeModeCombination,
                "catalogVersion cannot be submitted without sizeMode.");
        }
        var sizeMode = request.SizeMode.Trim().ToLowerInvariant();
        if (sizeMode is not (AiImageCatalogService.ExplicitSizeMode or AiImageCatalogService.AutoSizeMode))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.InvalidSizeModeCombination, "sizeMode must be explicit or auto.");
        }
        var modelCode = string.IsNullOrWhiteSpace(request.ModelCode)
            ? throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "modelCode is required")
            : AiImageModelConfigService.NormalizeModelCode(request.ModelCode);
        var qualityCode = string.IsNullOrWhiteSpace(request.QualityCode)
            ? throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "qualityCode is required")
            : request.QualityCode.Trim().ToLowerInvariant();
        var imageCount = ValidateImageCount(request.ImageCount, modelCode);
        var prompt = AiImagePromptValidator.Validate(request.Prompt);
        var negativePrompt = string.IsNullOrWhiteSpace(request.NegativePrompt) ? null : request.NegativePrompt.Trim();
        if (negativePrompt?.Length > 2000)
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, "Negative prompt is too long");
        }
        var resolutionCode = request.ResolutionCode?.Trim().ToLowerInvariant();
        var aspectRatioCode = request.AspectRatioCode?.Trim().ToLowerInvariant();
        if (sizeMode == AiImageCatalogService.AutoSizeMode
            && (request.Resolution is not null || request.ResolutionCode is not null || request.AspectRatioCode is not null))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.InvalidSizeModeCombination,
                "Auto size must omit resolution and aspect ratio fields.");
        }
        if (sizeMode == AiImageCatalogService.ExplicitSizeMode
            && (request.Resolution is not null || string.IsNullOrWhiteSpace(resolutionCode) || string.IsNullOrWhiteSpace(aspectRatioCode)))
        {
            throw new AppException(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError,
                "Explicit size requires resolutionCode and aspectRatioCode and does not accept the legacy resolution alias.");
        }
        var referenceAssetIds = NormalizeOrderedValues(request.ReferenceAssetIds);
        var referenceUrls = NormalizeOrderedValues(request.ReferenceImageUrls);
        var maskAssetId = string.IsNullOrWhiteSpace(request.MaskAssetId) ? null : request.MaskAssetId.Trim();
        var maskUrl = string.IsNullOrWhiteSpace(request.MaskImageUrl) ? null : request.MaskImageUrl.Trim();
        var canonical = new
        {
            Endpoint = "ai-images-size-mode-v1",
            request.SourcePromptId,
            Prompt = prompt,
            NegativePrompt = negativePrompt,
            ModelCode = modelCode,
            ImageCount = imageCount,
            SizeMode = sizeMode,
            QualityCode = qualityCode,
            ResolutionCode = sizeMode == AiImageCatalogService.AutoSizeMode ? null : resolutionCode,
            AspectRatioCode = sizeMode == AiImageCatalogService.AutoSizeMode ? null : aspectRatioCode,
            ReferenceAssetIds = referenceAssetIds,
            ReferenceImageUrls = referenceUrls,
            MaskAssetId = maskAssetId,
            MaskImageUrl = maskUrl
        };
        return new VersionedRequestIntent(
            prompt,
            negativePrompt,
            modelCode,
            imageCount,
            sizeMode,
            qualityCode,
            canonical);
    }

    private static IReadOnlyList<string> NormalizeOrderedValues(IReadOnlyList<string>? values) =>
        values?.Select(x => x?.Trim() ?? string.Empty).ToArray() ?? [];

    private sealed record VersionedRequestIntent(
        string Prompt,
        string? NegativePrompt,
        string ModelCode,
        int ImageCount,
        string SizeMode,
        string QualityCode,
        object CanonicalPayload);

    private static string MapTaskStatus(int status) => status switch
    {
        0 => "queued",
        3 => "processing",
        1 => "succeeded",
        2 => "failed",
        4 => "cancelled",
        _ => "failed"
    };

    private static int CalculateTaskProgress(AiImageTaskEntity entity)
    {
        if (entity.Status is 1 or 2 or 4) return 100;
        if (entity.Status == 0) return 0;
        if (entity.ImageCount <= 0) return 10;
        return Math.Clamp(10 + (int)Math.Round(80d * entity.CompletedImageCount / entity.ImageCount), 10, 90);
    }

    private static DateTime HongKongTimeToUtc(DateTime value) =>
        DateTime.SpecifyKind(value.AddHours(-8), DateTimeKind.Utc);

    private static IReadOnlyList<AiImageParameterOptionDto> MapOptions(IReadOnlyList<AiImageParameterEntity> parameters, string paramType)
    {
        return parameters
            .Where(x => x.ParamType == paramType)
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.Id)
            .Select(x => new AiImageParameterOptionDto
            {
                Code = x.ParamCode,
                Name = x.ParamName,
                ProviderValue = x.ProviderValue,
                ValueInt1 = NormalizeParameterValueInt1(paramType, x.ParamCode, x.ValueInt1),
                ValueInt2 = x.ValueInt2,
                Sort = x.Sort
            })
            .ToList();
    }

    private static int? NormalizeParameterValueInt1(string paramType, string paramCode, int? value)
    {
        if (paramType == ResolutionType && string.Equals(paramCode, "4k", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderMaxLongSide;
        }

        return value;
    }

    private static AiImageParameterEntity RequireParameter(IReadOnlyList<AiImageParameterEntity> parameters, string paramType, string paramCode)
    {
        return parameters.FirstOrDefault(x => x.ParamType == paramType && string.Equals(x.ParamCode, paramCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new AppException(ErrorCodes.BadRequest, $"Unsupported image {paramType.Replace('_', ' ')}");
    }

    private static string NormalizeCode(string? code, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(code) ? defaultValue : code.Trim().ToLowerInvariant();
    }

    private static string? ResolveCodeAlias(string? aliasCode, string? canonicalCode)
    {
        return !string.IsNullOrWhiteSpace(aliasCode) ? aliasCode : canonicalCode;
    }

    private static int ValidateImageCount(int imageCount, string modelCode)
    {
        var maxImageCount = AiImageModelConfigService.GetMaxImageCount(modelCode);
        if (imageCount < 1 || imageCount > maxImageCount)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Image count must be between 1 and {maxImageCount}");
        }

        return imageCount;
    }

    private static string ResolveRequestModelCode(string? modelCode, string? modelName, string defaultModelCode)
    {
        if (!string.IsNullOrWhiteSpace(modelCode))
        {
            return AiImageModelConfigService.NormalizeModelCode(modelCode);
        }

        return string.IsNullOrWhiteSpace(modelName)
            ? defaultModelCode
            : AiImageModelConfigService.NormalizeModelCode(modelName);
    }

    private static Uri BuildEndpoint(string baseUrl, string path)
    {
        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        normalizedPath = normalizedPath.StartsWith('/') ? normalizedPath : $"/{normalizedPath}";
        return new Uri($"{normalizedBaseUrl}{normalizedPath}", UriKind.Absolute);
    }

    private static HttpRequestMessage BuildGenerationRequest(ResolvedAiImageModelConfig config, string prompt, ResolveAiImageParametersResponse parameters)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(config.BaseUrl, config.TextToImagePath))
        {
            Content = JsonContent.Create(new
            {
                model = config.ProviderModel,
                prompt,
                size = parameters.Size,
                quality = parameters.ProviderQuality,
                n = 1
            })
        };

        return request;
    }

    private HttpRequestMessage BuildEditRequest(ResolvedAiImageModelConfig config, string prompt, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl, string imageFieldName)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(config.ProviderModel), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent(parameters.Size), "size" },
            { new StringContent(parameters.ProviderQuality), "quality" },
            { new StringContent("1"), "n" }
        };

        foreach (var referenceImageUrl in referenceImageUrls)
        {
            var file = ResolveReferenceImageFile(referenceImageUrl);
            var stream = File.OpenRead(file.Path);
            var imageContent = new StreamContent(stream);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
            content.Add(imageContent, imageFieldName, file.FileName);
        }

        if (!string.IsNullOrWhiteSpace(maskImageUrl))
        {
            var file = ResolveMaskImageFile(maskImageUrl);
            var stream = File.OpenRead(file.Path);
            var imageContent = new StreamContent(stream);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
            content.Add(imageContent, "mask", file.FileName);
        }

        return new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(config.BaseUrl, config.ImageToImagePath))
        {
            Content = content
        };
    }

    private ReferenceImageFile ResolveReferenceImageFile(string referenceImageUrl)
    {
        const string mediaPrefix = "/api/media/ai/";
        if (!referenceImageUrl.StartsWith(mediaPrefix, StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.BadRequest, "Reference image must be private media");
        }
        var relativeUrl = referenceImageUrl[mediaPrefix.Length..].Split('?', '#')[0];
        if (currentUser.UserId.HasValue && !currentUser.IsSuperAdmin && !relativeUrl.StartsWith($"{currentUser.UserId.Value}/", StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.NotFound, "Reference image does not exist");
        }
        string fullPath;
        try
        {
            fullPath = mediaPathResolver.ResolveFilePath(relativeUrl);
        }
        catch (InvalidOperationException)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid reference image URL");
        }

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new AppException(ErrorCodes.BadRequest, "Reference image does not exist");
        }

        if (fileInfo.Length is <= 0 or > MaxReferenceImageSizeBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "Reference image size must be between 1 byte and 10MB");
        }

        var mimeType = GetReferenceImageMimeType(fileInfo.Extension);
        return new ReferenceImageFile(fullPath, fileInfo.Name, mimeType);
    }

    private ReferenceImageFile ResolveMaskImageFile(string maskImageUrl)
    {
        const string mediaPrefix = "/api/media/ai/";
        if (!maskImageUrl.StartsWith(mediaPrefix, StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.BadRequest, "Mask image must be private media");
        }
        var relativeUrl = maskImageUrl[mediaPrefix.Length..].Split('?', '#')[0];
        if (currentUser.UserId.HasValue && !currentUser.IsSuperAdmin && !relativeUrl.StartsWith($"{currentUser.UserId.Value}/", StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.NotFound, "Mask image does not exist");
        }
        string fullPath;
        try
        {
            fullPath = mediaPathResolver.ResolveFilePath(relativeUrl);
        }
        catch (InvalidOperationException)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid mask image URL");
        }

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new AppException(ErrorCodes.BadRequest, "Mask image does not exist");
        }

        if (fileInfo.Length is <= 0 or > MaxMaskImageSizeBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "Mask image size must be between 1 byte and 4MB");
        }

        if (!string.Equals(fileInfo.Extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.BadRequest, "Mask image must be a PNG image");
        }

        return new ReferenceImageFile(fullPath, fileInfo.Name, "image/png");
    }

    private static string GetReferenceImageMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => throw new AppException(ErrorCodes.BadRequest, "Unsupported reference image type")
        };
    }

    private static IReadOnlyList<string> ValidateReferenceImageUrls(IReadOnlyList<string>? referenceImageUrls)
    {
        if (referenceImageUrls is null || referenceImageUrls.Count == 0)
        {
            return [];
        }

        if (referenceImageUrls.Count > MaxReferenceImageCount)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Reference images must not exceed {MaxReferenceImageCount}");
        }

        var normalized = new List<string>(referenceImageUrls.Count);
        foreach (var url in referenceImageUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new AppException(ErrorCodes.BadRequest, "Reference image URL is required");
            }

            var trimmed = url.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri)
                || trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.IndexOfAny(['?', '#']) >= 0)
            {
                throw new AppException(ErrorCodes.BadRequest, "Reference image URL must be an internal URL");
            }

            normalized.Add(uri.OriginalString);
        }

        return normalized;
    }

    private static void EnsureOwnedPrivateMediaUrls(
        long ownerUserId,
        IReadOnlyList<string> referenceImageUrls,
        string? maskImageUrl)
    {
        var ownerPrefix = $"/api/media/ai/{ownerUserId}/";
        if (referenceImageUrls.Any(url => !url.StartsWith(ownerPrefix, StringComparison.Ordinal))
            || maskImageUrl is not null && !maskImageUrl.StartsWith(ownerPrefix, StringComparison.Ordinal))
        {
            throw new AppException(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "AI image input does not exist");
        }
    }

    private static string? ValidateMaskImageUrl(string? maskImageUrl, IReadOnlyList<string> referenceImageUrls)
    {
        if (string.IsNullOrWhiteSpace(maskImageUrl))
        {
            return null;
        }

        if (referenceImageUrls.Count == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Mask image requires at least one reference image");
        }

        var trimmed = maskImageUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.IndexOfAny(['?', '#']) >= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Mask image URL must be an internal URL");
        }

        return uri.OriginalString;
    }

    private static IReadOnlyList<string> DeserializeReferenceImageUrls(string? referenceImageUrls)
    {
        if (string.IsNullOrWhiteSpace(referenceImageUrls))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(referenceImageUrls) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> DeserializeImageUrls(string? imageUrls)
    {
        if (string.IsNullOrWhiteSpace(imageUrls))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(imageUrls) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record ReferenceImageFile(string Path, string FileName, string MimeType);

    private sealed record SavedImage(string Url, string Base64, string MimeType, int Width, int Height);

    private sealed record OpenAiImageResult(string? Base64, string? Url, string? RevisedPrompt);

    private sealed record GeneratedProviderImage(OpenAiImageResult ProviderResult, SavedImage SavedImage, ResolvedAiImageModelConfig Route);

    private sealed record ImageProviderResponse(bool IsSuccess, HttpStatusCode StatusCode, string Endpoint, string Body, JsonDocument Document);

    private void LogProviderFailure(ImageProviderResponse response, string mode, ResolvedAiImageModelConfig modelConfig, string? imageFieldName)
    {
        logger.LogError(
            "AI image provider request failed. Mode={Mode}, ModelCode={ModelCode}, RouteRole={RouteRole}, RouteConfigId={RouteConfigId}, ProviderModel={ProviderModel}, Endpoint={Endpoint}, ImageFieldName={ImageFieldName}, StatusCode={StatusCode}, ProviderErrorCode={ProviderErrorCode}",
            mode,
            modelConfig.ModelCode,
            modelConfig.RouteRole,
            modelConfig.Id,
            modelConfig.ProviderModel,
            response.Endpoint,
            imageFieldName,
            response.StatusCode,
            ReadProviderErrorCode(response.Document.RootElement));
    }

    private static OpenAiImageResult ReadFirstOpenAiImage(JsonElement root)
    {
        if (TryReadOpenAiImage(root, out var image))
        {
            return image;
        }

        throw new AppException(ErrorCodes.BadRequest, "Image generation response did not contain image data");
    }

    private static bool TryReadOpenAiImage(JsonElement element, out OpenAiImageResult image)
    {
        image = default!;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadImageObject(element, out image))
                {
                    return true;
                }

                foreach (var propertyName in new[] { "data", "images", "result", "response", "output" })
                {
                    if (element.TryGetProperty(propertyName, out var child) && TryReadOpenAiImage(child, out image))
                    {
                        return true;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    if (TryReadOpenAiImage(child, out image))
                    {
                        return true;
                    }
                }

                break;
        }

        return false;
    }

    private static bool TryReadImageObject(JsonElement element, out OpenAiImageResult image)
    {
        image = default!;
        var base64 = TryGetFirstString(element, "b64_json", "base64", "imageBase64", "image_base64", "data");
        var url = TryGetFirstString(element, "url", "imageUrl", "image_url");

        if (string.IsNullOrWhiteSpace(base64) && !string.IsNullOrWhiteSpace(url) && url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            base64 = ExtractBase64FromDataUrl(url);
            url = null;
        }

        if (string.IsNullOrWhiteSpace(base64) && string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        image = new OpenAiImageResult(base64, url, TryGetFirstString(element, "revised_prompt", "revisedPrompt"));
        return true;
    }

    private static string? TryGetFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetString(element, propertyName) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ExtractBase64FromDataUrl(string value)
    {
        var markerIndex = value.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? null : value[(markerIndex + ";base64,".Length)..];
    }

    private static string ReadProviderErrorCode(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            return TryGetString(error, "code")
                ?? TryGetString(error, "type")
                ?? "unknown";
        }

        return TryGetString(root, "code")
            ?? TryGetString(root, "type")
            ?? "unknown";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
