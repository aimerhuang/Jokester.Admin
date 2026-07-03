using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SqlSugar;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace jokester.admin.Application.Services;

public sealed class AiImageService(
    HttpClient httpClient,
    IAiImageModelConfigService modelConfigService,
    IPointService pointService,
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IAiImageTaskQueue taskQueue,
    IWebHostEnvironment environment,
    ILogger<AiImageService> logger) : IAiImageService
{
    private const string MimeType = "image/png";
    private const int MaxReferenceImageCount = 6;
    private const long MaxReferenceImageSizeBytes = 10 * 1024 * 1024;
    private const long MaxMaskImageSizeBytes = 4 * 1024 * 1024;
    private const int ProviderDimensionQuantum = 16;
    private const int ProviderMaxLongSide = 3840;
    private const int ProviderMaxTotalPixels = 8_294_400;
    private static readonly TimeSpan GenerateWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GenerateWaitPollInterval = TimeSpan.FromSeconds(1);
    private const string ResolutionType = "resolution";
    private const string QualityType = "quality";
    private const string AspectRatioType = "aspect_ratio";
    private const string AiImageSiteCode = "ai_image";
    private const string EmptyResultUrlsJson = "[]";

    private static readonly Dictionary<string, string> AllowedUploadMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

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
            .Where(x => string.IsNullOrEmpty(x.ErrorMessage)
                || (!string.IsNullOrEmpty(x.ResultUrls) && x.ResultUrls != EmptyResultUrlsJson))
            .OrderByDescending(x => x.CreatedAt)
            .OrderByDescending(x => x.Id);

        var entities = await dbQuery.ToPageListAsync(query.PageIndex, query.PageSize, total);
        var favoriteLookup = await GetFavoriteUrlLookupAsync(entities.Select(x => x.Id).ToArray(), currentUserId, cancellationToken);
        var items = entities.Select(x => MapTaskDto(x, favoriteLookup.GetValueOrDefault(x.Id, []))).ToArray();

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
        var pointPrices = await db.Queryable<AiImagePointPriceEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.ModelCode)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .Select(x => new AiImagePointPriceDto
            {
                ModelCode = x.ModelCode,
                ResolutionCode = x.ResolutionCode,
                QualityCode = x.QualityCode,
                Points = x.Points,
                PriceAmount = x.PriceAmount,
                Currency = x.Currency,
                Sort = x.Sort
            })
            .ToListAsync(cancellationToken);

        return new AiImageParameterOptionsDto
        {
            Resolutions = MapOptions(parameters, ResolutionType),
            Qualities = MapOptions(parameters, QualityType),
            AspectRatios = MapOptions(parameters, AspectRatioType),
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

        // When the aspect ratio is "auto", skip resolution/size calculation and let the
        // provider decide the dimensions by passing size = "auto" straight through.
        if (string.Equals(aspectRatioCode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolveAiImageParametersResponse
            {
                ResolutionCode = resolutionCode,
                QualityCode = quality.ParamCode,
                AspectRatioCode = "auto",
                Width = 0,
                Height = 0,
                Size = "auto",
                ProviderQuality = providerQuality
            };
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

        for (var longSide = cappedLongSide; longSide >= ProviderDimensionQuantum; longSide -= ProviderDimensionQuantum)
        {
            var shortSide = RoundToNearestMultiple((double)longSide * shortRatio / longRatio, ProviderDimensionQuantum);
            if (shortSide <= 0)
            {
                continue;
            }

            var width = isLandscapeOrSquare ? longSide : shortSide;
            var height = isLandscapeOrSquare ? shortSide : longSide;
            if (width <= ProviderMaxLongSide
                && height <= ProviderMaxLongSide
                && (long)width * height <= ProviderMaxTotalPixels)
            {
                return (width, height);
            }
        }

        throw new AppException(ErrorCodes.BadRequest, "Invalid image size parameter");
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

        if (IsTaskExpired(entity, DateTime.UtcNow))
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
        return MapTaskDto(entity, favoriteLookup.GetValueOrDefault(entity.Id, []));
    }

    public async Task<GenerateAiImageResponse> GenerateAsync(GenerateAiImageRequest request, CancellationToken cancellationToken)
    {
        var taskId = await CreateAsync(new CreateAiImageTaskRequest
        {
            Prompt = request.Prompt,
            ModelCode = request.ModelCode,
            ModelName = request.ModelName,
            ImageCount = request.ImageCount,
            Resolution = request.Resolution,
            ResolutionCode = request.ResolutionCode,
            QualityCode = request.QualityCode,
            AspectRatioCode = request.AspectRatioCode,
            ReferenceImageUrls = request.ReferenceImageUrls,
            MaskImageUrl = request.MaskImageUrl
        }, cancellationToken);

        var task = await WaitForGeneratedTaskAsync(taskId, cancellationToken);
        if (task.Status == 2 && task.ResultUrls.Count == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, task.ErrorMessage ?? "AI image generation failed");
        }

        if (task.ResultUrls.Count == 0)
        {
            throw new AppException(ErrorCodes.ServerError, $"AI image generation timed out after {GenerateWaitTimeout.TotalMinutes:0} minutes. Task {taskId} is still available in history.");
        }

        return new GenerateAiImageResponse
        {
            TaskId = task.Id,
            ModelName = task.ModelName,
            ModelCode = task.ModelName,
            Prompt = task.Prompt,
            ResolutionCode = task.ResolutionCode,
            QualityCode = task.QualityCode,
            AspectRatioCode = task.AspectRatioCode,
            Width = task.Width,
            Height = task.Height,
            Size = task.Size,
            Quality = task.Quality,
            MimeType = MimeType,
            Url = task.ResultUrls[0],
            Urls = task.ResultUrls,
            MaskImageUrl = task.MaskImageUrl,
            ReferenceImageUrls = task.ReferenceImageUrls
        };
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

        var mimeType = file.ContentType.Trim().ToLowerInvariant();
        if (!AllowedUploadMimeTypes.TryGetValue(mimeType, out var ext))
        {
            throw new AppException(ErrorCodes.BadRequest, $"不支持的文件类型: {mimeType}");
        }

        var fileExt = Path.GetExtension(file.FileName);
        if (!string.IsNullOrWhiteSpace(fileExt) && AllowedUploadMimeTypes.ContainsValue(fileExt.ToLowerInvariant()))
        {
            ext = fileExt.ToLowerInvariant();
        }

        var storageKey = $"ai-images/uploads/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}{ext}";
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var savePath = Path.Combine(webRootPath, storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return new UploadAiImageResponse
        {
            Url = $"/{storageKey.Replace('\\', '/')}",
            FileName = file.FileName,
            MimeType = mimeType,
            FileSize = file.Length
        };
    }

    public async Task<GenerateAiImageResponse> GenerateFromResolvedAsync(string prompt, string? modelCode, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl, CancellationToken cancellationToken)
    {
        var normalizedPrompt = ValidatePrompt(prompt);
        var normalizedReferenceImageUrls = ValidateReferenceImageUrls(referenceImageUrls);
        var normalizedMaskImageUrl = ValidateMaskImageUrl(maskImageUrl, normalizedReferenceImageUrls);
        var requestedModelCode = AiImageModelConfigService.NormalizeModelCode(modelCode);
        var modelConfig = await modelConfigService.ResolveAsync(requestedModelCode, parameters.ResolutionCode, cancellationToken);

        if (!string.Equals(modelConfig.ModelCode, AiImageModelConfigService.DefaultGptModelCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                $"Model {requestedModelCode} is not supported by this endpoint. Use {AiImageModelConfigService.DefaultGptModelCode} on /api/ai/images/generate, or call /api/ai/images/nanoBananaImage/generate for Nano Banana.");
        }

        httpClient.DefaultRequestHeaders.Authorization = null;

        using var httpRequest = normalizedReferenceImageUrls.Count == 0
            ? BuildGenerationRequest(modelConfig, normalizedPrompt, parameters)
            : BuildEditRequest(modelConfig, normalizedPrompt, parameters, normalizedReferenceImageUrls, normalizedMaskImageUrl);
        httpRequest.Headers.Remove("Authorization");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);

        logger.LogInformation(
            "Sending AI image request. Endpoint={Endpoint}, Mode={Mode}, ModelCode={ModelCode}, ProviderModel={ProviderModel}, Size={Size}, Quality={Quality}, ReferenceImageCount={ReferenceImageCount}, ReferenceImageUrls={ReferenceImageUrls}, MaskImageUrl={MaskImageUrl}, Prompt={Prompt}",
            httpRequest.RequestUri,
            normalizedReferenceImageUrls.Count == 0 ? "generations" : "edits",
            modelConfig.ModelCode,
            modelConfig.ProviderModel,
            parameters.Size,
            parameters.ProviderQuality,
            normalizedReferenceImageUrls.Count,
            normalizedReferenceImageUrls,
            normalizedMaskImageUrl,
            normalizedPrompt);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Image generation failed: {ReadOpenAiError(document.RootElement)}");
        }

        var data = document.RootElement.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image generation returned no image data");
        }

        var firstImage = data[0];
        if (!firstImage.TryGetProperty("b64_json", out var b64Json) || string.IsNullOrWhiteSpace(b64Json.GetString()))
        {
            throw new AppException(ErrorCodes.BadRequest, "Image generation response did not contain base64 image data");
        }

        var base64 = b64Json.GetString()!;
        var url = await SaveImageAsync(base64, cancellationToken);
        return new GenerateAiImageResponse
        {
            TaskId = 0,
            ModelName = modelConfig.ModelName,
            ModelCode = modelConfig.ModelCode,
            ProviderModel = modelConfig.ProviderModel,
            Prompt = normalizedPrompt,
            ResolutionCode = parameters.ResolutionCode,
            QualityCode = parameters.QualityCode,
            AspectRatioCode = parameters.AspectRatioCode,
            Width = parameters.Width,
            Height = parameters.Height,
            Size = parameters.Size,
            Quality = parameters.ProviderQuality,
            MimeType = MimeType,
            Url = url,
            Urls = [url],
            Base64 = base64,
            DataUrl = $"data:{MimeType};base64,{base64}",
            MaskImageUrl = normalizedMaskImageUrl,
            ReferenceImageUrls = normalizedReferenceImageUrls,
            RevisedPrompt = TryGetString(firstImage, "revised_prompt")
        };
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

            if (task.Status != 0)
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

    public async Task<long> CreateAsync(CreateAiImageTaskRequest request, CancellationToken cancellationToken)
    {
        var prompt = ValidatePrompt(request.Prompt);
        var modelCode = ResolveRequestModelCode(request.ModelCode, request.ModelName, AiImageModelConfigService.DefaultGptModelCode);
        var imageCount = ValidateImageCount(request.ImageCount, modelCode);
        var parameters = await ResolveParametersAsync(new ResolveAiImageParametersRequest
        {
            Resolution = request.Resolution,
            ResolutionCode = request.ResolutionCode,
            QualityCode = request.QualityCode,
            AspectRatioCode = request.AspectRatioCode
        }, cancellationToken);
        var referenceImageUrls = ValidateReferenceImageUrls(request.ReferenceImageUrls);
        var maskImageUrl = ValidateMaskImageUrl(request.MaskImageUrl, referenceImageUrls);
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var siteId = await ResolveAiImageSiteIdAsync(request.SiteId, cancellationToken);
        var modelConfig = await modelConfigService.ResolveAsync(modelCode, parameters.ResolutionCode, cancellationToken);
        var pointCost = await pointService.GetImageGenerateCostAsync(modelConfig.ModelCode, parameters.ResolutionCode, parameters.QualityCode, imageCount, cancellationToken);
        await ExpireStaleTasksAsync(userId, cancellationToken);

        var entity = new AiImageTaskEntity
        {
            SiteId = siteId,
            UserId = userId,
            Prompt = prompt,
            NegativePrompt = string.IsNullOrWhiteSpace(request.NegativePrompt) ? null : request.NegativePrompt.Trim(),
            ModelName = modelConfig.ModelCode,
            ImageCount = imageCount,
            ResolutionCode = parameters.ResolutionCode,
            QualityCode = parameters.QualityCode,
            AspectRatioCode = parameters.AspectRatioCode,
            Width = parameters.Width,
            Height = parameters.Height,
            Size = parameters.Size,
            Quality = parameters.ProviderQuality,
            ReferenceImageUrls = referenceImageUrls.Count == 0 ? null : JsonSerializer.Serialize(referenceImageUrls),
            MaskImageUrl = maskImageUrl,
            Status = 0,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        await pointService.ConsumeForImageAsync(userId, entity.Id, modelConfig.ModelCode, parameters.ResolutionCode, parameters.QualityCode, pointCost, cancellationToken);
        if (!taskQueue.TryQueue(entity.Id))
        {
            await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = "AI image task queue is full",
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == entity.Id)
                .ExecuteCommandAsync();
            await pointService.RefundForImageAsync(userId, entity.Id, pointCost, cancellationToken);
            throw new AppException(ErrorCodes.ServerError, "AI image task queue is full");
        }

        return entity.Id;
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
        var affected = await db.Updateable<AiImageTaskEntity>()
            .SetColumns(x => new AiImageTaskEntity { IsDeleted = true, UpdatedAt = DateTime.UtcNow })
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
        var now = DateTime.UtcNow;
        var candidates = await db.Queryable<AiImageTaskEntity>()
            .Where(x => !x.IsDeleted && x.Status == 0)
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
        if (!IsTaskExpired(task, DateTime.UtcNow))
        {
            return;
        }

        var resultUrls = DeserializeImageUrls(task.ResultUrls);
        var timeout = AiImageTaskProcessor.ResolveTaskTimeout(task.ImageCount);
        var message = $"AI image generation expired after {timeout.TotalMinutes:0} minutes.";
        var affected = resultUrls.Count > 0
            ? await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = message,
                    ResultUrls = JsonSerializer.Serialize(resultUrls),
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.Status == 0)
                .ExecuteCommandAsync(cancellationToken)
            : await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = message,
                    UpdatedAt = DateTime.UtcNow
                })
                .Where(x => x.Id == task.Id && !x.IsDeleted && x.Status == 0)
                .ExecuteCommandAsync(cancellationToken);
        if (affected == 0)
        {
            return;
        }

        var failedImageCount = Math.Max(0, task.ImageCount - resultUrls.Count);
        if (failedImageCount == 0)
        {
            return;
        }

        var refundPoints = await ResolveTaskCostAsync(task, failedImageCount, cancellationToken);
        await pointService.RefundForImageAsync(task.UserId, task.Id, refundPoints, cancellationToken);
    }

    private static bool IsTaskExpired(AiImageTaskEntity task, DateTime now)
    {
        return task.Status == 0 && task.CreatedAt.Add(AiImageTaskProcessor.ResolveTaskTimeout(task.ImageCount)) <= now;
    }

    private async Task<int> ResolveTaskCostAsync(AiImageTaskEntity task, int imageCount, CancellationToken cancellationToken)
    {
        if (imageCount <= 0)
        {
            return 0;
        }

        var modelCode = AiImageModelConfigService.NormalizeModelCode(task.ModelName);
        var isNanoBananaModel = AiImageModelConfigService.IsNanoBananaModel(modelCode);
        if (isNanoBananaModel)
        {
            var candidates = string.Equals(task.Size, task.ResolutionCode, StringComparison.OrdinalIgnoreCase)
                ? [task.Size]
                : new[] { task.Size, task.ResolutionCode };

            foreach (var resolutionCode in candidates)
            {
                try
                {
                    return await pointService.GetImageGenerateCostAsync(modelCode, resolutionCode, string.Empty, imageCount, cancellationToken);
                }
                catch (AppException ex) when (ex.Code == ErrorCodes.BadRequest)
                {
                }
            }

            return 0;
        }

        return await pointService.GetImageGenerateCostAsync(modelCode, task.ResolutionCode, task.QualityCode, imageCount, cancellationToken);
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

    private async Task<string> SaveImageAsync(string base64, CancellationToken cancellationToken)
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

        var storageKey = $"ai-images/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}.png";
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var savePath = Path.Combine(webRootPath, storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);

        return $"/{storageKey.Replace('\\', '/')}";
    }

    private static AiImageTaskDto MapTaskDto(AiImageTaskEntity entity, IReadOnlyList<string>? favoriteUrls = null)
    {
        var normalizedFavoriteUrls = favoriteUrls ?? [];
        return new AiImageTaskDto
        {
            Id = entity.Id,
            SiteId = entity.SiteId,
            Prompt = entity.Prompt,
            ModelName = entity.ModelName ?? string.Empty,
            ImageCount = entity.ImageCount,
            ResolutionCode = entity.ResolutionCode,
            QualityCode = entity.QualityCode,
            AspectRatioCode = entity.AspectRatioCode,
            Width = entity.Width,
            Height = entity.Height,
            Size = entity.Size,
            Quality = entity.Quality,
            ReferenceImageUrls = DeserializeReferenceImageUrls(entity.ReferenceImageUrls),
            MaskImageUrl = entity.MaskImageUrl,
            ResultUrls = DeserializeImageUrls(entity.ResultUrls),
            FavoriteUrls = normalizedFavoriteUrls,
            IsFavorite = normalizedFavoriteUrls.Count > 0,
            ErrorMessage = entity.ErrorMessage,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Status = entity.Status
        };
    }

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

    private static string NormalizeCode(string code, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(code) ? defaultValue : code.Trim().ToLowerInvariant();
    }

    private static string ResolveCodeAlias(string aliasCode, string canonicalCode)
    {
        return !string.IsNullOrWhiteSpace(aliasCode) ? aliasCode : canonicalCode;
    }

    private static string ValidatePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new AppException(ErrorCodes.BadRequest, "Prompt is required");
        }

        var trimmed = prompt.Trim();
        if (trimmed.Length > 4000)
        {
            throw new AppException(ErrorCodes.BadRequest, "Prompt is too long");
        }

        return trimmed;
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
                n = 1,
                response_format = "b64_json"
            })
        };

        return request;
    }

    private HttpRequestMessage BuildEditRequest(ResolvedAiImageModelConfig config, string prompt, ResolveAiImageParametersResponse parameters, IReadOnlyList<string> referenceImageUrls, string? maskImageUrl)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(config.ProviderModel), "model" },
            { new StringContent(prompt), "prompt" },
            { new StringContent(parameters.Size), "size" },
            { new StringContent(parameters.ProviderQuality), "quality" },
            { new StringContent("1"), "n" },
            { new StringContent("b64_json"), "response_format" }
        };

        foreach (var referenceImageUrl in referenceImageUrls)
        {
            var file = ResolveReferenceImageFile(referenceImageUrl);
            var stream = File.OpenRead(file.Path);
            var imageContent = new StreamContent(stream);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);
            content.Add(imageContent, "image[]", file.FileName);
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
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var webRootFullPath = Path.GetFullPath(webRootPath);
        var relativeUrl = referenceImageUrl.Split('?', '#')[0].TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(webRootFullPath, relativeUrl.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(webRootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var webRootFullPath = Path.GetFullPath(webRootPath);
        var relativeUrl = maskImageUrl.Split('?', '#')[0].TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(webRootFullPath, relativeUrl.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(webRootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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
            if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                throw new AppException(ErrorCodes.BadRequest, "Reference image URL must be an internal URL");
            }

            normalized.Add(uri.OriginalString);
        }

        return normalized;
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
        if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri) || trimmed.StartsWith("//", StringComparison.Ordinal))
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

    private static string ReadOpenAiError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            if (TryGetString(error, "message") is { } message)
            {
                return message;
            }

            return error.ToString();
        }

        return root.ToString();
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
