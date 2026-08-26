using System.Threading.RateLimiting;
using jokester.admin;
using jokester.admin.Application;
using jokester.admin.Application.Abstractions;
using jokester.admin.Configuration;
using jokester.admin.Infrastructure;
using jokester.admin.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Net;

var rootDirectory = Directory.GetCurrentDirectory();
var isAiMediaMigration = args.Contains("--migrate-ai-media", StringComparer.OrdinalIgnoreCase);
var isLegalDocumentConfiguration = args.Contains("--configure-legal-documents", StringComparer.OrdinalIgnoreCase);
var isAiImageCatalogConfiguration = args.Contains("--configure-ai-image-catalog", StringComparer.OrdinalIgnoreCase);
if (new[] { isAiMediaMigration, isLegalDocumentConfiguration, isAiImageCatalogConfiguration }.Count(x => x) > 1)
{
    throw new InvalidOperationException("Only one maintenance command can run at a time.");
}
var applicationArgs = args
    .Where(x => !string.Equals(x, "--configure-legal-documents", StringComparison.OrdinalIgnoreCase))
    .Where(x => !string.Equals(x, "--configure-ai-image-catalog", StringComparison.OrdinalIgnoreCase))
    .ToArray();
DotEnvConfiguration.LoadToEnvironment(
    rootDirectory,
    Path.Combine(rootDirectory, "jokester.admin"),
    AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(applicationArgs);
const string CorsPolicyName = "DefaultCors";
const string AuthPolicyName = "AuthAbuseProtection";
var swaggerEnabled = builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:Enabled");

// 上传业务上限为 10MB；仅为 multipart 边界预留少量空间。
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 12 * 1024 * 1024;
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
if (!isAiMediaMigration
    && !isLegalDocumentConfiguration
    && !isAiImageCatalogConfiguration
    && !builder.Environment.IsDevelopment()
    && securityOptions.AllowedOrigins.Length == 0)
{
    throw new InvalidOperationException("Security:AllowedOrigins must contain explicit production origins.");
}
if (securityOptions.AllowedOrigins.Any(origin =>
        origin == "*"
        || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        || uri.AbsolutePath != "/"
        || !string.IsNullOrEmpty(uri.Query)
        || !string.IsNullOrEmpty(uri.Fragment)
        || origin.EndsWith('/')))
{
    throw new InvalidOperationException("Security:AllowedOrigins must contain exact HTTP(S) origins without paths, wildcards, query strings, fragments, or trailing slashes.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(securityOptions.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = static async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            jokester.admin.Common.ApiErrorResponse.Failure(
                jokester.admin.Common.MachineErrorCodes.RateLimited,
                "Too many requests.",
                context.HttpContext.TraceIdentifier,
                new { retryAfterSeconds = 60 }),
            cancellationToken);
    };

    options.AddPolicy(AuthPolicyName, context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in securityOptions.KnownProxies)
    {
        if (!IPAddress.TryParse(proxy, out var address))
        {
            throw new InvalidOperationException($"Security:KnownProxies contains an invalid IP address: {proxy}");
        }
        options.KnownProxies.Add(address);
    }
});

builder.Services
    .AddPresentation()
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();
var publicMediaRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var publicBlogRoot = Path.Combine(publicMediaRoot, "blog");
var publicAvatarRoot = Path.Combine(publicMediaRoot, "avatar");
var promptLibraryOptions = app.Services.GetRequiredService<IOptions<PromptLibraryOptions>>().Value;
Directory.CreateDirectory(publicBlogRoot);
Directory.CreateDirectory(publicAvatarRoot);

if (isAiMediaMigration)
{
    var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SqlSugar.ISqlSugarClient>();
    var mediaPathResolver = scope.ServiceProvider.GetRequiredService<IAiMediaPathResolver>();
    var result = await AiMediaMigration.RunAsync(db, app.Environment.ContentRootPath, mediaPathResolver, dryRun);
    Console.WriteLine(
        $"AI media migration {(result.DryRun ? "dry run" : "completed")}: "
        + $"tasks={result.UpdatedTaskCount}, favorites={result.UpdatedFavoriteCount}, "
        + $"files={result.CopiedFileCount}, unreferencedLegacyFiles={result.OrphanLegacyFileCount}");
    return;
}

if (isLegalDocumentConfiguration)
{
    var options = builder.Configuration
        .GetSection(LegalDocumentConfigurationOptions.SectionName)
        .Get<LegalDocumentConfigurationOptions>() ?? new LegalDocumentConfigurationOptions();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SqlSugar.ISqlSugarClient>();
    var result = await LegalDocumentConfiguration.RunAsync(db, options);
    Console.WriteLine(
        $"Configured legal documents for {result.Platform}/{result.Locale}: "
        + $"types={string.Join(',', result.DocumentTypes)}, "
        + $"providers={string.Join(',', result.ProviderCodes)}, "
        + $"effectiveAt={result.EffectiveAt:O}");
    return;
}

if (isAiImageCatalogConfiguration)
{
    var options = builder.Configuration
        .GetSection(AiImageCatalogReleaseConfigurationOptions.SectionName)
        .Get<AiImageCatalogReleaseConfigurationOptions>() ?? new AiImageCatalogReleaseConfigurationOptions();
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SqlSugar.ISqlSugarClient>();
    var result = await AiImageCatalogReleaseConfiguration.RunAsync(db, options);
    Console.WriteLine(
        $"Configured AI image catalog {result.ModelCode}/{result.CatalogVersion}: "
        + $"releaseId={result.ModelReleaseId}, explicitRoutes={result.ExplicitRouteCount}, "
        + $"autoRoutes={result.AutoRouteCount}, explicitPrices={result.ExplicitPriceCount}, "
        + $"autoPrices={result.AutoPriceCount}, reused={result.ReusedExistingRelease}");
    return;
}

app.UseMiddleware<GlobalExceptionMiddleware>();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/blog", FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicBlogRoot) });
app.UseStaticFiles(new StaticFileOptions { RequestPath = "/avatar", FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicAvatarRoot) });
if (!string.IsNullOrWhiteSpace(promptLibraryOptions.ImageRoot))
{
    var promptImageRoot = Path.GetFullPath(promptLibraryOptions.ImageRoot);
    Directory.CreateDirectory(promptImageRoot);
    app.UseStaticFiles(new StaticFileOptions
    {
        RequestPath = promptLibraryOptions.PublicBasePath,
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(promptImageRoot),
        OnPrepareResponse = context =>
            context.Context.Response.Headers.CacheControl = "public, max-age=604800, immutable"
    });
}
app.UseCors(CorsPolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<OperationLogMiddleware>();
app.UseMiddleware<SecurityRateLimitMiddleware>();
app.UseMiddleware<PermissionMiddleware>();
app.MapControllers();

app.Run();

public partial class Program;
