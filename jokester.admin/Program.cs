using jokester.admin;
using jokester.admin.Application;
using jokester.admin.Application.Abstractions;
using jokester.admin.Configuration;
using jokester.admin.Infrastructure;
using jokester.admin.Middleware;

var rootDirectory = Directory.GetCurrentDirectory();
DotEnvConfiguration.LoadToEnvironment(
    rootDirectory,
    Path.Combine(rootDirectory, "jokester.admin"),
    AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "DefaultCors";

// 允许最大 100MB 的请求体，防止 IIS 反向代理或 Kestrel 层面返回 413
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100 MB
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services
    .AddPresentation()
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<PermissionMiddleware>();
app.UseMiddleware<OperationLogMiddleware>();
app.MapControllers();

app.Run();
