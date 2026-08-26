using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImageCatalogService(
    ISqlSugarClient db,
    IAiImageModelConfigService legacyModelConfigService,
    IAiSizeModeRolloutPolicy rolloutPolicy,
    IOptions<AiImageSizeModeOptions> options) : IAiImageCatalogService
{
    public const string SizeContractVersion = "size-mode-v1";
    public const string ExplicitSizeMode = "explicit";
    public const string AutoSizeMode = "auto";
    public const string ExplicitPricingMode = "explicit";
    public const string AutoPricingMode = "auto";
    private const int ProviderDimensionQuantum = 16;
    private const int ProviderMaxLongSide = 3840;
    private const int ProviderMinTotalPixels = 655_360;
    private const int ProviderMaxTotalPixels = 8_294_400;

    public async Task<IReadOnlyList<AiImageModelOptionDto>> GetModelsAsync(
        AiImageClientContext client,
        CancellationToken cancellationToken)
    {
        var legacyModels = await legacyModelConfigService.GetEnabledModelsAsync(cancellationToken);
        if (!rolloutPolicy.CanUseVersionedContract(client))
        {
            return legacyModels;
        }

        var pointers = await db.Queryable<AiImageCurrentReleaseEntity>().ToListAsync(cancellationToken);
        var releaseIds = pointers.Select(x => x.ModelReleaseId).ToArray();
        List<AiImageModelReleaseEntity> releases = releaseIds.Length == 0
            ? []
            : await db.Queryable<AiImageModelReleaseEntity>()
                .Where(x => releaseIds.Contains(x.Id) && x.Status == "published" && x.RevokedAt == null)
                .ToListAsync(cancellationToken);
        List<AiImageModelReleaseRouteEntity> routes = releaseIds.Length == 0
            ? []
            : await db.Queryable<AiImageModelReleaseRouteEntity>()
                .Where(x => releaseIds.Contains(x.ModelReleaseId))
                .ToListAsync(cancellationToken);
        List<AiImageModelReleasePriceEntity> prices = releaseIds.Length == 0
            ? []
            : await db.Queryable<AiImageModelReleasePriceEntity>()
                .Where(x => releaseIds.Contains(x.ModelReleaseId) && x.Status == 1 && x.Points > 0)
                .ToListAsync(cancellationToken);

        var releaseLookup = releases.ToDictionary(x => x.Id);
        var pointerLookup = pointers.ToDictionary(
            x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode),
            x => x.ModelReleaseId,
            StringComparer.OrdinalIgnoreCase);
        return legacyModels.Select(model =>
        {
            if (!pointerLookup.TryGetValue(model.Code, out var releaseId)
                || !releaseLookup.TryGetValue(releaseId, out var release)
                || !string.Equals(release.SizeContractVersion, SizeContractVersion, StringComparison.Ordinal))
            {
                return model.WithContract(AiImageModelConfigService.ResolveProviderProtocol(model.Provider) == AiImageModelConfigService.GeminiImageProtocol
                    ? "legacy-aspect-auto"
                    : "legacy-explicit-v1");
            }

            var releaseRoutes = routes.Where(x => x.ModelReleaseId == release.Id).ToArray();
            var releasePrices = prices.Where(x => x.ModelReleaseId == release.Id).ToArray();
            var explicitResolutions = model.Resolutions
                .Where(resolution => HasRoute(releaseRoutes, ExplicitSizeMode, resolution)
                    && releasePrices.Any(price => price.PricingMode == ExplicitPricingMode
                        && string.Equals(price.ResolutionCode, resolution, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var hasAutoRoute = HasVerifiedAutoRoute(releaseRoutes);
            var hasAutoPrice = releasePrices.Any(x => x.PricingMode == AutoPricingMode);
            var supportsAuto = rolloutPolicy.CanUseAuto(client, model.Code, release.CatalogVersion)
                && hasAutoRoute
                && hasAutoPrice;
            var sizeModes = supportsAuto
                ? new[] { ExplicitSizeMode, AutoSizeMode }
                : new[] { ExplicitSizeMode };
            var qualities = model.Qualities
                .Where(quality => releasePrices.Any(price => price.QualityCode == quality
                    && (price.PricingMode == ExplicitPricingMode || supportsAuto && price.PricingMode == AutoPricingMode)))
                .ToArray();

            return new AiImageModelOptionDto
            {
                Code = model.Code,
                Name = release.ModelName,
                Provider = model.Provider,
                ProviderCode = model.ProviderCode,
                SizeContractVersion = SizeContractVersion,
                CatalogVersion = release.CatalogVersion,
                Capabilities = new AiImageModelCapabilitiesDto
                {
                    SupportsReferenceImages = model.Capabilities.SupportsReferenceImages,
                    MaxReferenceImages = model.Capabilities.MaxReferenceImages,
                    SupportsQuality = model.Capabilities.SupportsQuality,
                    SupportedImageCounts = model.Capabilities.SupportedImageCounts,
                    SizeModes = sizeModes,
                    DefaultSizeMode = sizeModes.Contains(release.DefaultSizeMode, StringComparer.Ordinal)
                        ? release.DefaultSizeMode
                        : ExplicitSizeMode,
                    SupportsAutoSize = supportsAuto
                },
                Resolutions = explicitResolutions,
                Qualities = qualities,
                AspectRatios = model.AspectRatios,
                Sort = model.Sort
            };
        }).ToArray();
    }

    public async Task<AiImageCatalogPricingResponse> GetPricingAsync(
        string? modelCode,
        string? catalogVersion,
        AiImageClientContext client,
        CancellationToken cancellationToken)
    {
        EnsureVersionedClient(client);
        var release = await RequireCurrentReleaseAsync(modelCode, catalogVersion, cancellationToken);
        var releaseRoutes = await GetReleaseRoutesAsync(release.Id, cancellationToken);
        var allowAuto = rolloutPolicy.CanUseAuto(client, release.ModelCode, release.CatalogVersion)
            && HasVerifiedAutoRoute(releaseRoutes);
        var prices = await db.Queryable<AiImageModelReleasePriceEntity>()
            .Where(x => x.ModelReleaseId == release.Id && x.Status == 1 && x.Points > 0)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var items = prices
            .Where(x => x.PricingMode == AutoPricingMode
                ? allowAuto
                : x.PricingMode == ExplicitPricingMode && HasRoute(releaseRoutes, ExplicitSizeMode, x.ResolutionCode))
            .Select(x => new AiImageCatalogPricingOptionDto
            {
                ModelCode = release.ModelCode,
                SizeMode = x.PricingMode,
                ResolutionCode = x.PricingMode == AutoPricingMode ? null : x.ResolutionCode,
                QualityCode = x.QualityCode,
                Points = x.Points,
                PriceAmount = x.PriceAmount,
                PriceMinorUnits = Money.ToMinorUnits(x.PriceAmount),
                Currency = x.Currency,
                Sort = x.Sort
            })
            .ToArray();
        return new AiImageCatalogPricingResponse
        {
            ModelCode = release.ModelCode,
            CatalogVersion = release.CatalogVersion,
            Items = items
        };
    }

    public async Task<AiImageCatalogResolution> ResolveAsync(
        ResolveAiImageParametersRequest request,
        AiImageClientContext client,
        CancellationToken cancellationToken)
    {
        EnsureVersionedClient(client);
        var modelCode = RequireCode(request.ModelCode, "modelCode is required");
        var sizeMode = NormalizeSizeMode(request.SizeMode);
        var catalogVersion = RequireCatalogVersion(request.CatalogVersion);
        var qualityCode = RequireCode(request.QualityCode, "qualityCode is required");
        var release = await RequireCurrentReleaseAsync(modelCode, catalogVersion, cancellationToken);
        if (!string.Equals(release.SizeContractVersion, SizeContractVersion, StringComparison.Ordinal))
        {
            throw InvalidCombination("The selected model does not use size-mode-v1.");
        }

        string? resolutionCode;
        string? aspectRatioCode;
        int? width;
        int? height;
        string requestedSize;
        string pricingMode;
        if (sizeMode == AutoSizeMode)
        {
            if (request.Resolution is not null || request.ResolutionCode is not null || request.AspectRatioCode is not null)
            {
                throw InvalidCombination("Auto size must omit resolution and aspect ratio fields.");
            }
            if (!rolloutPolicy.CanUseAuto(client, release.ModelCode, release.CatalogVersion))
            {
                throw AutoNotSupported();
            }
            resolutionCode = null;
            aspectRatioCode = null;
            width = null;
            height = null;
            requestedSize = AutoSizeMode;
            pricingMode = AutoPricingMode;
        }
        else
        {
            if (request.Resolution is not null)
            {
                throw InvalidCombination("size-mode-v1 does not accept the legacy resolution alias.");
            }
            resolutionCode = RequireCode(request.ResolutionCode, "resolutionCode is required for explicit size");
            aspectRatioCode = RequireCode(request.AspectRatioCode, "aspectRatioCode is required for explicit size");
            if (aspectRatioCode == AutoSizeMode)
            {
                throw InvalidCombination("Explicit size requires an explicit aspect ratio.");
            }
            (width, height) = await ResolveExplicitDimensionsAsync(resolutionCode, aspectRatioCode, cancellationToken);
            requestedSize = $"{width}x{height}";
            pricingMode = ExplicitPricingMode;
        }

        var routes = await ResolveRoutesAsync(release.Id, sizeMode, resolutionCode, cancellationToken);
        if (sizeMode == AutoSizeMode && !HasVerifiedAutoRoute(await GetReleaseRoutesAsync(release.Id, cancellationToken)))
        {
            throw AutoNotSupported();
        }
        var normalizedResolution = resolutionCode ?? string.Empty;
        var price = await db.Queryable<AiImageModelReleasePriceEntity>()
            .FirstAsync(x => x.ModelReleaseId == release.Id
                && x.ModelCode == release.ModelCode
                && x.PricingMode == pricingMode
                && x.ResolutionCode == normalizedResolution
                && x.QualityCode == qualityCode
                && x.Status == 1,
                cancellationToken);
        if (price is null || price.Points <= 0)
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                MachineErrorCodes.ImagePriceNotConfigured,
                "当前尺寸模式和画质未配置积分价格");
        }

        var quality = await db.Queryable<AiImageParameterEntity>()
            .FirstAsync(x => !x.IsDeleted && x.Status == 1 && x.ParamType == "quality" && x.ParamCode == qualityCode, cancellationToken)
            ?? throw Validation("Unsupported image quality");
        return new AiImageCatalogResolution(
            release,
            price,
            new ResolveAiImageParametersResponse
            {
                SizeContractVersion = SizeContractVersion,
                ModelCode = release.ModelCode,
                SizeMode = sizeMode,
                CatalogVersion = release.CatalogVersion,
                RequestedSize = requestedSize,
                ResolutionCode = resolutionCode,
                QualityCode = quality.ParamCode,
                AspectRatioCode = aspectRatioCode,
                Width = width,
                Height = height,
                Size = requestedSize,
                ProviderQuality = string.IsNullOrWhiteSpace(quality.ProviderValue) ? quality.ParamCode : quality.ProviderValue.Trim()
            },
            routes,
            routes.Select(x => x.ConsentProviderCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<IReadOnlyList<ResolvedAiImageModelConfig>> ResolveRoutesAsync(
        long modelReleaseId,
        string sizeMode,
        string? resolutionCode,
        CancellationToken cancellationToken)
    {
        var release = await db.Queryable<AiImageModelReleaseEntity>()
            .FirstAsync(x => x.Id == modelReleaseId && x.Status == "published" && x.RevokedAt == null, cancellationToken)
            ?? throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "AI image model release is unavailable.");
        var releaseRoutes = await GetReleaseRoutesAsync(modelReleaseId, cancellationToken);
        var normalizedResolution = resolutionCode?.Trim().ToLowerInvariant() ?? string.Empty;
        var selected = releaseRoutes
            .Where(x => x.SizeMode == sizeMode)
            .Where(x => sizeMode == AutoSizeMode
                ? x.ResolutionCode.Length == 0
                : x.ResolutionCode.Length == 0 || x.ResolutionCode == normalizedResolution)
            .GroupBy(x => x.RouteRole, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.ResolutionCode == normalizedResolution)
                .ThenBy(x => x.Sort)
                .ThenBy(x => x.Id)
                .First())
            .OrderBy(x => RouteOrder(x.RouteRole))
            .ToArray();
        if (selected.Length == 0)
        {
            throw sizeMode == AutoSizeMode
                ? AutoNotSupported()
                : new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "The catalog release has no matching explicit route.");
        }

        var configIds = selected.Select(x => x.RouteConfigId).ToArray();
        var configs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => configIds.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(cancellationToken);
        var configLookup = configs.ToDictionary(x => x.Id);
        var resolved = new List<ResolvedAiImageModelConfig>(selected.Length);
        foreach (var route in selected)
        {
            if (!configLookup.TryGetValue(route.RouteConfigId, out var config)
                || string.IsNullOrWhiteSpace(config.ApiKey)
                || !FixedEquals(route.SecretVersionHash, Hash(config.ApiKey)))
            {
                throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "The catalog route secret version is unavailable.");
            }
            ValidateRouteEndpoint(route.BaseUrl, requireAllowlist: true);
            resolved.Add(new ResolvedAiImageModelConfig
            {
                Id = config.Id,
                ModelReleaseId = release.Id,
                ReleaseRouteId = route.Id,
                CatalogVersion = release.CatalogVersion,
                SizeMode = sizeMode,
                ModelCode = release.ModelCode,
                ModelName = release.ModelName,
                Provider = route.ProviderProtocol,
                ProviderProtocol = route.ProviderProtocol,
                ConsentProviderCode = route.ConsentProviderCode,
                ProviderModel = route.ProviderModel,
                ResolutionCode = route.ResolutionCode.Length == 0 ? null : route.ResolutionCode,
                RouteRole = route.RouteRole,
                BaseUrl = route.BaseUrl,
                ApiKey = config.ApiKey,
                TextToImagePath = route.TextToImagePath,
                ImageToImagePath = route.ImageToImagePath
            });
        }
        return resolved;
    }

    private async Task<AiImageModelReleaseEntity> RequireCurrentReleaseAsync(
        string? modelCode,
        string? catalogVersion,
        CancellationToken cancellationToken)
    {
        var normalizedModel = AiImageModelConfigService.NormalizeModelCode(RequireCode(modelCode, "modelCode is required"));
        var normalizedCatalog = RequireCatalogVersion(catalogVersion);
        var pointer = await db.Queryable<AiImageCurrentReleaseEntity>()
            .FirstAsync(x => x.ModelCode == normalizedModel, cancellationToken);
        if (pointer is null)
        {
            throw CatalogChanged();
        }
        var release = await db.Queryable<AiImageModelReleaseEntity>()
            .FirstAsync(x => x.Id == pointer.ModelReleaseId
                && x.ModelCode == normalizedModel
                && x.CatalogVersion == normalizedCatalog
                && x.Status == "published"
                && x.RevokedAt == null,
                cancellationToken);
        return release ?? throw CatalogChanged();
    }

    private Task<List<AiImageModelReleaseRouteEntity>> GetReleaseRoutesAsync(long releaseId, CancellationToken cancellationToken) =>
        db.Queryable<AiImageModelReleaseRouteEntity>()
            .Where(x => x.ModelReleaseId == releaseId)
            .ToListAsync(cancellationToken);

    private async Task<(int Width, int Height)> ResolveExplicitDimensionsAsync(
        string resolutionCode,
        string aspectRatioCode,
        CancellationToken cancellationToken)
    {
        var parameters = await db.Queryable<AiImageParameterEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1
                && (x.ParamCode == resolutionCode || x.ParamCode == aspectRatioCode))
            .ToListAsync(cancellationToken);
        var resolution = parameters.FirstOrDefault(x => x.ParamType == "resolution" && x.ParamCode == resolutionCode)
            ?? throw Validation("Unsupported image resolution");
        var aspect = parameters.FirstOrDefault(x => x.ParamType == "aspect_ratio" && x.ParamCode == aspectRatioCode)
            ?? throw Validation("Unsupported image aspect ratio");
        var longSide = resolution.ValueInt1.GetValueOrDefault();
        var ratioWidth = aspect.ValueInt1.GetValueOrDefault();
        var ratioHeight = aspect.ValueInt2.GetValueOrDefault();
        if (longSide <= 0 || ratioWidth <= 0 || ratioHeight <= 0)
        {
            throw Validation("Invalid image size parameter configuration");
        }
        return CalculateProviderSize(longSide, ratioWidth, ratioHeight);
    }

    internal static (int Width, int Height) CalculateProviderSize(int requestedLongSide, int ratioWidth, int ratioHeight)
    {
        var cappedLongSide = Math.Min(requestedLongSide, ProviderMaxLongSide) / ProviderDimensionQuantum * ProviderDimensionQuantum;
        var landscape = ratioWidth >= ratioHeight;
        var longRatio = Math.Max(ratioWidth, ratioHeight);
        var shortRatio = Math.Min(ratioWidth, ratioHeight);
        if (cappedLongSide <= 0 || (long)longRatio > (long)shortRatio * 3)
        {
            throw Validation("Invalid image aspect ratio parameter");
        }
        for (var side = cappedLongSide; side >= ProviderDimensionQuantum; side -= ProviderDimensionQuantum)
        {
            var candidate = CalculateDimensions(side, shortRatio, longRatio, landscape);
            if (IsValid(candidate.Width, candidate.Height)) return candidate;
        }
        for (var side = cappedLongSide + ProviderDimensionQuantum; side <= ProviderMaxLongSide; side += ProviderDimensionQuantum)
        {
            var candidate = CalculateDimensions(side, shortRatio, longRatio, landscape);
            if (IsValid(candidate.Width, candidate.Height)) return candidate;
        }
        throw Validation("Invalid image size parameter");
    }

    private static (int Width, int Height) CalculateDimensions(int longSide, int shortRatio, int longRatio, bool landscape)
    {
        var shortSide = Math.Max(ProviderDimensionQuantum,
            (int)Math.Round((double)longSide * shortRatio / longRatio / ProviderDimensionQuantum, MidpointRounding.AwayFromZero) * ProviderDimensionQuantum);
        return landscape ? (longSide, shortSide) : (shortSide, longSide);
    }

    private static bool IsValid(int width, int height)
    {
        var longSide = Math.Max(width, height);
        var shortSide = Math.Min(width, height);
        var pixels = (long)width * height;
        return width > 0 && height > 0
            && width % ProviderDimensionQuantum == 0 && height % ProviderDimensionQuantum == 0
            && longSide <= ProviderMaxLongSide && (long)longSide <= (long)shortSide * 3
            && pixels is >= ProviderMinTotalPixels and <= ProviderMaxTotalPixels;
    }

    private void EnsureVersionedClient(AiImageClientContext client)
    {
        if (!rolloutPolicy.CanUseVersionedContract(client))
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "size-mode-v1 is not enabled for this client.");
        }
    }

    private void ValidateRouteEndpoint(string baseUrl, bool requireAllowlist)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "The catalog route endpoint is invalid.");
        }
        var allowlist = options.Value.ProviderAllowedHosts;
        if (requireAllowlist && allowlist.Length == 0
            || allowlist.Length > 0 && !allowlist.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.ServiceUnavailable, MachineErrorCodes.ServiceUnavailable, "The catalog route endpoint is not approved.");
        }
    }

    private static bool HasRoute(IEnumerable<AiImageModelReleaseRouteEntity> routes, string sizeMode, string resolution) =>
        routes.Any(x => x.SizeMode == sizeMode && (x.ResolutionCode.Length == 0 || x.ResolutionCode == resolution));

    private static bool HasVerifiedAutoRoute(IEnumerable<AiImageModelReleaseRouteEntity> routes)
    {
        var autoRoutes = routes.Where(x => x.SizeMode == AutoSizeMode && x.ResolutionCode.Length == 0).ToArray();
        return autoRoutes.Length > 0 && autoRoutes.All(x => x.VerifiedGenerations && x.VerifiedEdits && x.VerifiedMaskEdits);
    }

    private static int RouteOrder(string role) => role == AiImageModelConfigService.PrimaryRouteRole ? 0 : 1;

    private static string NormalizeSizeMode(string? value)
    {
        if (value is null)
        {
            throw Validation("sizeMode is required");
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is ExplicitSizeMode or AutoSizeMode
            ? normalized
            : throw InvalidCombination("sizeMode must be explicit or auto.");
    }

    private static string RequireCode(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Validation(message);
        return value.Trim().ToLowerInvariant();
    }

    private static string RequireCatalogVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Validation("catalogVersion is required");
        return value.Trim();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedEquals(string expected, string actual)
    {
        var left = Encoding.ASCII.GetBytes(expected);
        var right = Encoding.ASCII.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static AppException Validation(string message) =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, message);

    private static AppException InvalidCombination(string message) =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.InvalidSizeModeCombination, message);

    private static AppException AutoNotSupported() =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.AutoSizeNotSupported, "当前站点配置不支持自动尺寸");

    private static AppException CatalogChanged() =>
        new(ErrorCodes.Conflict, MachineErrorCodes.ImageCatalogChanged, "图片模型目录已更新，请刷新后重试");
}

file static class AiImageModelOptionExtensions
{
    public static AiImageModelOptionDto WithContract(this AiImageModelOptionDto model, string contract) => new()
    {
        Code = model.Code,
        Name = model.Name,
        Provider = model.Provider,
        ProviderCode = model.ProviderCode,
        SizeContractVersion = contract,
        Capabilities = model.Capabilities,
        Resolutions = model.Resolutions,
        Qualities = model.Qualities,
        AspectRatios = model.AspectRatios,
        Sort = model.Sort
    };
}
