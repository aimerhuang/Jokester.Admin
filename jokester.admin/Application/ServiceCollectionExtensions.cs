using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Security;
using jokester.admin.Application.Services;
using jokester.admin.Infrastructure;
using jokester.admin.Infrastructure.PromptLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace jokester.admin.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<ILegalDocumentService, LegalDocumentService>();
        services.AddScoped<IUserConsentService, UserConsentService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IMediaAssetService, MediaAssetService>();
        services.AddHostedService<AccountDeletionWorker>();
        services.AddHttpClient<IEmailValidationService, EmailValidationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddScoped<IBlogArticleService, BlogArticleService>();
        services.AddScoped<IBlogMediaService, BlogMediaService>();
        services.AddScoped<IBlogCommentService, BlogCommentService>();
        services.AddScoped<IBlogDashboardService, BlogDashboardService>();
        services.AddScoped<IBlogCategoryService, BlogCategoryService>();
        services.AddScoped<IBlogReadService, BlogReadService>();
        services.AddSingleton<IBlogCaptchaService, BlogCaptchaService>();
        services.AddSingleton<IAiImageTaskQueue, AiImageTaskQueue>();
        services.AddSingleton<IAiImageAdmissionService, AiImageAdmissionService>();
        services.AddSingleton<IAiImageProviderGate, AiImageProviderGate>();
        services.AddSingleton<IAiPromptFilter, AiPromptFilterService>();
        services.AddScoped<IAiImageTaskProcessor, AiImageTaskProcessor>();
        services.AddHostedService<AiImageTaskWorker>();
        services.AddHostedService<AiImageTaskRecoveryWorker>();
        services.AddHostedService<AiPromptFilterRefreshWorker>();
        services.AddSingleton<IPromptLibrarySyncQueue, PromptLibrarySyncQueue>();
        services.AddHostedService<PromptLibrarySyncWorker>();
        services.AddHttpClient<IPromptLibraryImageStore, PromptLibraryImageStore>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Jokester-PromptLibrarySync/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PromptLibraryOptions>>().Value;
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
                    Proxy = string.IsNullOrWhiteSpace(options.HttpProxy)
                        ? null
                        : new WebProxy(options.HttpProxy),
                    UseProxy = !string.IsNullOrWhiteSpace(options.HttpProxy),
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
            });
        services.AddHttpClient<IPromptLibrarySourceClient, YouMarketingPromptSourceClient>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Jokester-PromptLibrarySync/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PromptLibraryOptions>>().Value;
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
                    Proxy = string.IsNullOrWhiteSpace(options.HttpProxy)
                        ? null
                        : new WebProxy(options.HttpProxy),
                    UseProxy = !string.IsNullOrWhiteSpace(options.HttpProxy),
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
            });
        services.AddHttpClient<IAiImageService, AiImageService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            ConnectCallback = OutboundNetworkGuard.ConnectPublicAsync,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        services.AddHttpClient<INanoBananaImageService, NanoBananaImageService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddScoped<IAiImageModelConfigService, AiImageModelConfigService>();
        services.AddScoped<IAiImageCatalogService, AiImageCatalogService>();
        services.AddScoped<IAiSizeModeRolloutPolicy, AiSizeModeRolloutPolicy>();
        services.AddScoped<IAiPromptSensitiveWordService, AiPromptSensitiveWordService>();
        services.AddScoped<IPointService, PointService>();
        services.AddScoped<IPointRechargeService, PointRechargeService>();
        services.AddScoped<IAppleIapService, AppleIapService>();
        services.AddHostedService<AppleNotificationWorker>();
        services.AddScoped<IPromptLibraryService, PromptLibraryService>();
        services.AddScoped<IPromptLibrarySyncAdminService, PromptLibrarySyncAdminService>();
        services.AddScoped<IPromptLibrarySyncRunner, PromptLibrarySyncRunner>();
        services.AddScoped<ILogService, LogService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IPermissionService, PermissionService>();

        return services;
    }
}
