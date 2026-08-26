using System.Security.Cryptography;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace jokester.admin.Application.Services;

public sealed class AiSizeModeRolloutPolicy(
    IHttpContextAccessor httpContextAccessor,
    ICurrentUser currentUser,
    IOptions<AiImageSizeModeOptions> options) : IAiSizeModeRolloutPolicy
{
    private const string Capability = "ai-size-mode-v1";

    public AiImageClientContext GetClientContext()
    {
        var headers = httpContextAccessor.HttpContext?.Request.Headers;
        var capabilities = headers?["X-Client-Capabilities"].ToString() ?? string.Empty;
        var understands = capabilities.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(Capability, StringComparer.OrdinalIgnoreCase);
        var platform = NormalizeHeader(headers?["X-Client-Platform"].ToString(), "unknown");
        var version = NormalizeHeader(headers?["X-Client-Version"].ToString(), string.Empty);
        var build = NormalizeHeader(headers?["X-Client-Build"].ToString(), string.Empty);
        return new AiImageClientContext(currentUser.UserId, understands, platform, version, build);
    }

    public bool CanUseVersionedContract(AiImageClientContext context) =>
        options.Value.Enabled
        && context.UnderstandsSizeModeV1
        && options.Value.AllowedPlatforms.Contains(context.Platform, StringComparer.OrdinalIgnoreCase);

    public bool CanUseAuto(AiImageClientContext context, string modelCode, string catalogVersion)
    {
        var settings = options.Value;
        if (!CanUseVersionedContract(context) || !settings.AutoEnabled || !context.UserId.HasValue)
        {
            return false;
        }
        if (settings.AllowedUserIds.Contains(context.UserId.Value))
        {
            return true;
        }
        var percent = Math.Clamp(settings.AutoCohortPercent, 0, 100);
        if (percent == 0)
        {
            return false;
        }
        var material = $"{context.UserId.Value}:{modelCode}:{catalogVersion}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bucket = BitConverter.ToUInt32(hash, 0) % 100;
        return bucket < percent;
    }

    private static string NormalizeHeader(string? value, string fallback)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.Length is > 0 and <= 100 ? normalized : fallback;
    }
}
