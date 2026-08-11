using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace jokester.admin.Tests;

public sealed class SecurityPipelineTests : IClassFixture<SecurityWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly SecurityWebApplicationFactory _factory;

    public SecurityPipelineTests(SecurityWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task ProtectedEndpoints_DefaultToUnauthorized_AndIncludeSecurityHeaders()
    {
        var response = await _client.GetAsync("/api/points/balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task DevelopmentBootstrap_IsExplicitlyAnonymous_ButStillRequiresBootstrapSecret()
    {
        var response = await _client.PostAsync("/api/dev/bootstrap/super-admin", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PrivateMedia_DoesNotExposeFilesToAnonymousCallers()
    {
        var response = await _client.GetAsync("/api/media/ai/42/202608/nonexistent.png");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PromptCoverImages_ArePublicAndCacheable()
    {
        var directory = Path.Combine(_factory.PromptImageRoot, "youmind-gpt-image-2");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "1-test.jpg");
        await File.WriteAllBytesAsync(path, [0xff, 0xd8, 0xff, 0xd9]);

        var response = await _client.GetAsync("/prompt-images/youmind-gpt-image-2/1-test.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("public", response.Headers.CacheControl?.ToString());
        Assert.Equal([0xff, 0xd8, 0xff, 0xd9], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Cors_DoesNotReflectUnlistedOrigins()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "https://not-allowed.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

public sealed class SecurityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public string PromptImageRoot { get; } = Path.Combine(
        Path.GetTempPath(),
        "jokester-admin-tests",
        Guid.NewGuid().ToString("N"));

    public SecurityWebApplicationFactory()
    {
        SetEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        SetEnvironment("Jwt__Issuer", "https://test.example");
        SetEnvironment("Jwt__Audience", "jokester-tests");
        SetEnvironment("Jwt__SecretKey", "test-only-secret-that-is-longer-than-thirty-two-bytes");
        SetEnvironment("Jwt__AccessTokenExpiresMinutes", "15");
        SetEnvironment("Jwt__RefreshTokenExpiresDays", "7");
        SetEnvironment("Database__Provider", "MySql");
        SetEnvironment("Database__ConnectionString", "server=127.0.0.1;Database=unused;Uid=unused;Pwd=unused;");
        SetEnvironment("Redis__ConnectionString", "127.0.0.1:1,abortConnect=false,connectTimeout=100");
        SetEnvironment("Redis__InstanceName", "jokester-tests:");
        SetEnvironment("Redis__EnableInMemoryRefreshTokenFallback", "true");
        SetEnvironment("BootstrapAdmin__Secret", "test-bootstrap-secret");
        SetEnvironment("PROMPT_LIBRARY_ENABLED", "false");
        SetEnvironment("AiPromptFilter__Enabled", "false");
        SetEnvironment("PROMPT_IMAGE_ROOT", PromptImageRoot);
        SetEnvironment("PROMPT_IMAGE_PUBLIC_BASE", "/prompt-images");
        Directory.CreateDirectory(PromptImageRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://test.example",
                ["Jwt:Audience"] = "jokester-tests",
                ["Jwt:SecretKey"] = "test-only-secret-that-is-longer-than-thirty-two-bytes",
                ["Jwt:AccessTokenExpiresMinutes"] = "15",
                ["Jwt:RefreshTokenExpiresDays"] = "7",
                ["Database:Provider"] = "MySql",
                ["Database:ConnectionString"] = "server=127.0.0.1;Database=unused;Uid=unused;Pwd=unused;",
                ["Redis:ConnectionString"] = "127.0.0.1:1,abortConnect=false,connectTimeout=100",
                ["Redis:InstanceName"] = "jokester-tests:",
                ["Redis:EnableInMemoryRefreshTokenFallback"] = "true",
                ["BootstrapAdmin:Secret"] = "test-bootstrap-secret",
                ["PromptLibrary:Enabled"] = "false",
                ["AiPromptFilter:Enabled"] = "false",
                ["PromptLibrary:ImageRoot"] = PromptImageRoot,
                ["PromptLibrary:PublicBasePath"] = "/prompt-images"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var pair in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
        if (Directory.Exists(PromptImageRoot))
        {
            Directory.Delete(PromptImageRoot, true);
        }
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }
}
