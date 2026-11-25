using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImageModelRouteAdminService(
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    IHostAddressResolver hostAddressResolver,
    IHttpClientFactory httpClientFactory,
    IAiImageModelStatusService statusService,
    IOptions<AiImageModelAdminOptions> options,
    ILogger<AiImageModelRouteAdminService> logger) : IAiImageModelRouteAdminService
{
    public const string ProbeHttpClientName = "ai-model-route-probe";
    private const int MaxModelNameLength = 100;
    private const int MaxProviderModelLength = 100;
    private const int MaxApiKeyLength = 500;
    private const string PublishedReleaseStatus = "published";
    private const string DefaultTextToImagePath = "/images/generations";
    private const string DefaultImageToImagePath = "/images/edits";
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public async Task<IReadOnlyList<AiImageModelRouteGroupDto>> GetListAsync(
        AiImageModelRouteQuery query,
        CancellationToken cancellationToken)
    {
        EnsureSuperAdminClaim();
        var configs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var keyword = query.Keyword?.Trim();
        var groups = configs
            .GroupBy(x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => string.IsNullOrEmpty(keyword)
                || group.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || group.Any(x => x.ModelName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var visible = groups
            .SelectMany(group => group)
            .Where(x => query.Status is null || x.Status == query.Status)
            .ToArray();
        var locks = await GetReleaseLocksAsync(visible.Select(x => x.Id).ToArray(), cancellationToken);
        var userNames = await GetUserNamesAsync(visible.Select(x => x.UpdatedBy), cancellationToken);
        var states = await TryGetStatusStatesAsync(cancellationToken);

        return groups
            .Select(group =>
            {
                var routes = group
                    .Where(x => query.Status is null || x.Status == query.Status)
                    .OrderBy(x => x.ResolutionCode)
                    .ThenBy(x => RouteOrder(x.RouteRole))
                    .ThenBy(x => x.Sort)
                    .ThenBy(x => x.Id)
                    .Select(x => ToDto(x, locks, userNames))
                    .ToArray();
                var display = group.OrderBy(x => RouteOrder(x.RouteRole)).ThenBy(x => x.Sort).ThenBy(x => x.Id).First();
                var enabled = group.Where(x => x.Status == 1).ToArray();
                return new
                {
                    Sort = group.Min(x => x.Sort),
                    Dto = new AiImageModelRouteGroupDto
                    {
                        ModelCode = group.Key,
                        ModelName = display.ModelName,
                        State = states?.GetValueOrDefault(group.Key),
                        MissingPrimaryRoute = enabled.Length > 0
                            && !enabled.Any(x => NormalizeRouteRole(x.RouteRole) == AiImageModelConfigService.PrimaryRouteRole),
                        Routes = routes
                    }
                };
            })
            .Where(x => x.Dto.Routes.Count > 0)
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.Dto.ModelCode, StringComparer.Ordinal)
            .Select(x => x.Dto)
            .ToArray();
    }

    public async Task<AiImageModelRouteDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        EnsureSuperAdminClaim();
        var config = await db.Queryable<AiImageModelConfigEntity>()
            .FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken)
            ?? throw RouteNotFound();
        return await ToDtoAsync(config, cancellationToken);
    }

    public async Task<AiImageModelRouteDto> CreateAsync(
        CreateAiImageModelRouteRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = EnsureSuperAdminClaim();
        var modelCode = RequireModelCode(request.ModelCode);
        var routeRole = NormalizeRouteRole(request.RouteRole);
        if (routeRole is not (AiImageModelConfigService.PrimaryRouteRole or AiImageModelConfigService.FallbackRouteRole))
        {
            throw Validation("routeRole must be primary or fallback.");
        }
        var provider = RequireText(request.Provider, "provider", 50).ToLowerInvariant();
        var protocol = ResolveProtocolOrThrow(provider);
        var providerModel = RequireText(request.ProviderModel, "providerModel", MaxProviderModelLength);
        var gifProviderModel = NormalizeGifProviderModel(request.GifProviderModel, protocol);
        var baseUrl = await NormalizeAndCheckBaseUrlAsync(request.BaseUrl, cancellationToken);
        var apiKey = RequireApiKey(request.ApiKey);
        var textPath = AiImageRouteEndpointPolicy.NormalizePath(request.TextToImagePath, DefaultTextToImagePath);
        var imagePath = AiImageRouteEndpointPolicy.NormalizePath(request.ImageToImagePath, DefaultImageToImagePath);
        var requestFormat = NormalizeRequestFormat(request.RequestFormat);
        if (request.Status is not (0 or 1))
        {
            throw Validation("status must be 0 or 1.");
        }
        var resolutionCode = (request.ResolutionCode ?? string.Empty).Trim().ToLowerInvariant();
        if (resolutionCode.Length > 0)
        {
            var resolutionExists = await db.Queryable<AiImageParameterEntity>()
                .AnyAsync(x => !x.IsDeleted && x.Status == 1 && x.ParamType == "resolution" && x.ParamCode == resolutionCode, cancellationToken);
            if (!resolutionExists)
            {
                throw Validation("resolutionCode must be empty or an enabled resolution.");
            }
        }

        await db.Ado.BeginTranAsync();
        try
        {
            await RequireActiveSuperAdminAsync(operatorId, cancellationToken);
            var modelRoutes = (await db.Queryable<AiImageModelConfigEntity>()
                    .TranLock(DbLockType.Wait)
                    .ToListAsync(cancellationToken))
                .Where(x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode) == modelCode)
                .ToArray();
            var liveRoutes = modelRoutes.Where(x => !x.IsDeleted).ToArray();
            var isNewModel = liveRoutes.Length == 0;
            if (isNewModel)
            {
                // A new model starts with its primary route and an explicit display name; it stays invisible to users
                // until legacy prices exist for it, because the model list only offers priced resolutions.
                if (routeRole != AiImageModelConfigService.PrimaryRouteRole)
                {
                    throw Validation("The first route of a new model must be a primary route.");
                }
                if (string.IsNullOrWhiteSpace(request.ModelName))
                {
                    throw Validation("modelName is required when creating a new model.");
                }
            }
            if (liveRoutes.Any(x => TryResolveProtocol(x.Provider) is { } existing && existing != protocol))
            {
                throw Validation("provider protocol must match the existing routes of this model.");
            }
            // The unique key also covers logically deleted rows.
            if (modelRoutes.Any(x => NormalizeResolution(x.ResolutionCode) == resolutionCode
                && NormalizeRouteRole(x.RouteRole) == routeRole))
            {
                throw new AppException(ErrorCodes.Conflict, MachineErrorCodes.Conflict, "A route with this model, resolution and role already exists.");
            }

            var modelName = string.IsNullOrWhiteSpace(request.ModelName)
                ? liveRoutes.OrderBy(x => RouteOrder(x.RouteRole)).ThenBy(x => x.Sort).ThenBy(x => x.Id).First().ModelName
                : RequireText(request.ModelName, "modelName", MaxModelNameLength);
            var now = DateTime.Now;
            var entity = new AiImageModelConfigEntity
            {
                ModelCode = modelCode,
                ModelName = modelName,
                Provider = provider,
                ProviderModel = providerModel,
                GifProviderModel = gifProviderModel,
                ResolutionCode = resolutionCode,
                RouteRole = routeRole,
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                TextToImagePath = textPath,
                ImageToImagePath = imagePath,
                RequestFormat = requestFormat,
                Sort = request.Sort,
                Status = request.Status,
                Revision = 1,
                ApiKeyUpdatedAt = DateTime.UtcNow,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = operatorId,
                IsDeleted = false
            };
            entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
            await SyncModelNameAsync(modelCode, modelName, liveRoutes, entity.Id, operatorId, now, cancellationToken);
            await WriteAuditAsync(entity, "create", ["*"], null, Snapshot(entity), operatorId, cancellationToken);
            await db.Ado.CommitTranAsync();
            logger.LogInformation(
                "AI model route created. ConfigId={ConfigId}, ModelCode={ModelCode}, OperatorUserId={OperatorUserId}",
                entity.Id,
                modelCode,
                operatorId);
            return await ToDtoAsync(entity, cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<AiImageModelRouteDto> UpdateAsync(
        long id,
        UpdateAiImageModelRouteRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = EnsureSuperAdminClaim();
        var modelName = RequireText(request.ModelName, "modelName", MaxModelNameLength);
        var providerModel = RequireText(request.ProviderModel, "providerModel", MaxProviderModelLength);
        var textPath = AiImageRouteEndpointPolicy.NormalizePath(request.TextToImagePath, DefaultTextToImagePath);
        var imagePath = AiImageRouteEndpointPolicy.NormalizePath(request.ImageToImagePath, DefaultImageToImagePath);
        var requestFormat = NormalizeRequestFormat(request.RequestFormat);
        var newApiKey = string.IsNullOrEmpty(request.ApiKey) ? null : RequireApiKey(request.ApiKey);
        var requestedBaseUrl = CanonicalUrl(AiImageRouteEndpointPolicy.NormalizeBaseUrl(request.BaseUrl, options.Value));

        await db.Ado.BeginTranAsync();
        try
        {
            await RequireActiveSuperAdminAsync(operatorId, cancellationToken);
            var config = await LockRouteAsync(id, request.Revision, cancellationToken);
            var before = Snapshot(config);
            var gifProviderModel = request.GifProviderModel is null
                ? config.GifProviderModel
                : NormalizeGifProviderModel(request.GifProviderModel, TryResolveProtocol(config.Provider));
            var changed = new List<string>();
            var baseUrlChanged = !string.Equals(CanonicalStoredUrl(config.BaseUrl), requestedBaseUrl, StringComparison.Ordinal);
            if (baseUrlChanged)
            {
                // Only re-validate DNS when the endpoint actually changes, so unrelated edits keep working.
                await NormalizeAndCheckBaseUrlAsync(requestedBaseUrl, cancellationToken);
                changed.Add("baseUrl");
            }
            if (!string.Equals(config.ProviderModel, providerModel, StringComparison.Ordinal)) changed.Add("providerModel");
            if (!string.Equals(config.TextToImagePath, textPath, StringComparison.Ordinal)) changed.Add("textToImagePath");
            if (!string.Equals(config.ImageToImagePath, imagePath, StringComparison.Ordinal)) changed.Add("imageToImagePath");
            var frozenChanged = changed.ToArray();
            if (!string.Equals(config.RequestFormat, requestFormat, StringComparison.Ordinal)) changed.Add("requestFormat");
            // GIF runs only on the legacy queue, so a size-mode release never freezes this field.
            if (!string.Equals(config.GifProviderModel, gifProviderModel, StringComparison.Ordinal)) changed.Add("gifProviderModel");
            if (config.Sort != request.Sort) changed.Add("sort");
            var modelNameChanged = !string.Equals(config.ModelName, modelName, StringComparison.Ordinal);
            if (modelNameChanged) changed.Add("modelName");
            var keyChanged = newApiKey is not null && !string.Equals(config.ApiKey, newApiKey, StringComparison.Ordinal);
            if (keyChanged) changed.Add("apiKey");

            if (changed.Count == 0)
            {
                await db.Ado.CommitTranAsync();
                return await ToDtoAsync(config, cancellationToken);
            }

            var releaseIds = await GetPublishedReleaseIdsReferencingAsync(config.Id, cancellationToken);
            if (frozenChanged.Length > 0 && releaseIds.Length > 0)
            {
                throw new AppException(
                    ErrorCodes.Conflict,
                    MachineErrorCodes.ModelRouteLockedByRelease,
                    "This route is frozen by a published size-mode release. Publish a new catalog version to change its endpoint, paths or provider model.");
            }

            var now = DateTime.Now;
            var newRevision = config.Revision + 1;
            config.ModelName = modelName;
            config.ProviderModel = providerModel;
            config.GifProviderModel = gifProviderModel;
            config.BaseUrl = baseUrlChanged ? requestedBaseUrl : config.BaseUrl;
            config.TextToImagePath = textPath;
            config.ImageToImagePath = imagePath;
            config.RequestFormat = requestFormat;
            config.Sort = request.Sort;
            if (keyChanged)
            {
                config.ApiKey = newApiKey!;
                config.ApiKeyUpdatedAt = DateTime.UtcNow;
            }
            var affected = await db.Updateable<AiImageModelConfigEntity>()
                .SetColumns(x => new AiImageModelConfigEntity
                {
                    ModelName = config.ModelName,
                    ProviderModel = config.ProviderModel,
                    GifProviderModel = config.GifProviderModel,
                    BaseUrl = config.BaseUrl,
                    ApiKey = config.ApiKey,
                    ApiKeyUpdatedAt = config.ApiKeyUpdatedAt,
                    TextToImagePath = config.TextToImagePath,
                    ImageToImagePath = config.ImageToImagePath,
                    RequestFormat = config.RequestFormat,
                    Sort = config.Sort,
                    Revision = newRevision,
                    UpdatedAt = now,
                    UpdatedBy = operatorId
                })
                .Where(x => x.Id == config.Id && !x.IsDeleted && x.Revision == request.Revision)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                throw RevisionConflict();
            }
            config.Revision = newRevision;
            config.UpdatedAt = now;
            config.UpdatedBy = operatorId;

            if (keyChanged && releaseIds.Length > 0)
            {
                // Published size-mode releases bind SHA-256(api_key). A key rotation does not change the catalog's
                // capabilities, prices or endpoints, so the bound hash is re-pointed in the same transaction.
                var secretHash = HashSecret(config.ApiKey);
                await db.Updateable<AiImageModelReleaseRouteEntity>()
                    .SetColumns(x => new AiImageModelReleaseRouteEntity { SecretVersionHash = secretHash })
                    .Where(x => x.RouteConfigId == config.Id && releaseIds.Contains(x.ModelReleaseId))
                    .ExecuteCommandAsync(cancellationToken);
            }
            if (modelNameChanged)
            {
                var others = (await db.Queryable<AiImageModelConfigEntity>()
                        .Where(x => !x.IsDeleted && x.Id != config.Id)
                        .ToListAsync(cancellationToken))
                    .Where(x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode)
                        == AiImageModelConfigService.NormalizeModelCode(config.ModelCode))
                    .ToArray();
                await SyncModelNameAsync(
                    AiImageModelConfigService.NormalizeModelCode(config.ModelCode),
                    modelName,
                    others,
                    config.Id,
                    operatorId,
                    now,
                    cancellationToken);
            }

            var action = changed is ["apiKey"] ? "rotate_key" : "update";
            await WriteAuditAsync(config, action, changed, before, Snapshot(config), operatorId, cancellationToken);
            await db.Ado.CommitTranAsync();
            logger.LogInformation(
                "AI model route updated. ConfigId={ConfigId}, ChangedFields={ChangedFields}, OperatorUserId={OperatorUserId}",
                config.Id,
                string.Join(',', changed),
                operatorId);
            return await ToDtoAsync(config, cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<AiImageModelRouteDto> UpdateStatusAsync(
        long id,
        UpdateAiImageModelRouteStatusRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = EnsureSuperAdminClaim();
        if (request.Status is not (0 or 1))
        {
            throw Validation("status must be 0 or 1.");
        }

        await db.Ado.BeginTranAsync();
        try
        {
            await RequireActiveSuperAdminAsync(operatorId, cancellationToken);
            var config = await LockRouteAsync(id, request.Revision, cancellationToken);
            if (config.Status == request.Status)
            {
                await db.Ado.CommitTranAsync();
                return await ToDtoAsync(config, cancellationToken);
            }

            if (request.Status == 1)
            {
                if (string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    throw Validation("Configure an API key before enabling this route.");
                }
                ResolveProtocolOrThrow(config.Provider);
                await NormalizeAndCheckBaseUrlAsync(config.BaseUrl, cancellationToken);
            }
            else
            {
                var releaseIds = await GetPublishedReleaseIdsReferencingAsync(config.Id, cancellationToken);
                if (releaseIds.Length > 0)
                {
                    throw new AppException(
                        ErrorCodes.Conflict,
                        MachineErrorCodes.ModelRouteLockedByRelease,
                        "This route is used by a published size-mode release. Publish a catalog version without it before disabling.");
                }
                if (!request.ConfirmOffline)
                {
                    var uncovered = await FindResolutionsLosingCoverageAsync(config, cancellationToken);
                    if (uncovered.Count > 0)
                    {
                        throw new AppException(
                            ErrorCodes.Conflict,
                            MachineErrorCodes.ModelRouteOfflineConfirmRequired,
                            "Disabling this route leaves priced resolutions without any enabled route. Retry with confirmOffline=true to take them offline.",
                            new { resolutionCodes = uncovered });
                    }
                }
            }

            var before = Snapshot(config);
            var now = DateTime.Now;
            var newRevision = config.Revision + 1;
            var affected = await db.Updateable<AiImageModelConfigEntity>()
                .SetColumns(x => new AiImageModelConfigEntity
                {
                    Status = request.Status,
                    Revision = newRevision,
                    UpdatedAt = now,
                    UpdatedBy = operatorId
                })
                .Where(x => x.Id == config.Id && !x.IsDeleted && x.Revision == request.Revision)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
            {
                throw RevisionConflict();
            }
            config.Status = request.Status;
            config.Revision = newRevision;
            config.UpdatedAt = now;
            config.UpdatedBy = operatorId;
            await WriteAuditAsync(
                config,
                request.Status == 1 ? "enable" : "disable",
                ["status"],
                before,
                Snapshot(config),
                operatorId,
                cancellationToken);
            await db.Ado.CommitTranAsync();
            return await ToDtoAsync(config, cancellationToken);
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    public async Task<AiImageModelRouteProbeResult> ProbeAsync(long id, CancellationToken cancellationToken)
    {
        EnsureSuperAdminClaim();
        var config = await db.Queryable<AiImageModelConfigEntity>()
            .FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken)
            ?? throw RouteNotFound();
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw Validation("Configure an API key before testing this route.");
        }
        var baseUrl = await NormalizeAndCheckBaseUrlAsync(config.BaseUrl, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ProbeTimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{baseUrl}/models", UriKind.Absolute));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            var client = httpClientFactory.CreateClient(ProbeHttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            return new AiImageModelRouteProbeResult
            {
                Result = response.IsSuccessStatusCode
                    ? AiImageModelRouteProbeResults.Passed
                    : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? AiImageModelRouteProbeResults.AuthFailed
                        : AiImageModelRouteProbeResults.Unreachable,
                HttpStatus = status,
                ElapsedMilliseconds = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "AI model route probe failed. ConfigId={ConfigId}, FailureType={FailureType}",
                config.Id,
                ex.GetType().Name);
            return new AiImageModelRouteProbeResult
            {
                Result = AiImageModelRouteProbeResults.Unreachable,
                HttpStatus = null,
                ElapsedMilliseconds = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    public async Task<PagedResult<AiImageModelRouteAuditDto>> GetAuditsAsync(
        AiImageModelRouteAuditQuery query,
        CancellationToken cancellationToken)
    {
        EnsureSuperAdminClaim();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        RefAsync<int> total = 0;
        var rows = await db.Queryable<AiImageModelConfigAuditEntity>()
            .WhereIF(query.ConfigId.HasValue, x => x.ConfigId == query.ConfigId!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToPageListAsync(page, pageSize, total);
        var userNames = await GetUserNamesAsync(rows.Select(x => (long?)x.OperatorUserId), cancellationToken);
        return new PagedResult<AiImageModelRouteAuditDto>
        {
            Total = total.Value,
            PageIndex = page,
            PageSize = pageSize,
            Items = rows.Select(x => new AiImageModelRouteAuditDto
            {
                Id = x.Id,
                ConfigId = x.ConfigId,
                ModelCode = x.ModelCode,
                ResolutionCode = x.ResolutionCode,
                RouteRole = x.RouteRole,
                Action = x.Action,
                ChangedFields = x.ChangedFields.Split(',', StringSplitOptions.RemoveEmptyEntries),
                BeforeJson = x.BeforeJson,
                AfterJson = x.AfterJson,
                OperatorUserId = x.OperatorUserId,
                OperatorUserName = userNames.GetValueOrDefault(x.OperatorUserId),
                OperatorIp = x.OperatorIp,
                CreatedAt = ApiDateTime.FromUtcStorage(x.CreatedAt)
            }).ToArray()
        };
    }

    public static string MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return string.Empty;
        }
        return apiKey.Length >= 8 ? "••••" + apiKey[^4..] : "••••";
    }

    private long EnsureSuperAdminClaim()
    {
        if (!currentUser.UserId.HasValue)
        {
            throw new AppException(ErrorCodes.Unauthorized, MachineErrorCodes.Unauthorized, "Authentication is required.");
        }
        if (!currentUser.IsSuperAdmin)
        {
            throw new AppException(ErrorCodes.Forbidden, MachineErrorCodes.ResourceForbidden, "Only super administrators can manage model routes.");
        }
        return currentUser.UserId.Value;
    }

    private async Task RequireActiveSuperAdminAsync(long operatorId, CancellationToken cancellationToken)
    {
        // Re-check inside the transaction: an access token stays valid for up to 15 minutes after a downgrade.
        var user = await db.Queryable<SysUserEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.Id == operatorId, cancellationToken);
        if (user is null || user.IsDeleted || user.Status != 1 || !user.IsSuperAdmin)
        {
            throw new AppException(ErrorCodes.Forbidden, MachineErrorCodes.ResourceForbidden, "Only active super administrators can manage model routes.");
        }
    }

    private async Task<AiImageModelConfigEntity> LockRouteAsync(long id, int revision, CancellationToken cancellationToken)
    {
        var config = await db.Queryable<AiImageModelConfigEntity>()
            .TranLock(DbLockType.Wait)
            .FirstAsync(x => x.Id == id && !x.IsDeleted, cancellationToken)
            ?? throw RouteNotFound();
        if (config.Revision != revision)
        {
            throw RevisionConflict();
        }
        return config;
    }

    private async Task<string> NormalizeAndCheckBaseUrlAsync(string? rawBaseUrl, CancellationToken cancellationToken)
    {
        var uri = AiImageRouteEndpointPolicy.NormalizeBaseUrl(rawBaseUrl, options.Value);
        await AiImageRouteEndpointPolicy.EnsureResolvesToPublicAddressesAsync(uri, options.Value, hostAddressResolver, cancellationToken);
        return CanonicalUrl(uri);
    }

    private static string CanonicalUrl(Uri uri) => uri.AbsoluteUri.TrimEnd('/');

    private static string CanonicalStoredUrl(string? stored)
    {
        var value = stored?.Trim() ?? string.Empty;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? CanonicalUrl(uri) : value;
    }

    private async Task<long[]> GetPublishedReleaseIdsReferencingAsync(long configId, CancellationToken cancellationToken)
    {
        var releaseIds = await db.Queryable<AiImageModelReleaseRouteEntity>()
            .Where(x => x.RouteConfigId == configId)
            .Select(x => x.ModelReleaseId)
            .ToListAsync(cancellationToken);
        if (releaseIds.Count == 0)
        {
            return [];
        }
        var distinct = releaseIds.Distinct().ToArray();
        return (await db.Queryable<AiImageModelReleaseEntity>()
                .Where(x => distinct.Contains(x.Id) && x.Status == PublishedReleaseStatus && x.RevokedAt == null)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken))
            .ToArray();
    }

    private async Task<Dictionary<long, string[]>> GetReleaseLocksAsync(long[] configIds, CancellationToken cancellationToken)
    {
        if (configIds.Length == 0)
        {
            return [];
        }
        var routes = await db.Queryable<AiImageModelReleaseRouteEntity>()
            .Where(x => configIds.Contains(x.RouteConfigId))
            .Select(x => new { x.RouteConfigId, x.ModelReleaseId })
            .ToListAsync(cancellationToken);
        if (routes.Count == 0)
        {
            return [];
        }
        var releaseIds = routes.Select(x => x.ModelReleaseId).Distinct().ToArray();
        var releases = (await db.Queryable<AiImageModelReleaseEntity>()
                .Where(x => releaseIds.Contains(x.Id) && x.Status == PublishedReleaseStatus && x.RevokedAt == null)
                .Select(x => new { x.Id, x.CatalogVersion })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.CatalogVersion);
        return routes
            .Where(x => releases.ContainsKey(x.ModelReleaseId))
            .GroupBy(x => x.RouteConfigId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(r => releases[r.ModelReleaseId]).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Priced resolutions of the route's model that are covered by an enabled route now but would not be after
    /// disabling it. Runtime resolution falls back to the fallback route, so any enabled role counts as coverage.
    /// </summary>
    private async Task<IReadOnlyList<string>> FindResolutionsLosingCoverageAsync(
        AiImageModelConfigEntity target,
        CancellationToken cancellationToken)
    {
        var modelCode = AiImageModelConfigService.NormalizeModelCode(target.ModelCode);
        var pricedResolutions = (await db.Queryable<AiImagePointPriceEntity>()
                .Where(x => !x.IsDeleted && x.Status == 1)
                .ToListAsync(cancellationToken))
            .Where(x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode) == modelCode)
            .Select(x => NormalizeResolution(x.ResolutionCode))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (pricedResolutions.Length == 0)
        {
            return [];
        }
        var enabledRoutes = (await db.Queryable<AiImageModelConfigEntity>()
                .Where(x => !x.IsDeleted && x.Status == 1)
                .ToListAsync(cancellationToken))
            .Where(x => AiImageModelConfigService.NormalizeModelCode(x.ModelCode) == modelCode)
            .ToArray();
        static bool Covers(AiImageModelConfigEntity route, string resolution)
        {
            var routeResolution = NormalizeResolution(route.ResolutionCode);
            return routeResolution.Length == 0 || routeResolution == resolution;
        }

        return pricedResolutions
            .Where(resolution => enabledRoutes.Any(x => Covers(x, resolution))
                && !enabledRoutes.Any(x => x.Id != target.Id && Covers(x, resolution)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task SyncModelNameAsync(
        string modelCode,
        string modelName,
        IReadOnlyList<AiImageModelConfigEntity> routes,
        long excludeId,
        long operatorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var ids = routes
            .Where(x => x.Id != excludeId
                && !x.IsDeleted
                && AiImageModelConfigService.NormalizeModelCode(x.ModelCode) == modelCode
                && !string.Equals(x.ModelName, modelName, StringComparison.Ordinal))
            .Select(x => x.Id)
            .ToArray();
        if (ids.Length == 0)
        {
            return;
        }
        await db.Updateable<AiImageModelConfigEntity>()
            .SetColumns(x => new AiImageModelConfigEntity { ModelName = modelName, UpdatedAt = now, UpdatedBy = operatorId })
            .Where(x => ids.Contains(x.Id))
            .ExecuteCommandAsync(cancellationToken);
    }

    private async Task WriteAuditAsync(
        AiImageModelConfigEntity config,
        string action,
        IReadOnlyCollection<string> changedFields,
        object? before,
        object after,
        long operatorId,
        CancellationToken cancellationToken)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        await db.Insertable(new AiImageModelConfigAuditEntity
        {
            ConfigId = config.Id,
            ModelCode = AiImageModelConfigService.NormalizeModelCode(config.ModelCode),
            ResolutionCode = NormalizeResolution(config.ResolutionCode),
            RouteRole = NormalizeRouteRole(config.RouteRole),
            Action = action,
            ChangedFields = string.Join(',', changedFields),
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, AuditJsonOptions),
            AfterJson = JsonSerializer.Serialize(after, AuditJsonOptions),
            OperatorUserId = operatorId,
            OperatorIp = ip is { Length: > 50 } ? ip[..50] : ip,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>Non-secret audit snapshot. The key is represented only by its mask.</summary>
    private static object Snapshot(AiImageModelConfigEntity config) => new
    {
        config.ModelName,
        config.Provider,
        config.ProviderModel,
        config.GifProviderModel,
        ResolutionCode = NormalizeResolution(config.ResolutionCode),
        RouteRole = NormalizeRouteRole(config.RouteRole),
        config.BaseUrl,
        ApiKeyMask = MaskApiKey(config.ApiKey),
        config.TextToImagePath,
        config.ImageToImagePath,
        config.RequestFormat,
        config.Sort,
        config.Status
    };

    private async Task<AiImageModelRouteDto> ToDtoAsync(AiImageModelConfigEntity config, CancellationToken cancellationToken)
    {
        var locks = await GetReleaseLocksAsync([config.Id], cancellationToken);
        var userNames = await GetUserNamesAsync([config.UpdatedBy], cancellationToken);
        return ToDto(config, locks, userNames);
    }

    private static AiImageModelRouteDto ToDto(
        AiImageModelConfigEntity config,
        IReadOnlyDictionary<long, string[]> locks,
        IReadOnlyDictionary<long, string> userNames) => new()
    {
        Id = config.Id,
        Revision = config.Revision,
        ModelCode = AiImageModelConfigService.NormalizeModelCode(config.ModelCode),
        ModelName = config.ModelName,
        ResolutionCode = NormalizeResolution(config.ResolutionCode),
        RouteRole = NormalizeRouteRole(config.RouteRole),
        Provider = config.Provider,
        ProviderProtocol = TryResolveProtocol(config.Provider),
        ProviderModel = config.ProviderModel,
        GifProviderModel = string.IsNullOrWhiteSpace(config.GifProviderModel) ? null : config.GifProviderModel,
        BaseUrl = config.BaseUrl,
        ApiKeySet = !string.IsNullOrWhiteSpace(config.ApiKey),
        ApiKeyMask = string.IsNullOrWhiteSpace(config.ApiKey) ? null : MaskApiKey(config.ApiKey),
        ApiKeyUpdatedAt = config.ApiKeyUpdatedAt.HasValue ? ApiDateTime.FromUtcStorage(config.ApiKeyUpdatedAt.Value) : null,
        TextToImagePath = config.TextToImagePath,
        ImageToImagePath = config.ImageToImagePath,
        RequestFormat = string.IsNullOrWhiteSpace(config.RequestFormat)
            ? AiImageModelConfigService.OpenAiSizeRequestFormat
            : config.RequestFormat,
        Sort = config.Sort,
        Status = config.Status,
        PublishedCatalogVersions = locks.GetValueOrDefault(config.Id) ?? [],
        UpdatedAt = config.UpdatedAt.HasValue ? ApiDateTime.FromLocalStorage(config.UpdatedAt.Value) : null,
        UpdatedBy = config.UpdatedBy.HasValue ? userNames.GetValueOrDefault(config.UpdatedBy.Value) : null
    };

    private async Task<Dictionary<long, string>> GetUserNamesAsync(IEnumerable<long?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return [];
        }
        return (await db.Queryable<SysUserEntity>()
                .Where(x => distinct.Contains(x.Id))
                .Select(x => new { x.Id, x.UserName })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id, x => x.UserName);
    }

    private async Task<Dictionary<string, string>?> TryGetStatusStatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await statusService.GetAsync(cancellationToken);
            return status.Items.ToDictionary(x => x.ModelCode, x => x.State, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("AI model status unavailable for the route list. FailureType={FailureType}", ex.GetType().Name);
            return null;
        }
    }

    private static string RequireModelCode(string? value)
    {
        var text = RequireText(value, "modelCode", 100);
        var code = AiImageModelConfigService.NormalizeModelCode(text);
        // Model codes appear in URLs (/models/{modelCode}/...), Redis keys and audit filters.
        if (!ModelCodePattern.IsMatch(code))
        {
            throw Validation("modelCode must be 2-100 lowercase letters, digits, '.', '_' or '-', starting with a letter or digit.");
        }
        return code;
    }

    private static readonly System.Text.RegularExpressions.Regex ModelCodePattern =
        new("^[a-z0-9][a-z0-9._-]{1,99}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string RequireText(string? value, string field, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > maxLength || text.Any(char.IsControl))
        {
            throw Validation($"{field} is required and must be at most {maxLength} characters.");
        }
        return text;
    }

    private static string RequireApiKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length == 0 || key.Length > MaxApiKeyLength || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            // Never echo the submitted key.
            throw Validation("apiKey is required, must be at most 500 characters and must not contain whitespace.");
        }
        return key;
    }

    private static string? NormalizeGifProviderModel(string? value, string? protocol)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }
        if (protocol != AiImageModelConfigService.OpenAiImageProtocol)
        {
            throw Validation("gifProviderModel is only supported on OpenAI Images routes.");
        }
        return RequireText(normalized, "gifProviderModel", MaxProviderModelLength);
    }

    private static string NormalizeRequestFormat(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? AiImageModelConfigService.OpenAiSizeRequestFormat
            : value.Trim().ToLowerInvariant();
        return normalized is AiImageModelConfigService.OpenAiSizeRequestFormat or AiImageModelConfigService.PromptAspectRatioRequestFormat
            ? normalized
            : throw Validation("requestFormat must be openai-size or prompt-aspect-ratio.");
    }

    private static string ResolveProtocolOrThrow(string provider) =>
        TryResolveProtocol(provider) ?? throw Validation("provider must map to openai-image or gemini-image.");

    private static string? TryResolveProtocol(string? provider)
    {
        try
        {
            return AiImageModelConfigService.ResolveProviderProtocol(provider);
        }
        catch (AppException)
        {
            return null;
        }
    }

    private static string NormalizeRouteRole(string? value) =>
        string.IsNullOrWhiteSpace(value) ? AiImageModelConfigService.PrimaryRouteRole : value.Trim().ToLowerInvariant();

    private static string NormalizeResolution(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static int RouteOrder(string? role) =>
        NormalizeRouteRole(role) == AiImageModelConfigService.PrimaryRouteRole ? 0 : 1;

    private static string HashSecret(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static AppException Validation(string message) =>
        new(ErrorCodes.BadRequest, MachineErrorCodes.ValidationError, message);

    private static AppException RouteNotFound() =>
        new(ErrorCodes.NotFound, MachineErrorCodes.ResourceNotFound, "Model route does not exist.");

    private static AppException RevisionConflict() =>
        new(ErrorCodes.Conflict, MachineErrorCodes.ModelRouteRevisionConflict, "The route was changed by someone else. Reload and retry.");
}
