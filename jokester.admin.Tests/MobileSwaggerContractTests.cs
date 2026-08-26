using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace jokester.admin.Tests;

[Collection(SecurityWebApplicationCollection.Name)]
public sealed class MobileSwaggerContractTests
{
    private static readonly string[] ErrorStatusCodes =
        ["400", "401", "403", "409", "412", "422", "429", "500", "503"];

    private static readonly (string Path, string Method)[] MobileOperations =
    [
        ("/api/auth/login", "post"),
        ("/api/auth/refresh", "post"),
        ("/api/auth/profile", "get"),
        ("/api/auth/register", "post"),
        ("/api/auth/register/email-code", "post"),
        ("/api/ai/images/models", "get"),
        ("/api/ai/images/parameters", "get"),
        ("/api/ai/images/pricing-options", "get"),
        ("/api/ai/images", "get"),
        ("/api/ai/images", "post"),
        ("/api/ai/images/{id}", "get"),
        ("/api/ai/images/upload", "post"),
        ("/api/ai/images/{id}/favorite", "post"),
        ("/api/assets/{assetId}", "delete"),
        ("/api/prompts", "get"),
        ("/api/prompts/{id}", "get"),
        ("/api/prompts/{id}/events", "post"),
        ("/api/points/balance", "get"),
        ("/api/points/details", "get"),
        ("/api/points/sign-in", "post"),
        ("/api/points/recharge/packages", "get"),
        ("/api/points/recharge/apple/transactions", "post"),
        ("/api/Users/{id}/nickname", "put"),
        ("/api/Users/{id}/password", "put"),
        ("/api/Users/{id}/avatar", "post"),
        ("/api/auth/account-deletion/requests", "post"),
        ("/api/auth/account-deletion/requests/current", "get"),
        ("/api/auth/account-deletion/requests/{requestId}", "delete"),
        ("/api/legal/documents/current", "get"),
        ("/api/users/me/consents", "get"),
        ("/api/users/me/consents/ai-processing", "put"),
        ("/api/mobile/config", "get")
    ];

    private readonly HttpClient _client;

    public MobileSwaggerContractTests(SecurityWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task MobileOperations_ExposeTypedSuccessAndErrorResponses()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        foreach (var (path, method) in MobileOperations)
        {
            var operation = FindPath(paths, path).GetProperty(method);
            var responses = operation.GetProperty("responses");
            AssertSchema(responses.GetProperty("200"), path, method, "200");
            foreach (var statusCode in ErrorStatusCodes)
            {
                var errorResponse = responses.GetProperty(statusCode);
                AssertSchema(errorResponse, path, method, statusCode);
                var schema = errorResponse.GetProperty("content").GetProperty("application/json").GetProperty("schema");
                Assert.EndsWith("/ApiErrorResponse", schema.GetProperty("$ref").GetString(), StringComparison.Ordinal);
            }
        }

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var emailCodeRequest = schemas.GetProperty("SendRegisterEmailCodeRequest");
        var emailCodeRequestProperties = emailCodeRequest
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(["email"], emailCodeRequestProperties);

        var registerRequest = schemas.GetProperty("RegisterRequest");
        var registerRequestProperties = registerRequest
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(["email", "emailCode", "password"], registerRequestProperties);

        var emailCodeResponse = schemas.GetProperty("SendRegisterEmailCodeResponse");
        Assert.True(emailCodeResponse.GetProperty("properties").TryGetProperty("retryAfterSeconds", out _));

        var userProfile = schemas.GetProperty("UserProfileDto").GetProperty("properties");
        Assert.True(userProfile.TryGetProperty("membership", out _));
        var membership = schemas.GetProperty("UserMembershipDto").GetProperty("properties");
        Assert.True(membership.TryGetProperty("tierCode", out _));
        Assert.True(membership.TryGetProperty("status", out _));
        Assert.Equal("string", membership.GetProperty("expiresAt").GetProperty("type").GetString());
        Assert.Equal("date-time", membership.GetProperty("expiresAt").GetProperty("format").GetString());

        var pricingOperation = FindPath(paths, "/api/ai/images/pricing-options").GetProperty("get");
        var pricingSchema = pricingOperation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        var pricingVariants = pricingSchema.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, pricingVariants.Length);
        Assert.Contains(pricingVariants, variant =>
            variant.GetProperty("$ref").GetString()!.Contains("AiImageCatalogPricingResponse", StringComparison.Ordinal));

        var pricingHeaders = pricingOperation
            .GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "header")
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("X-Client-Capabilities", pricingHeaders);
        Assert.Contains("X-Client-Platform", pricingHeaders);
        Assert.Contains("X-Client-Version", pricingHeaders);
        Assert.Contains("X-Client-Build", pricingHeaders);
    }

    private static JsonElement FindPath(JsonElement paths, string expected)
    {
        foreach (var candidate in paths.EnumerateObject())
        {
            if (string.Equals(candidate.Name, expected, StringComparison.OrdinalIgnoreCase)) return candidate.Value;
        }
        throw new Xunit.Sdk.XunitException($"Swagger path was not found: {expected}");
    }

    private static void AssertSchema(JsonElement response, string path, string method, string statusCode)
    {
        Assert.True(
            response.TryGetProperty("content", out var content)
            && content.TryGetProperty("application/json", out var mediaType)
            && mediaType.TryGetProperty("schema", out var schema)
            && schema.ValueKind == JsonValueKind.Object
            && schema.EnumerateObject().Any(),
            $"{method.ToUpperInvariant()} {path} response {statusCode} does not expose a JSON schema.");
    }
}
