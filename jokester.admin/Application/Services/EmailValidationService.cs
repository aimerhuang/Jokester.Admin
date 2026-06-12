using System.Net.Mail;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Options;

namespace jokester.admin.Application.Services;

public sealed class EmailValidationService(
    HttpClient httpClient,
    IOptions<EmailValidationOptions> options) : IEmailValidationService
{
    private static readonly string[] ValidPropertyNames = ["valid", "isValid", "deliverable", "isDeliverable"];

    public async Task<string> ValidateAndNormalizeAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = ValidateFormat(email);
        ValidateBlacklist(normalized);
        if (options.Value.EnableApiValidation)
        {
            await ValidateByApiAsync(normalized, cancellationToken);
        }

        return normalized;
    }

    private static string ValidateFormat(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 100)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid email");
        }

        try
        {
            var address = new MailAddress(email.Trim());
            if (!string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(address.Host))
            {
                throw new AppException(ErrorCodes.BadRequest, "Invalid email");
            }

            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid email");
        }
    }

    private void ValidateBlacklist(string email)
    {
        var domain = email[(email.LastIndexOf('@') + 1)..];
        var blocked = options.Value.BlacklistDomains
            .Any(x => string.Equals(x.Trim().TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase));
        if (blocked)
        {
            throw new AppException(ErrorCodes.BadRequest, "Email domain is blocked");
        }
    }

    private async Task ValidateByApiAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiEndpoint))
        {
            throw new AppException(ErrorCodes.BadRequest, "Email validation API is not configured");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.ApiEndpoint)
        {
            Content = JsonContent.Create(new { email })
        };

        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            var headerName = string.IsNullOrWhiteSpace(options.Value.ApiKeyHeaderName)
                ? "Authorization"
                : options.Value.ApiKeyHeaderName;
            var headerValue = headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? $"Bearer {options.Value.ApiKey}"
                : options.Value.ApiKey;
            request.Headers.TryAddWithoutValidation(headerName, headerValue);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(ErrorCodes.BadRequest, "Email validation API rejected the request");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!IsApiValid(document.RootElement))
        {
            throw new AppException(ErrorCodes.BadRequest, "Email is not deliverable");
        }
    }

    private static bool IsApiValid(JsonElement element)
    {
        foreach (var propertyName in ValidPropertyNames)
        {
            if (TryReadBoolean(element, propertyName, out var value))
            {
                return value;
            }
        }

        if (element.TryGetProperty("result", out var result))
        {
            if (result.ValueKind == JsonValueKind.String)
            {
                return result.GetString() is "valid" or "deliverable" or "ok";
            }

            return IsApiValid(result);
        }

        if (element.TryGetProperty("data", out var data))
        {
            return IsApiValid(data);
        }

        return false;
    }

    private static bool TryReadBoolean(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out value))
        {
            return true;
        }

        return false;
    }
}
