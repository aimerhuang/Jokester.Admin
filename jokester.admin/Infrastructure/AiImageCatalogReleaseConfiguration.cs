using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Services;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Infrastructure;

public sealed record AiImageCatalogReleaseConfigurationOptions
{
    public const string SectionName = "AiImageCatalogRelease";

    public bool Approved { get; init; }

    public string ModelCode { get; init; } = "gpt-image-2";

    public string ModelName { get; init; } = "GPT Image 2";

    public string CatalogVersion { get; init; } = string.Empty;

    public string ConsentProviderCode { get; init; } = string.Empty;

    public string[] ExplicitResolutionCodes { get; init; } = ["1k", "2k", "4k"];

    public bool EnsureGptImage2TwoK { get; init; }

    public bool PublishAuto { get; init; }

    public long AutoRouteConfigId { get; init; }

    public string AutoRouteSourceResolutionCode { get; init; } = "1k";

    public bool AutoVerifiedGenerations { get; init; }

    public bool AutoVerifiedEdits { get; init; }

    public bool AutoVerifiedMaskEdits { get; init; }

    public Dictionary<string, int> AutoPoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, decimal> AutoPriceAmounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string Currency { get; init; } = "CNY";
}

public sealed record AiImageCatalogReleaseConfigurationResult(
    long ModelReleaseId,
    string ModelCode,
    string CatalogVersion,
    int ExplicitRouteCount,
    int AutoRouteCount,
    int ExplicitPriceCount,
    int AutoPriceCount,
    bool ReusedExistingRelease);

public static class AiImageCatalogReleaseConfiguration
{
    private const string ExplicitMode = AiImageCatalogService.ExplicitSizeMode;
    private const string AutoMode = AiImageCatalogService.AutoSizeMode;

    public static async Task<AiImageCatalogReleaseConfigurationResult> RunAsync(
        ISqlSugarClient db,
        AiImageCatalogReleaseConfigurationOptions options,
        DateTime? nowUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        var settings = NormalizeAndValidate(options);
        var now = EnsureUtc(nowUtc ?? DateTime.UtcNow);
        await db.Ado.BeginTranAsync();
        try
        {
            if (settings.EnsureGptImage2TwoK)
            {
                await EnsureGptImage2TwoKAsync(db, now, cancellationToken);
            }

            var plan = await BuildPlanAsync(db, settings, cancellationToken);
            var existing = await db.Queryable<AiImageModelReleaseEntity>()
                .FirstAsync(x => x.ModelCode == settings.ModelCode
                    && x.CatalogVersion == settings.CatalogVersion,
                    cancellationToken);

            AiImageModelReleaseEntity release;
            var reused = existing is not null;
            if (existing is null)
            {
                release = new AiImageModelReleaseEntity
                {
                    ModelCode = settings.ModelCode,
                    ModelName = settings.ModelName,
                    CatalogVersion = settings.CatalogVersion,
                    SizeContractVersion = AiImageCatalogService.SizeContractVersion,
                    DefaultSizeMode = ExplicitMode,
                    Status = "draft",
                    CreatedAt = now
                };
                release.Id = await db.Insertable(release).ExecuteReturnBigIdentityAsync();

                foreach (var route in plan.Routes)
                {
                    route.ModelReleaseId = release.Id;
                }
                foreach (var price in plan.Prices)
                {
                    price.ModelReleaseId = release.Id;
                }
                await db.Insertable(plan.Routes).ExecuteCommandAsync(cancellationToken);
                await db.Insertable(plan.Prices).ExecuteCommandAsync(cancellationToken);

                release.Status = "published";
                release.PublishedAt = now;
                var updated = await db.Updateable(release)
                    .Where(x => x.Id == release.Id && x.Status == "draft")
                    .ExecuteCommandAsync(cancellationToken);
                if (updated != 1)
                {
                    throw new InvalidOperationException("The AI image catalog release was not published exactly once.");
                }
            }
            else
            {
                release = existing;
                await EnsureExistingReleaseMatchesAsync(db, release, plan, cancellationToken);
            }

            var pointer = await db.Queryable<AiImageCurrentReleaseEntity>()
                .FirstAsync(x => x.ModelCode == settings.ModelCode, cancellationToken);
            if (pointer is null)
            {
                await db.Insertable(new AiImageCurrentReleaseEntity
                {
                    ModelCode = settings.ModelCode,
                    ModelReleaseId = release.Id,
                    UpdatedAt = now
                }).ExecuteCommandAsync(cancellationToken);
            }
            else if (pointer.ModelReleaseId != release.Id)
            {
                pointer.ModelReleaseId = release.Id;
                pointer.UpdatedAt = now;
                await db.Updateable(pointer).ExecuteCommandAsync(cancellationToken);
            }

            await db.Ado.CommitTranAsync();
            return CreateResult(release.Id, settings, plan, reused);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private static NormalizedSettings NormalizeAndValidate(AiImageCatalogReleaseConfigurationOptions options)
    {
        if (!options.Approved)
        {
            throw new InvalidOperationException(
                "AiImageCatalogRelease:Approved must be true only after routes, prices, and verification evidence are approved.");
        }

        var modelCode = AiImageModelConfigService.NormalizeModelCode(options.ModelCode);
        var modelName = RequireText(options.ModelName, "ModelName", 100);
        var catalogVersion = RequireAsciiToken(options.CatalogVersion, "CatalogVersion", 100);
        var consentProviderCode = LegalDocumentService.NormalizeProviderCode(options.ConsentProviderCode);
        if (consentProviderCode is not ("openai" or "google"))
        {
            throw new InvalidOperationException("AiImageCatalogRelease:ConsentProviderCode must be openai or google.");
        }
        var currency = RequireAsciiToken(options.Currency, "Currency", 3).ToUpperInvariant();
        if (currency.Length != 3)
        {
            throw new InvalidOperationException("AiImageCatalogRelease:Currency must be a three-letter ISO 4217 code.");
        }

        var explicitResolutions = options.ExplicitResolutionCodes
            .Select(NormalizeCode)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (explicitResolutions.Length == 0)
        {
            throw new InvalidOperationException("AiImageCatalogRelease:ExplicitResolutionCodes cannot be empty.");
        }
        if (options.EnsureGptImage2TwoK && modelCode != AiImageModelConfigService.DefaultGptModelCode)
        {
            throw new InvalidOperationException("EnsureGptImage2TwoK can only be used with gpt-image-2.");
        }

        var autoSourceResolution = NormalizeCode(options.AutoRouteSourceResolutionCode);
        var autoPoints = NormalizeDictionary(options.AutoPoints);
        var autoAmounts = NormalizeDictionary(options.AutoPriceAmounts);
        if (options.PublishAuto)
        {
            if (!options.AutoVerifiedGenerations || !options.AutoVerifiedEdits || !options.AutoVerifiedMaskEdits)
            {
                throw new InvalidOperationException(
                    "Publishing auto requires approved generations, edits, and mask-edits verification evidence.");
            }
            if (options.AutoRouteConfigId <= 0 && autoSourceResolution.Length == 0)
            {
                throw new InvalidOperationException(
                    "Publishing auto requires AutoRouteConfigId or AutoRouteSourceResolutionCode.");
            }
            if (autoPoints.Count == 0 || autoAmounts.Count == 0
                || !autoPoints.Keys.Order().SequenceEqual(autoAmounts.Keys.Order(), StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "AutoPoints and AutoPriceAmounts must define the same non-empty quality set.");
            }
            if (autoPoints.Values.Any(x => x <= 0) || autoAmounts.Values.Any(x => x < 0))
            {
                throw new InvalidOperationException("Auto prices must use positive points and non-negative monetary amounts.");
            }
        }

        return new NormalizedSettings(
            modelCode,
            modelName,
            catalogVersion,
            consentProviderCode,
            explicitResolutions,
            options.EnsureGptImage2TwoK,
            options.PublishAuto,
            options.AutoRouteConfigId,
            autoSourceResolution,
            options.AutoVerifiedGenerations,
            options.AutoVerifiedEdits,
            options.AutoVerifiedMaskEdits,
            autoPoints,
            autoAmounts,
            currency);
    }

    private static async Task<ReleasePlan> BuildPlanAsync(
        ISqlSugarClient db,
        NormalizedSettings settings,
        CancellationToken cancellationToken)
    {
        var configs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => x.ModelCode == settings.ModelCode && x.Status == 1 && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var explicitConfigs = configs
            .Where(x => string.IsNullOrWhiteSpace(x.ResolutionCode)
                || settings.ExplicitResolutionCodes.Contains(NormalizeCode(x.ResolutionCode), StringComparer.Ordinal))
            .Where(x => x.RouteRole is AiImageModelConfigService.PrimaryRouteRole or AiImageModelConfigService.FallbackRouteRole)
            .GroupBy(x => (Resolution: NormalizeCode(x.ResolutionCode), Role: NormalizeCode(x.RouteRole)))
            .Select(x => x.First())
            .ToArray();

        foreach (var resolution in settings.ExplicitResolutionCodes)
        {
            if (!explicitConfigs.Any(x => NormalizeCode(x.RouteRole) == AiImageModelConfigService.PrimaryRouteRole
                    && (NormalizeCode(x.ResolutionCode).Length == 0 || NormalizeCode(x.ResolutionCode) == resolution)))
            {
                throw new InvalidOperationException($"No enabled primary route is configured for {settings.ModelCode}/{resolution}.");
            }
        }

        var routes = explicitConfigs.Select(config => CreateRoute(
            config,
            ExplicitMode,
            NormalizeCode(config.ResolutionCode),
            settings.ConsentProviderCode,
            verifiedGenerations: true,
            verifiedEdits: true,
            verifiedMaskEdits: true)).ToList();

        if (settings.PublishAuto)
        {
            var candidates = configs
                .Where(x => NormalizeCode(x.RouteRole) == AiImageModelConfigService.PrimaryRouteRole)
                .Where(x => settings.AutoRouteConfigId > 0
                    ? x.Id == settings.AutoRouteConfigId
                    : NormalizeCode(x.ResolutionCode) == settings.AutoRouteSourceResolutionCode
                        && string.Equals(x.ProviderModel.Trim(), settings.ModelCode, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length != 1)
            {
                throw new InvalidOperationException(
                    "The approved auto route must resolve to exactly one enabled primary route config.");
            }
            routes.Add(CreateRoute(
                candidates[0],
                AutoMode,
                string.Empty,
                settings.ConsentProviderCode,
                settings.AutoVerifiedGenerations,
                settings.AutoVerifiedEdits,
                settings.AutoVerifiedMaskEdits));
        }

        var legacyPrices = await db.Queryable<AiImagePointPriceEntity>()
            .Where(x => x.ModelCode == settings.ModelCode && x.Status == 1 && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var prices = legacyPrices
            .Where(x => settings.ExplicitResolutionCodes.Contains(NormalizeCode(x.ResolutionCode), StringComparer.Ordinal))
            .GroupBy(x => (Resolution: NormalizeCode(x.ResolutionCode), Quality: NormalizeCode(x.QualityCode)))
            .Select(x => x.First())
            .Select(x => new AiImageModelReleasePriceEntity
            {
                ModelCode = settings.ModelCode,
                PricingMode = ExplicitMode,
                ResolutionCode = NormalizeCode(x.ResolutionCode),
                QualityCode = NormalizeCode(x.QualityCode),
                Points = x.Points,
                PriceAmount = x.PriceAmount,
                Currency = x.Currency.Trim().ToUpperInvariant(),
                Sort = x.Sort,
                Status = 1
            }).ToList();
        foreach (var resolution in settings.ExplicitResolutionCodes)
        {
            if (!prices.Any(x => x.ResolutionCode == resolution && x.Points > 0))
            {
                throw new InvalidOperationException($"No enabled explicit price is configured for {settings.ModelCode}/{resolution}.");
            }
        }

        if (settings.PublishAuto)
        {
            var sort = prices.Count == 0 ? 1 : prices.Max(x => x.Sort) + 1;
            foreach (var quality in settings.AutoPoints.Keys.Order(StringComparer.Ordinal))
            {
                prices.Add(new AiImageModelReleasePriceEntity
                {
                    ModelCode = settings.ModelCode,
                    PricingMode = AutoMode,
                    ResolutionCode = string.Empty,
                    QualityCode = quality,
                    Points = settings.AutoPoints[quality],
                    PriceAmount = settings.AutoPriceAmounts[quality],
                    Currency = settings.Currency,
                    Sort = sort++,
                    Status = 1
                });
            }
        }

        return new ReleasePlan(routes.ToArray(), prices.ToArray());
    }

    private static AiImageModelReleaseRouteEntity CreateRoute(
        AiImageModelConfigEntity config,
        string sizeMode,
        string resolutionCode,
        string consentProviderCode,
        bool verifiedGenerations,
        bool verifiedEdits,
        bool verifiedMaskEdits)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new InvalidOperationException($"Route config {config.Id} has no API key.");
        }
        if (!Uri.TryCreate(config.BaseUrl?.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException($"Route config {config.Id} must use an absolute HTTPS base URL without credentials.");
        }

        return new AiImageModelReleaseRouteEntity
        {
            RouteConfigId = config.Id,
            SizeMode = sizeMode,
            ResolutionCode = resolutionCode,
            RouteRole = NormalizeCode(config.RouteRole),
            ProviderProtocol = AiImageModelConfigService.ResolveProviderProtocol(config.Provider),
            ConsentProviderCode = consentProviderCode,
            ProviderModel = RequireText(config.ProviderModel, $"route {config.Id} ProviderModel", 100),
            BaseUrl = endpoint.AbsoluteUri.TrimEnd('/'),
            TextToImagePath = RequirePath(config.TextToImagePath, "/images/generations"),
            ImageToImagePath = RequirePath(config.ImageToImagePath, "/images/edits"),
            SecretVersionHash = Hash(config.ApiKey),
            VerifiedGenerations = verifiedGenerations,
            VerifiedEdits = verifiedEdits,
            VerifiedMaskEdits = verifiedMaskEdits,
            Sort = config.Sort
        };
    }

    private static async Task EnsureGptImage2TwoKAsync(
        ISqlSugarClient db,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var parameters = await db.Queryable<AiImageParameterEntity>()
            .Where(x => x.ParamType == "resolution")
            .ToListAsync(cancellationToken);
        foreach (var (code, name, longSide, sort) in new[]
        {
            ("1k", "1K(快速预览)", 1024, 1),
            ("2k", "2K(推荐尺寸)", 2048, 2),
            ("4k", "4K(超清画质)", 3840, 3)
        })
        {
            var parameter = parameters.FirstOrDefault(x => NormalizeCode(x.ParamCode) == code);
            if (parameter is null)
            {
                await db.Insertable(new AiImageParameterEntity
                {
                    ParamType = "resolution",
                    ParamCode = code,
                    ParamName = name,
                    ValueInt1 = longSide,
                    Sort = sort,
                    Status = 1,
                    CreatedAt = now,
                    IsDeleted = false
                }).ExecuteCommandAsync(cancellationToken);
            }
            else
            {
                parameter.ParamName = name;
                parameter.ValueInt1 = longSide;
                parameter.Sort = sort;
                parameter.Status = 1;
                parameter.UpdatedAt = now;
                parameter.IsDeleted = false;
                await db.Updateable(parameter).ExecuteCommandAsync(cancellationToken);
            }
        }

        foreach (var (quality, points, amount, sort) in new[]
        {
            ("low", 15, 0.15m, 4),
            ("med", 30, 0.30m, 5),
            ("high", 60, 0.60m, 6)
        })
        {
            var price = await db.Queryable<AiImagePointPriceEntity>()
                .FirstAsync(x => x.ModelCode == AiImageModelConfigService.DefaultGptModelCode
                    && x.ResolutionCode == "2k"
                    && x.QualityCode == quality,
                    cancellationToken);
            if (price is null)
            {
                await db.Insertable(new AiImagePointPriceEntity
                {
                    ModelCode = AiImageModelConfigService.DefaultGptModelCode,
                    ResolutionCode = "2k",
                    QualityCode = quality,
                    Points = points,
                    PriceAmount = amount,
                    Currency = "CNY",
                    Sort = sort,
                    Status = 1,
                    CreatedAt = now,
                    IsDeleted = false
                }).ExecuteCommandAsync(cancellationToken);
            }
            else
            {
                price.Points = points;
                price.PriceAmount = amount;
                price.Currency = "CNY";
                price.Sort = sort;
                price.Status = 1;
                price.UpdatedAt = now;
                price.IsDeleted = false;
                await db.Updateable(price).ExecuteCommandAsync(cancellationToken);
            }
        }

        var allGptConfigs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => x.ModelCode == AiImageModelConfigService.DefaultGptModelCode && !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var source = allGptConfigs.FirstOrDefault(x => x.Status == 1
            && NormalizeCode(x.ResolutionCode) == "1k"
            && NormalizeCode(x.RouteRole) == AiImageModelConfigService.PrimaryRouteRole
            && string.Equals(x.ProviderModel.Trim(), AiImageModelConfigService.DefaultGptModelCode, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.ApiKey)
            && !string.IsNullOrWhiteSpace(x.BaseUrl))
            ?? throw new InvalidOperationException("No verified generic GPT Image 2 primary route is available to configure 2K.");
        var target = allGptConfigs.FirstOrDefault(x => NormalizeCode(x.ResolutionCode) == "2k"
            && NormalizeCode(x.RouteRole) == AiImageModelConfigService.PrimaryRouteRole);
        if (target is null)
        {
            target = CopyAsTwoK(source, now);
            target.Id = await db.Insertable(target).ExecuteReturnBigIdentityAsync();
        }
        else
        {
            ApplyTwoKRoute(target, source, now);
            await db.Updateable(target).ExecuteCommandAsync(cancellationToken);
        }
    }

    private static AiImageModelConfigEntity CopyAsTwoK(AiImageModelConfigEntity source, DateTime now)
    {
        var target = new AiImageModelConfigEntity { CreatedAt = now };
        ApplyTwoKRoute(target, source, now);
        return target;
    }

    private static void ApplyTwoKRoute(AiImageModelConfigEntity target, AiImageModelConfigEntity source, DateTime now)
    {
        target.ModelCode = source.ModelCode;
        target.ModelName = "GPT Image 2 2K";
        target.Provider = source.Provider;
        target.ProviderModel = source.ProviderModel;
        target.ResolutionCode = "2k";
        target.RouteRole = AiImageModelConfigService.PrimaryRouteRole;
        target.BaseUrl = source.BaseUrl;
        target.ApiKey = source.ApiKey;
        target.TextToImagePath = source.TextToImagePath;
        target.ImageToImagePath = source.ImageToImagePath;
        target.Sort = 2;
        target.Status = 1;
        target.UpdatedAt = now;
        target.IsDeleted = false;
    }

    private static async Task EnsureExistingReleaseMatchesAsync(
        ISqlSugarClient db,
        AiImageModelReleaseEntity release,
        ReleasePlan plan,
        CancellationToken cancellationToken)
    {
        if (release.Status != "published" || release.RevokedAt is not null
            || release.SizeContractVersion != AiImageCatalogService.SizeContractVersion
            || release.DefaultSizeMode != ExplicitMode)
        {
            throw new InvalidOperationException(
                "The requested catalog version already exists but is not an active immutable size-mode-v1 release.");
        }

        var routes = await db.Queryable<AiImageModelReleaseRouteEntity>()
            .Where(x => x.ModelReleaseId == release.Id)
            .ToListAsync(cancellationToken);
        var prices = await db.Queryable<AiImageModelReleasePriceEntity>()
            .Where(x => x.ModelReleaseId == release.Id)
            .ToListAsync(cancellationToken);
        if (!routes.Select(RouteSignature).Order(StringComparer.Ordinal)
                .SequenceEqual(plan.Routes.Select(RouteSignature).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !prices.Select(PriceSignature).Order(StringComparer.Ordinal)
                .SequenceEqual(plan.Prices.Select(PriceSignature).Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The requested catalog version already exists with different immutable routes or prices. Use a new CatalogVersion.");
        }
    }

    private static AiImageCatalogReleaseConfigurationResult CreateResult(
        long releaseId,
        NormalizedSettings settings,
        ReleasePlan plan,
        bool reused) => new(
            releaseId,
            settings.ModelCode,
            settings.CatalogVersion,
            plan.Routes.Count(x => x.SizeMode == ExplicitMode),
            plan.Routes.Count(x => x.SizeMode == AutoMode),
            plan.Prices.Count(x => x.PricingMode == ExplicitMode),
            plan.Prices.Count(x => x.PricingMode == AutoMode),
            reused);

    private static string RouteSignature(AiImageModelReleaseRouteEntity route) => string.Join('|',
        route.RouteConfigId,
        route.SizeMode,
        route.ResolutionCode,
        route.RouteRole,
        route.ProviderProtocol,
        route.ConsentProviderCode,
        route.ProviderModel,
        route.BaseUrl,
        route.TextToImagePath,
        route.ImageToImagePath,
        route.SecretVersionHash,
        route.VerifiedGenerations,
        route.VerifiedEdits,
        route.VerifiedMaskEdits,
        route.Sort);

    private static string PriceSignature(AiImageModelReleasePriceEntity price) => string.Join('|',
        price.ModelCode,
        price.PricingMode,
        price.ResolutionCode,
        price.QualityCode,
        price.Points,
        price.PriceAmount.ToString("G29", CultureInfo.InvariantCulture),
        price.Currency,
        price.Sort,
        price.Status);

    private static string RequireText(string? value, string name, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"AiImageCatalogRelease:{name} must contain 1-{maxLength} characters.");
        }
        return normalized;
    }

    private static string RequireAsciiToken(string? value, string name, int maxLength)
    {
        var normalized = RequireText(value, name, maxLength);
        if (normalized.Any(ch => ch is < '!' or > '~'))
        {
            throw new InvalidOperationException($"AiImageCatalogRelease:{name} must be an ASCII token.");
        }
        return normalized;
    }

    private static string RequirePath(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (!normalized.StartsWith('/') || normalized.Contains("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("AI image provider paths must be root-relative paths.");
        }
        return normalized;
    }

    private static Dictionary<string, TValue> NormalizeDictionary<TValue>(IReadOnlyDictionary<string, TValue> source) =>
        source.ToDictionary(x => NormalizeCode(x.Key), x => x.Value, StringComparer.Ordinal);

    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record NormalizedSettings(
        string ModelCode,
        string ModelName,
        string CatalogVersion,
        string ConsentProviderCode,
        string[] ExplicitResolutionCodes,
        bool EnsureGptImage2TwoK,
        bool PublishAuto,
        long AutoRouteConfigId,
        string AutoRouteSourceResolutionCode,
        bool AutoVerifiedGenerations,
        bool AutoVerifiedEdits,
        bool AutoVerifiedMaskEdits,
        Dictionary<string, int> AutoPoints,
        Dictionary<string, decimal> AutoPriceAmounts,
        string Currency);

    private sealed record ReleasePlan(
        AiImageModelReleaseRouteEntity[] Routes,
        AiImageModelReleasePriceEntity[] Prices);
}
