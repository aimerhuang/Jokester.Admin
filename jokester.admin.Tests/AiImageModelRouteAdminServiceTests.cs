using System.Net;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiImageModelRouteAdminServiceTests
{
    private const long AdminId = 1;
    private const string OriginalKey = "sk-original-secret-1234";
    private const string RotatedKey = "sk-rotated-secret-9876";

    [Fact]
    public async Task NonSuperAdmin_IsForbidden()
    {
        using var context = new TestContext(isSuperAdminClaim: false);

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.GetListAsync(new AiImageModelRouteQuery(), default));

        Assert.Equal(MachineErrorCodes.ResourceForbidden, ex.MachineCode);
    }

    [Fact]
    public async Task DowngradedSuperAdminWithStillValidToken_CannotWrite()
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");
        context.Db.Ado.ExecuteCommand("UPDATE sys_user SET is_super_admin = 0 WHERE id = @id", new { id = AdminId });

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateAsync(route.Id, UpdateFrom(route), default));

        Assert.Equal(MachineErrorCodes.ResourceForbidden, ex.MachineCode);
    }

    [Fact]
    public async Task Responses_NeverExposeTheApiKey()
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var list = await context.Service.GetListAsync(new AiImageModelRouteQuery(), default);
        var detail = await context.Service.GetAsync(route.Id, default);
        var rotated = await context.Service.UpdateAsync(route.Id, UpdateFrom(route, apiKey: RotatedKey), default);
        var audits = await context.Service.GetAuditsAsync(new AiImageModelRouteAuditQuery(), default);

        var serialized = JsonSerializer.Serialize(new object[] { list, detail, rotated, audits });
        Assert.DoesNotContain(OriginalKey, serialized);
        Assert.DoesNotContain(RotatedKey, serialized);
        Assert.True(detail.ApiKeySet);
        Assert.Equal("••••1234", detail.ApiKeyMask);
        Assert.Equal("••••9876", rotated.ApiKeyMask);
    }

    [Fact]
    public async Task Update_EmptyKeyKeepsCurrentKey_NewKeyRotatesAndAudits()
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var renamed = await context.Service.UpdateAsync(route.Id, UpdateFrom(route, providerModel: "gpt-image-2-new"), default);
        Assert.Equal(OriginalKey, context.GetRoute(route.Id).ApiKey);
        Assert.Equal(2, renamed.Revision);

        var rotated = await context.Service.UpdateAsync(
            route.Id,
            UpdateFrom(context.GetRoute(route.Id), apiKey: RotatedKey),
            default);

        var stored = context.GetRoute(route.Id);
        Assert.Equal(RotatedKey, stored.ApiKey);
        Assert.NotNull(stored.ApiKeyUpdatedAt);
        Assert.Equal(3, rotated.Revision);
        Assert.Equal(AdminId, stored.UpdatedBy);
        var audits = context.Db.Queryable<AiImageModelConfigAuditEntity>().OrderBy(x => x.Id).ToList();
        Assert.Equal(["update", "rotate_key"], audits.Select(x => x.Action));
        Assert.Equal("providerModel", audits[0].ChangedFields);
        Assert.Contains("••••9876", audits[1].AfterJson);
    }

    [Fact]
    public async Task Update_WithStaleRevision_Conflicts()
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");
        await context.Service.UpdateAsync(route.Id, UpdateFrom(route, sort: 5), default);

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateAsync(route.Id, UpdateFrom(route, sort: 6), default));

        Assert.Equal(MachineErrorCodes.ModelRouteRevisionConflict, ex.MachineCode);
        Assert.Equal(5, context.GetRoute(route.Id).Sort);
    }

    [Fact]
    public async Task PublishedReleaseRoute_KeyRotationResyncsBoundHash_ButEndpointAndDisableAreLocked()
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");
        var published = context.SeedRelease(route.Id, "published", revoked: false);
        var revoked = context.SeedRelease(route.Id, "published", revoked: true);

        var detail = await context.Service.GetAsync(route.Id, default);
        Assert.Equal(["v-published"], detail.PublishedCatalogVersions);

        var endpointChange = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateAsync(
            route.Id,
            UpdateFrom(route, baseUrl: "https://other-relay.example.com/v1"),
            default));
        Assert.Equal(MachineErrorCodes.ModelRouteLockedByRelease, endpointChange.MachineCode);

        var disable = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateStatusAsync(
            route.Id,
            new UpdateAiImageModelRouteStatusRequest { Revision = route.Revision, Status = 0, ConfirmOffline = true },
            default));
        Assert.Equal(MachineErrorCodes.ModelRouteLockedByRelease, disable.MachineCode);

        // Key rotation and non-frozen fields are allowed; the published snapshot hash follows the new key.
        await context.Service.UpdateAsync(route.Id, UpdateFrom(route, apiKey: RotatedKey, requestFormat: "prompt-aspect-ratio"), default);

        Assert.Equal(Sha256(RotatedKey), context.GetReleaseRouteHash(published));
        Assert.Equal(Sha256(OriginalKey), context.GetReleaseRouteHash(revoked));
    }

    [Theory]
    [InlineData("http://relay.example.com/v1")]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://user:pass@relay.example.com/v1")]
    [InlineData("https://relay.example.com/v1?x=1")]
    [InlineData("ftp://relay.example.com/v1")]
    public async Task Update_RejectsUnsafeBaseUrls(string baseUrl)
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateAsync(route.Id, UpdateFrom(route, baseUrl: baseUrl), default));

        Assert.Equal(MachineErrorCodes.ValidationError, ex.MachineCode);
        Assert.Equal("https://relay.example.com/v1", context.GetRoute(route.Id).BaseUrl);
    }

    [Fact]
    public async Task Update_RejectsHostThatResolvesToPrivateAddress()
    {
        using var context = new TestContext(resolve: host => host == "internal.example.com"
            ? [IPAddress.Parse("10.0.0.5")]
            : [IPAddress.Parse("93.184.216.34")]);
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateAsync(
            route.Id,
            UpdateFrom(route, baseUrl: "https://internal.example.com/v1"),
            default));

        Assert.Equal(MachineErrorCodes.ValidationError, ex.MachineCode);
    }

    [Fact]
    public async Task Update_AllowsExplicitlyAllowedPrivateNetworkHost()
    {
        using var context = new TestContext(
            resolve: _ => [IPAddress.Parse("172.18.0.4")],
            configure: options => options.AllowPrivateNetworkHosts = ["relay"]);
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var updated = await context.Service.UpdateAsync(route.Id, UpdateFrom(route, baseUrl: "http://relay:8080/v1"), default);

        Assert.Equal("http://relay:8080/v1", updated.BaseUrl);
    }

    [Fact]
    public async Task Disable_LastRouteForPricedResolution_RequiresOfflineConfirmation()
    {
        using var context = new TestContext();
        var primary = context.SeedRoute("gpt-image-2", "1k", "primary");
        context.SeedPrice("gpt-image-2", "1k");

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.UpdateStatusAsync(
            primary.Id,
            new UpdateAiImageModelRouteStatusRequest { Revision = primary.Revision, Status = 0 },
            default));
        Assert.Equal(MachineErrorCodes.ModelRouteOfflineConfirmRequired, ex.MachineCode);
        Assert.Equal(1, context.GetRoute(primary.Id).Status);

        var disabled = await context.Service.UpdateStatusAsync(
            primary.Id,
            new UpdateAiImageModelRouteStatusRequest { Revision = primary.Revision, Status = 0, ConfirmOffline = true },
            default);

        Assert.Equal(0, disabled.Status);
        Assert.Equal("disable", context.Db.Queryable<AiImageModelConfigAuditEntity>().Single().Action);
    }

    [Fact]
    public async Task Disable_PrimaryWithGenericFallback_DoesNotNeedConfirmation_AndListFlagsMissingPrimary()
    {
        using var context = new TestContext();
        var primary = context.SeedRoute("gpt-image-2", "1k", "primary");
        context.SeedRoute("gpt-image-2", "", "fallback");
        context.SeedPrice("gpt-image-2", "1k");

        await context.Service.UpdateStatusAsync(
            primary.Id,
            new UpdateAiImageModelRouteStatusRequest { Revision = primary.Revision, Status = 0 },
            default);

        var group = Assert.Single(await context.Service.GetListAsync(new AiImageModelRouteQuery(), default));
        Assert.True(group.MissingPrimaryRoute);
        Assert.Equal(2, group.Routes.Count);
    }

    [Fact]
    public async Task Create_AddsRouteToExistingModel_AndSyncsModelName()
    {
        using var context = new TestContext();
        var primary = context.SeedRoute("gpt-image-2", "1k", "primary");

        var created = await context.Service.CreateAsync(
            new CreateAiImageModelRouteRequest
            {
                ModelCode = "GPT-IMAGE-2",
                ModelName = "GPT Image 2 (renamed)",
                ResolutionCode = "1k",
                RouteRole = "fallback",
                Provider = "openai",
                ProviderModel = "gpt-image-2",
                BaseUrl = "https://backup.example.com/v1/",
                ApiKey = "sk-backup-key-5555"
            },
            default);

        Assert.Equal("gpt-image-2", created.ModelCode);
        Assert.Equal("fallback", created.RouteRole);
        Assert.Equal("https://backup.example.com/v1", created.BaseUrl);
        Assert.Equal(1, created.Revision);
        Assert.Equal("GPT Image 2 (renamed)", context.GetRoute(primary.Id).ModelName);
        Assert.Equal("create", context.Db.Queryable<AiImageModelConfigAuditEntity>().Single().Action);
    }

    [Fact]
    public async Task Create_RejectsDuplicateEvenWhenExistingRowIsDeleted()
    {
        using var context = new TestContext();
        context.SeedRoute("gpt-image-2", "1k", "primary");
        context.SeedRoute("gpt-image-2", "1k", "fallback", isDeleted: true);

        var ex = await Assert.ThrowsAsync<AppException>(() => context.Service.CreateAsync(
            NewRoute("gpt-image-2", "fallback"),
            default));

        Assert.Equal(MachineErrorCodes.Conflict, ex.MachineCode);
    }

    [Fact]
    public async Task Create_NewModelNeedsPrimaryRouteNameAndValidCode()
    {
        using var context = new TestContext();
        context.SeedRoute("gpt-image-2", "1k", "primary");

        var fallbackFirst = await Assert.ThrowsAsync<AppException>(() => context.Service.CreateAsync(
            NewRoute("brand-new-model", "fallback", modelName: "Brand New"), default));
        var unnamed = await Assert.ThrowsAsync<AppException>(() => context.Service.CreateAsync(NewRoute("brand-new-model", "primary"), default));
        var badCode = await Assert.ThrowsAsync<AppException>(() => context.Service.CreateAsync(
            NewRoute("brand new/model", "primary", modelName: "Brand New"), default));
        Assert.All([fallbackFirst, unnamed, badCode], ex => Assert.Equal(MachineErrorCodes.ValidationError, ex.MachineCode));

        var created = await context.Service.CreateAsync(
            NewRoute("Brand-New-Model", "primary", provider: "gemini-image", modelName: "Brand New", resolutionCode: ""),
            default);

        Assert.Equal("brand-new-model", created.ModelCode);
        Assert.Equal("Brand New", created.ModelName);
        Assert.Equal("primary", created.RouteRole);
        var groups = await context.Service.GetListAsync(new AiImageModelRouteQuery(), default);
        Assert.Equal("Brand New", groups.Single(x => x.ModelCode == "brand-new-model").ModelName);
        Assert.Equal("create", context.Db.Queryable<AiImageModelConfigAuditEntity>().Single().Action);

        // Later routes of the new model may be fallbacks, and keep the model's name when none is sent.
        var fallback = await context.Service.CreateAsync(NewRoute("brand-new-model", "fallback", provider: "gemini-image", resolutionCode: ""), default);
        Assert.Equal("Brand New", fallback.ModelName);
    }

    [Fact]
    public async Task Create_RejectsProtocolMismatch()
    {
        using var context = new TestContext();
        context.SeedRoute("gpt-image-2", "1k", "primary");

        var mismatch = await Assert.ThrowsAsync<AppException>(() => context.Service.CreateAsync(
            NewRoute("gpt-image-2", "fallback", provider: "gemini-image"),
            default));

        Assert.Equal(MachineErrorCodes.ValidationError, mismatch.MachineCode);
    }

    [Fact]
    public async Task GifProviderModel_IsConfigurableAuditedAndNotFrozenByReleases()
    {
        using var context = new TestContext();
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");
        var published = context.SeedRelease(route.Id, "published", revoked: false);

        var enabled = await context.Service.UpdateAsync(route.Id, UpdateFrom(route, gifProviderModel: "  gpt-image-2-native  "), default);
        Assert.Equal("gpt-image-2-native", enabled.GifProviderModel);
        Assert.Equal("gpt-image-2-native", context.GetRoute(route.Id).GifProviderModel);

        // Omitting the field (older admin clients) keeps the GIF model; an empty string switches GIF off.
        var renamed = await context.Service.UpdateAsync(route.Id, UpdateFrom(context.GetRoute(route.Id), sort: 7), default);
        Assert.Equal("gpt-image-2-native", renamed.GifProviderModel);
        var cleared = await context.Service.UpdateAsync(route.Id, UpdateFrom(context.GetRoute(route.Id), gifProviderModel: ""), default);
        Assert.Null(cleared.GifProviderModel);
        Assert.Null(context.GetRoute(route.Id).GifProviderModel);

        var audits = context.Db.Queryable<AiImageModelConfigAuditEntity>().OrderBy(x => x.Id).ToList();
        Assert.Equal(["gifProviderModel", "sort", "gifProviderModel"], audits.Select(x => x.ChangedFields));
        Assert.Contains("gpt-image-2-native", audits[0].AfterJson);
        Assert.NotNull(published);
    }

    [Fact]
    public async Task Create_AcceptsGifProviderModelOnlyForOpenAiRoutes()
    {
        using var context = new TestContext();
        context.SeedRoute("gpt-image-2", "1k", "primary");
        var request = NewRoute("gpt-image-2", "fallback");
        var created = await context.Service.CreateAsync(new CreateAiImageModelRouteRequest
        {
            ModelCode = request.ModelCode, ModelName = request.ModelName, ResolutionCode = request.ResolutionCode,
            RouteRole = request.RouteRole, Provider = request.Provider, ProviderModel = request.ProviderModel,
            BaseUrl = request.BaseUrl, ApiKey = request.ApiKey, GifProviderModel = "gpt-image-2-native"
        }, default);
        Assert.Equal("gpt-image-2-native", created.GifProviderModel);

        // A brand-new Gemini model would otherwise be created; the GIF field alone makes it invalid.
        var gemini = NewRoute("nano-gif-test", "primary", provider: "gemini-image", modelName: "Nano GIF Test");
        var rejected = await Assert.ThrowsAsync<AppException>(() => context.Service.CreateAsync(new CreateAiImageModelRouteRequest
        {
            ModelCode = gemini.ModelCode, ModelName = gemini.ModelName, ResolutionCode = gemini.ResolutionCode,
            RouteRole = gemini.RouteRole, Provider = gemini.Provider, ProviderModel = gemini.ProviderModel,
            BaseUrl = gemini.BaseUrl, ApiKey = gemini.ApiKey, GifProviderModel = "gemini-native"
        }, default));
        Assert.Contains("gifProviderModel", rejected.Message);
        Assert.Empty(context.Db.Queryable<AiImageModelConfigEntity>().Where(x => x.ModelCode == "nano-gif-test").ToList());
        Assert.Equal(MachineErrorCodes.ValidationError, rejected.MachineCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, AiImageModelRouteProbeResults.Passed)]
    [InlineData(HttpStatusCode.Unauthorized, AiImageModelRouteProbeResults.AuthFailed)]
    [InlineData(HttpStatusCode.NotFound, AiImageModelRouteProbeResults.Unreachable)]
    public async Task Probe_MapsStatusToThreeStates(HttpStatusCode status, string expected)
    {
        HttpRequestMessage? captured = null;
        using var context = new TestContext(probe: request =>
        {
            captured = request;
            return new HttpResponseMessage(status) { Content = new StringContent("{\"upstream\":\"detail\"}") };
        });
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var result = await context.Service.ProbeAsync(route.Id, default);

        Assert.Equal(expected, result.Result);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.Equal("https://relay.example.com/v1/models", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Probe_NetworkFailureIsUnreachable()
    {
        using var context = new TestContext(probe: _ => throw new HttpRequestException("connection refused"));
        var route = context.SeedRoute("gpt-image-2", "1k", "primary");

        var result = await context.Service.ProbeAsync(route.Id, default);

        Assert.Equal(AiImageModelRouteProbeResults.Unreachable, result.Result);
        Assert.Null(result.HttpStatus);
    }

    private static UpdateAiImageModelRouteRequest UpdateFrom(
        AiImageModelConfigEntity route,
        string? apiKey = null,
        string? providerModel = null,
        string? baseUrl = null,
        string? requestFormat = null,
        int? sort = null,
        string? gifProviderModel = null) => new()
    {
        Revision = route.Revision,
        ModelName = route.ModelName,
        ProviderModel = providerModel ?? route.ProviderModel,
        GifProviderModel = gifProviderModel,
        BaseUrl = baseUrl ?? route.BaseUrl,
        ApiKey = apiKey,
        TextToImagePath = route.TextToImagePath,
        ImageToImagePath = route.ImageToImagePath,
        RequestFormat = requestFormat ?? route.RequestFormat,
        Sort = sort ?? route.Sort
    };

    private static CreateAiImageModelRouteRequest NewRoute(
        string modelCode, string role, string provider = "openai", string? modelName = null, string resolutionCode = "1k") => new()
    {
        ModelCode = modelCode,
        ModelName = modelName ?? string.Empty,
        ResolutionCode = resolutionCode,
        RouteRole = role,
        Provider = provider,
        ProviderModel = modelCode,
        BaseUrl = "https://backup.example.com/v1",
        ApiKey = "sk-backup-key-5555"
    };

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            bool isSuperAdminClaim = true,
            Func<string, IPAddress[]>? resolve = null,
            Action<AiImageModelAdminOptions>? configure = null,
            Func<HttpRequestMessage, HttpResponseMessage>? probe = null)
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute
            });
            Db.Ado.ExecuteCommand("""
                CREATE TABLE sys_user (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, user_name TEXT NOT NULL, nick_name TEXT NULL,
                    password_hash TEXT NOT NULL, salt TEXT NULL, email TEXT NULL, phone TEXT NULL,
                    avatar_url TEXT NULL, signature TEXT NULL, point_balance INTEGER NOT NULL DEFAULT 0,
                    status INTEGER NOT NULL DEFAULT 1, is_super_admin INTEGER NOT NULL DEFAULT 0,
                    last_login_time TEXT NULL, last_login_ip TEXT NULL, remark TEXT NULL,
                    created_at TEXT NOT NULL, updated_at TEXT NULL, is_deleted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE ai_image_model_config (
                    gif_provider_model TEXT NULL,
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_code TEXT NOT NULL, model_name TEXT NOT NULL,
                    provider TEXT NOT NULL, provider_model TEXT NOT NULL, resolution_code TEXT NOT NULL,
                    route_role TEXT NOT NULL, base_url TEXT NOT NULL, api_key TEXT NOT NULL,
                    text_to_image_path TEXT NOT NULL, image_to_image_path TEXT NOT NULL,
                    request_format TEXT NOT NULL DEFAULT 'openai-size', sort INTEGER NOT NULL,
                    status INTEGER NOT NULL, revision INTEGER NOT NULL DEFAULT 0, api_key_updated_at TEXT NULL,
                    created_at TEXT NOT NULL, updated_at TEXT NULL, updated_by INTEGER NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    UNIQUE (model_code, resolution_code, route_role));
                CREATE TABLE ai_image_model_config_audit (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, config_id INTEGER NOT NULL, model_code TEXT NOT NULL,
                    resolution_code TEXT NOT NULL, route_role TEXT NOT NULL, action TEXT NOT NULL,
                    changed_fields TEXT NOT NULL, before_json TEXT NULL, after_json TEXT NULL,
                    operator_user_id INTEGER NOT NULL, operator_ip TEXT NULL, created_at TEXT NOT NULL);
                CREATE TABLE ai_image_model_release (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_code TEXT NOT NULL, model_name TEXT NOT NULL,
                    catalog_version TEXT NOT NULL, size_contract_version TEXT NOT NULL,
                    default_size_mode TEXT NOT NULL, status TEXT NOT NULL, revoked_at TEXT NULL,
                    created_at TEXT NOT NULL, published_at TEXT NULL);
                CREATE TABLE ai_image_model_release_route (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_release_id INTEGER NOT NULL,
                    route_config_id INTEGER NOT NULL, size_mode TEXT NOT NULL, resolution_code TEXT NOT NULL,
                    route_role TEXT NOT NULL, provider_protocol TEXT NOT NULL, consent_provider_code TEXT NOT NULL,
                    provider_model TEXT NOT NULL, base_url TEXT NOT NULL, text_to_image_path TEXT NOT NULL,
                    image_to_image_path TEXT NOT NULL, secret_version_hash TEXT NOT NULL,
                    verified_generations INTEGER NOT NULL DEFAULT 0, verified_edits INTEGER NOT NULL DEFAULT 0,
                    verified_mask_edits INTEGER NOT NULL DEFAULT 0, sort INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE ai_image_parameter (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, param_type TEXT NOT NULL, param_code TEXT NOT NULL,
                    param_name TEXT NOT NULL, provider_value TEXT NULL, value_int_1 INTEGER NULL,
                    value_int_2 INTEGER NULL, sort INTEGER NOT NULL, status INTEGER NOT NULL,
                    created_at TEXT NOT NULL, updated_at TEXT NULL, is_deleted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE ai_image_point_price (
                    vip_points INTEGER NULL, vip_price_mode TEXT DEFAULT 'follow', revision INTEGER DEFAULT 0, updated_by INTEGER NULL,
                    id INTEGER PRIMARY KEY AUTOINCREMENT, model_code TEXT NOT NULL, resolution_code TEXT NOT NULL,
                    quality_code TEXT NOT NULL, points INTEGER NOT NULL, price_amount NUMERIC NOT NULL,
                    currency TEXT NOT NULL, sort INTEGER NOT NULL, status INTEGER NOT NULL,
                    created_at TEXT NOT NULL, updated_at TEXT NULL, is_deleted INTEGER NOT NULL DEFAULT 0);
                INSERT INTO sys_user (id, user_name, password_hash, status, is_super_admin, created_at)
                VALUES (1, 'root', 'x', 1, 1, '2026-11-22 10:00:00');
                INSERT INTO ai_image_parameter (param_type, param_code, param_name, value_int_1, sort, status, created_at)
                VALUES ('resolution', '1k', '1K', 1024, 1, 1, '2026-11-22 10:00:00');
                """);

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(AdminId);
            currentUser.SetupGet(x => x.IsSuperAdmin).Returns(isSuperAdminClaim);
            var resolver = new Mock<IHostAddressResolver>();
            resolver
                .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string host, CancellationToken _) => resolve?.Invoke(host) ?? [IPAddress.Parse("93.184.216.34")]);
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory
                .Setup(x => x.CreateClient(AiImageModelRouteAdminService.ProbeHttpClientName))
                .Returns(() => new HttpClient(new StubHandler(probe ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)))));
            var status = new Mock<IAiImageModelStatusService>();
            status
                .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AiImageModelStatusResponse { GeneratedAt = DateTime.UtcNow });
            var options = new AiImageModelAdminOptions();
            configure?.Invoke(options);

            Service = new AiImageModelRouteAdminService(
                Db,
                currentUser.Object,
                new HttpContextAccessor(),
                resolver.Object,
                httpClientFactory.Object,
                status.Object,
                Options.Create(options),
                NullLogger<AiImageModelRouteAdminService>.Instance);
        }

        public SqlSugarClient Db { get; }

        public AiImageModelRouteAdminService Service { get; }

        public AiImageModelConfigEntity SeedRoute(string modelCode, string resolution, string role, bool isDeleted = false)
        {
            var entity = new AiImageModelConfigEntity
            {
                ModelCode = modelCode,
                ModelName = "GPT Image 2",
                Provider = "openai",
                ProviderModel = modelCode,
                ResolutionCode = resolution,
                RouteRole = role,
                BaseUrl = "https://relay.example.com/v1",
                ApiKey = OriginalKey,
                TextToImagePath = "/images/generations",
                ImageToImagePath = "/images/edits",
                RequestFormat = "openai-size",
                Sort = 1,
                Status = 1,
                Revision = 1,
                CreatedAt = DateTime.Now,
                IsDeleted = isDeleted
            };
            entity.Id = Db.Insertable(entity).ExecuteReturnBigIdentity();
            return entity;
        }

        public void SeedPrice(string modelCode, string resolution) =>
            Db.Insertable(new AiImagePointPriceEntity
            {
                ModelCode = modelCode,
                ResolutionCode = resolution,
                QualityCode = "med",
                Points = 10,
                Currency = "CNY",
                Status = 1,
                CreatedAt = DateTime.Now
            }).ExecuteCommand();

        public long SeedRelease(long configId, string status, bool revoked)
        {
            var catalogVersion = revoked ? "v-revoked" : "v-published";
            var releaseId = Db.Insertable(new AiImageModelReleaseEntity
            {
                ModelCode = "gpt-image-2",
                ModelName = "GPT Image 2",
                CatalogVersion = catalogVersion,
                SizeContractVersion = "size-mode-v1",
                DefaultSizeMode = "explicit",
                Status = status,
                RevokedAt = revoked ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                PublishedAt = DateTime.UtcNow
            }).ExecuteReturnBigIdentity();
            return Db.Insertable(new AiImageModelReleaseRouteEntity
            {
                ModelReleaseId = releaseId,
                RouteConfigId = configId,
                SizeMode = "explicit",
                ResolutionCode = "1k",
                RouteRole = "primary",
                ProviderProtocol = "openai-image",
                ConsentProviderCode = "openai",
                ProviderModel = "gpt-image-2",
                BaseUrl = "https://relay.example.com/v1",
                TextToImagePath = "/images/generations",
                ImageToImagePath = "/images/edits",
                SecretVersionHash = Sha256(OriginalKey)
            }).ExecuteReturnBigIdentity();
        }

        public AiImageModelConfigEntity GetRoute(long id) => Db.Queryable<AiImageModelConfigEntity>().Single(x => x.Id == id);

        public string GetReleaseRouteHash(long releaseRouteId) =>
            Db.Queryable<AiImageModelReleaseRouteEntity>().Single(x => x.Id == releaseRouteId).SecretVersionHash;

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
