using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Security;
using jokester.admin.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace jokester.admin.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
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
        services.AddScoped<IAiImageTaskProcessor, AiImageTaskProcessor>();
        services.AddHostedService<AiImageTaskWorker>();
        services.AddHttpClient<IAiImageService, AiImageService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHttpClient<INanoBananaImageService, NanoBananaImageService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddScoped<IAiImageModelConfigService, AiImageModelConfigService>();
        services.AddScoped<IPointService, PointService>();
        services.AddScoped<ILogService, LogService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IPermissionService, PermissionService>();

        return services;
    }
}
