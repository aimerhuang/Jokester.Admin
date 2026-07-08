using System.Threading.RateLimiting;
using jokester.admin;
using jokester.admin.Application;
using jokester.admin.Application.Abstractions;
using jokester.admin.Configuration;
using jokester.admin.Infrastructure;
using jokester.admin.Middleware;
using Microsoft.AspNetCore.RateLimiting;

var rootDirectory = Directory.GetCurrentDirectory();
DotEnvConfiguration.LoadToEnvironment(
    rootDirectory,
    Path.Combine(rootDirectory, "jokester.admin"),
    AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "DefaultCors";
const string AuthPolicyName = "AuthAbuseProtection";

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? GetAllowedOrigins(builder.Configuration["ASPNETCORE_URLS"]);

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = static (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
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

builder.Services
    .AddPresentation()
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors(CorsPolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<OperationLogMiddleware>();
app.UseMiddleware<PermissionMiddleware>();
app.MapControllers();

app.Run();

static string[] GetAllowedOrigins(string? aspNetCoreUrls)
{
    if (string.IsNullOrWhiteSpace(aspNetCoreUrls))
    {
        return ["http://localhost:5049"];
    }

    var origins = aspNetCoreUrls
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(url => new Uri(url).GetLeftPart(UriPartial.Authority))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return origins.Length > 0 ? origins : ["http://localhost:5049"];
}
