using System.Text.Json;
using System.Text.Json.Serialization;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Legal;
using jokester.admin.Application.DTOs.Mobile;
using jokester.admin.Application.Services;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Controllers;
using jokester.admin.Domain.Entities;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class LegalAndMobileContractTests
{
    [Fact]
    public async Task LegalController_ReturnsCurrentRegistrationDocumentsWithoutAiNotice()
    {
        var legalService = new Mock<ILegalDocumentService>();
        legalService.Setup(x => x.GetCurrentAsync("web", "zh-CN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentLegalDocumentsResponse
            {
                PrivacyPolicy = new LegalDocumentDto
                {
                    Version = "privacy-2026-08-13",
                    Url = "https://app.example.com/legal/privacy/index.html"
                },
                TermsOfService = new LegalDocumentDto
                {
                    Version = "terms-2026-08-13",
                    Url = "https://app.example.com/legal/terms/index.html"
                },
                AiProcessingNotice = null
            });
        var controller = new LegalController(legalService.Object);

        var action = await controller.GetCurrent("web", "zh-CN", default);
        var response = Assert.IsType<ApiResponse<CurrentLegalDocumentsResponse>>(
            Assert.IsType<OkObjectResult>(action).Value);

        Assert.Equal("privacy-2026-08-13", response.Data!.PrivacyPolicy.Version);
        Assert.Equal("terms-2026-08-13", response.Data.TermsOfService.Version);
        Assert.Null(response.Data.AiProcessingNotice);
    }

    [Fact]
    public void LegalDocuments_ResponseKeepsNullAiProcessingNotice()
    {
        var json = JsonSerializer.Serialize(
            new CurrentLegalDocumentsResponse(),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

        Assert.Contains("\"aiProcessingNotice\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegalConfiguration_IsIdempotentAndActivatesAllWebDocuments()
    {
        using var context = new LegalContext();
        context.SeedEnabledProviders();
        var now = new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc);
        var options = ApprovedWebLegalOptions();

        var first = await LegalDocumentConfiguration.RunAsync(context.Db, options, now);
        var second = await LegalDocumentConfiguration.RunAsync(context.Db, options, now.AddMinutes(1));
        var documents = await context.Service.GetCurrentAsync("web", "zh-CN", default);

        Assert.Equal(["ai_processing", "privacy_policy", "terms_of_service"],
            context.Db.Queryable<LegalDocumentEntity>()
                .Where(x => x.Status == 1)
                .OrderBy(x => x.DocumentType)
                .Select(x => x.DocumentType)
                .ToList());
        Assert.Equal("privacy-2026-08-13", documents.PrivacyPolicy.Version);
        Assert.True(documents.PrivacyPolicy.RequiresReconsent);
        Assert.False(documents.TermsOfService.RequiresReconsent);
        Assert.Equal("terms-2026-08-13", documents.TermsOfService.Version);
        var aiProcessingNotice = Assert.IsType<AiProcessingNoticeDto>(documents.AiProcessingNotice);
        Assert.Equal("ai-2026-08-13", aiProcessingNotice.Version);
        Assert.Equal(["google", "openai"], aiProcessingNotice.ProviderCodes);
        Assert.Equal(first.DocumentTypes, second.DocumentTypes);
        Assert.Equal(DateTimeKind.Utc, documents.PrivacyPolicy.EffectiveAt.Kind);
    }

    [Fact]
    public async Task LegalConfiguration_AllowsPrivacyAndTermsWithoutAiProcessing()
    {
        using var context = new LegalContext();
        context.SeedEnabledProviders();
        var options = ApprovedWebLegalOptions() with
        {
            Platform = "all",
            AiProcessing = new AiProcessingDocumentDefinitionOptions { Enabled = false }
        };

        var result = await LegalDocumentConfiguration.RunAsync(
            context.Db,
            options,
            new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc));
        var documents = await context.Service.GetCurrentAsync("web", "zh-CN", default);

        Assert.Equal(["privacy_policy", "terms_of_service"], result.DocumentTypes);
        Assert.Empty(result.ProviderCodes);
        Assert.Null(documents.AiProcessingNotice);
        Assert.Equal(2, context.Db.Queryable<LegalDocumentEntity>().Where(x => x.Status == 1).Count());
    }

    [Fact]
    public async Task LegalConfiguration_DisablingAiProcessingDeactivatesTheCurrentExactScopeNotice()
    {
        using var context = new LegalContext();
        context.SeedCurrentDocuments();
        var options = ApprovedWebLegalOptions() with
        {
            Platform = "ios",
            AiProcessing = new AiProcessingDocumentDefinitionOptions { Enabled = false }
        };

        await LegalDocumentConfiguration.RunAsync(
            context.Db,
            options,
            new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc));
        var documents = await context.Service.GetCurrentAsync("ios", "zh-CN", default);

        Assert.Null(documents.AiProcessingNotice);
        Assert.Equal(0, context.Db.Queryable<LegalDocumentEntity>()
            .Where(x => x.DocumentType == LegalDocumentService.AiProcessingType && x.Status == 1)
            .Count());
    }

    [Fact]
    public async Task LegalConfiguration_RejectsUnapprovedValuesWithoutWriting()
    {
        using var context = new LegalContext();
        var options = ApprovedWebLegalOptions() with { Approved = false };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LegalDocumentConfiguration.RunAsync(
                context.Db,
                options,
                new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("Approved", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.Db.Queryable<LegalDocumentEntity>().ToList());
    }

    [Fact]
    public async Task LegalConfiguration_RequiresEveryEnabledAiProvider()
    {
        using var context = new LegalContext();
        context.SeedEnabledProviders();
        var options = ApprovedWebLegalOptions() with
        {
            AiProcessing = new AiProcessingDocumentDefinitionOptions
            {
                Version = "ai-2026-08-13",
                Url = "https://legal.jokester.test/ai-processing",
                ProviderCodes = ["openai"]
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LegalDocumentConfiguration.RunAsync(
                context.Db,
                options,
                new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("google", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.Db.Queryable<LegalDocumentEntity>().ToList());
    }

    [Fact]
    public async Task LegalConfiguration_RejectsChangingAnExistingVersion()
    {
        using var context = new LegalContext();
        context.SeedEnabledProviders();
        var now = new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc);
        var options = ApprovedWebLegalOptions();
        await LegalDocumentConfiguration.RunAsync(context.Db, options, now);
        var changed = options with
        {
            PrivacyPolicy = new LegalDocumentDefinitionOptions
            {
                Version = options.PrivacyPolicy.Version,
                Url = "https://legal.jokester.test/replaced-privacy",
                RequiresReconsent = options.PrivacyPolicy.RequiresReconsent
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LegalDocumentConfiguration.RunAsync(context.Db, changed, now.AddMinutes(1)));
        var documents = await context.Service.GetCurrentAsync("web", "zh-CN", default);

        Assert.Contains("new version", exception.Message, StringComparison.Ordinal);
        Assert.Equal("https://legal.jokester.test/privacy", documents.PrivacyPolicy.Url);
        Assert.Equal(3, context.Db.Queryable<LegalDocumentEntity>().Where(x => x.Status == 1).Count());
    }

    [Fact]
    public async Task LegalConfiguration_AllPlatformFailsWhenAnExactScopeOverridesIt()
    {
        using var context = new LegalContext();
        context.SeedEnabledProviders();
        context.SeedCurrentDocuments();
        var options = ApprovedWebLegalOptions() with { Platform = "all" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LegalDocumentConfiguration.RunAsync(
                context.Db,
                options,
                new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc)));

        Assert.Contains("ios/zh-CN", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.Db.Queryable<LegalDocumentEntity>().Where(x => x.Platform == "all").ToList());
        Assert.Equal(3, context.Db.Queryable<LegalDocumentEntity>().Where(x => x.Platform == "ios" && x.Status == 1).Count());
    }

    [Fact]
    public async Task AiConsent_MustCoverCurrentVersionAndSelectedProvider()
    {
        using var context = new LegalContext();
        context.SeedCurrentDocuments();
        context.Db.Insertable(new UserConsentEntity
        {
            UserId = 7,
            ConsentType = LegalDocumentService.AiProcessingType,
            DocumentVersion = "ai-v3",
            ProviderCodesJson = JsonSerializer.Serialize(new[] { "openai" }),
            Accepted = true,
            ClientPlatform = "ios",
            AcceptedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommand();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(7);
        var service = new UserConsentService(context.Db, currentUser.Object, context.Service);

        await service.EnsureAiProcessingConsentAsync(7, "openai", default);
        var consents = await service.GetCurrentUserConsentsAsync(default);
        Assert.Equal(DateTimeKind.Utc, consents.AiProcessing!.AcceptedAt!.Value.Kind);
        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.EnsureAiProcessingConsentAsync(7, "google", default));

        Assert.Equal(ErrorCodes.PreconditionFailed, exception.Code);
        Assert.Equal(MachineErrorCodes.AiConsentRequired, exception.MachineCode);
        Assert.NotNull(exception.Details);
    }

    [Fact]
    public async Task AiConsent_IsUnavailableWithoutAnApprovedAiProcessingNotice()
    {
        using var context = new LegalContext();
        var options = ApprovedWebLegalOptions() with
        {
            Platform = "all",
            AiProcessing = new AiProcessingDocumentDefinitionOptions { Enabled = false }
        };
        await LegalDocumentConfiguration.RunAsync(
            context.Db,
            options,
            new DateTime(2026, 8, 13, 4, 0, 0, DateTimeKind.Utc));
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(7);
        var service = new UserConsentService(context.Db, currentUser.Object, context.Service);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.EnsureAiProcessingConsentAsync(7, "openai", default));

        Assert.Equal(ErrorCodes.ServiceUnavailable, exception.Code);
        Assert.Equal(MachineErrorCodes.ServiceUnavailable, exception.MachineCode);
        Assert.Empty(context.Db.Queryable<UserConsentEntity>().ToList());
    }

    [Fact]
    public async Task AiConsent_UsesAcceptedPlatformWithoutRegistrationDocuments()
    {
        using var context = new LegalContext();
        var effectiveAt = DateTime.UtcNow.AddMinutes(-1);
        context.Db.Insertable(new LegalDocumentEntity
        {
            DocumentType = LegalDocumentService.AiProcessingType,
            Version = "ai-web-v1",
            Platform = "web",
            Locale = "zh-CN",
            Url = "https://legal.jokester.test/ai-processing",
            ProviderCodesJson = JsonSerializer.Serialize(new[] { "openai" }),
            EffectiveAt = effectiveAt,
            Status = 1,
            CreatedAt = effectiveAt
        }).ExecuteCommand();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(7);
        var service = new UserConsentService(context.Db, currentUser.Object, context.Service);

        var consent = await service.UpdateAiProcessingAsync(
            new UpdateAiProcessingConsentRequest
            {
                Accepted = true,
                DocumentVersion = "ai-web-v1",
                ProviderCodes = ["openai"],
                ClientPlatform = "web"
            },
            default);
        await service.EnsureAiProcessingConsentAsync(7, "openai", default);

        Assert.Equal("web", consent.ClientPlatform);
        Assert.Equal("ai-web-v1", consent.DocumentVersion);
    }

    [Fact]
    public async Task MobileConfig_CombinesFeatureFlagsWithRuntimeCapabilities()
    {
        var legalService = new Mock<ILegalDocumentService>();
        legalService.Setup(x => x.GetCurrentAsync("ios", "zh-CN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentLegalDocumentsResponse
            {
                PrivacyPolicy = new LegalDocumentDto { Version = "privacy-v2" },
                TermsOfService = new LegalDocumentDto { Version = "terms-v2" },
                AiProcessingNotice = new AiProcessingNoticeDto { Version = "ai-v3" }
            });
        var controller = new MobileController(
            legalService.Object,
            Options.Create(new MobileConfigurationOptions
            {
                MinimumSupportedVersion = "1.3.0",
                LatestVersion = "1.4.0",
                MaintenanceMode = true,
                Features = new MobileFeatureOptions
                {
                    AppleIap = true,
                    AccountDeletion = true,
                    PromptLibrary = true
                }
            }),
            Options.Create(new AppleAppStoreOptions { Enabled = false }),
            Options.Create(new PromptLibraryOptions { Enabled = false }));

        var action = await controller.GetConfiguration("ios", "1.3.0", "zh-CN", default);
        var response = Assert.IsType<ApiResponse<MobileConfigurationDto>>(
            Assert.IsType<OkObjectResult>(action).Value);

        Assert.Equal("1.3.0", response.Data!.MinimumSupportedVersion);
        Assert.True(response.Data.MaintenanceMode);
        Assert.False(response.Data.Features.AppleIap);
        Assert.False(response.Data.Features.PromptLibrary);
        Assert.True(response.Data.Features.AccountDeletion);
        Assert.Equal("ai-v3", response.Data.LegalDocumentVersions.AiProcessing);
    }

    private sealed class LegalContext : IDisposable
    {
        public LegalContext()
        {
            SQLitePCL.Batteries_V2.Init();
            Db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = "Data Source=:memory:",
                DbType = SqlSugar.DbType.Sqlite,
                IsAutoCloseConnection = false
            });
            Db.Ado.ExecuteCommand("""
                CREATE TABLE legal_document (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    document_type TEXT NOT NULL,
                    version TEXT NOT NULL,
                    platform TEXT NOT NULL,
                    locale TEXT NOT NULL,
                    url TEXT NOT NULL,
                    provider_codes_json TEXT NULL,
                    effective_at TEXT NOT NULL,
                    requires_reconsent INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NULL
                );
                CREATE TABLE user_consent (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    consent_type TEXT NOT NULL,
                    document_version TEXT NOT NULL,
                    provider_codes_json TEXT NULL,
                    accepted INTEGER NOT NULL,
                    client_platform TEXT NOT NULL,
                    accepted_at TEXT NULL,
                    revoked_at TEXT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE TABLE ai_image_model_config (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    provider TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    is_deleted INTEGER NOT NULL
                );
                """);
            Service = new LegalDocumentService(Db);
        }

        public SqlSugarClient Db { get; }

        public LegalDocumentService Service { get; }

        public void SeedCurrentDocuments()
        {
            var now = DateTime.UtcNow.AddMinutes(-1);
            Db.Insertable(new[]
            {
                Document(LegalDocumentService.PrivacyPolicyType, "privacy-v2", now),
                Document(LegalDocumentService.TermsOfServiceType, "terms-v2", now),
                Document(
                    LegalDocumentService.AiProcessingType,
                    "ai-v3",
                    now,
                    JsonSerializer.Serialize(new[] { "openai", "google" }))
            }).ExecuteCommand();
        }

        public void SeedEnabledProviders()
        {
            Db.Ado.ExecuteCommand("""
                INSERT INTO ai_image_model_config (provider, status, is_deleted)
                VALUES ('openai-image', 1, 0), ('gemini-image', 1, 0);
                """);
        }

        public void Dispose() => Db.Dispose();

        private static LegalDocumentEntity Document(
            string type,
            string version,
            DateTime effectiveAt,
            string? providers = null) => new()
            {
                DocumentType = type,
                Version = version,
                Platform = "ios",
                Locale = "zh-CN",
                Url = $"https://example.test/legal/{type}",
                ProviderCodesJson = providers,
                EffectiveAt = effectiveAt,
                Status = 1,
                CreatedAt = effectiveAt
            };
    }

    private static LegalDocumentConfigurationOptions ApprovedWebLegalOptions() => new()
    {
        Approved = true,
        Platform = "web",
        Locale = "zh-CN",
        EffectiveAt = "2026-08-12T03:00:00Z",
        PrivacyPolicy = new LegalDocumentDefinitionOptions
        {
            Version = "privacy-2026-08-13",
            Url = "https://legal.jokester.test/privacy",
            RequiresReconsent = true
        },
        TermsOfService = new LegalDocumentDefinitionOptions
        {
            Version = "terms-2026-08-13",
            Url = "https://legal.jokester.test/terms"
        },
        AiProcessing = new AiProcessingDocumentDefinitionOptions
        {
            Version = "ai-2026-08-13",
            Url = "https://legal.jokester.test/ai-processing",
            ProviderCodes = ["openai", "google"]
        }
    };
}
