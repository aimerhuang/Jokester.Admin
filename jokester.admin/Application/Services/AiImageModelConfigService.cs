using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class AiImageModelConfigService(ISqlSugarClient db) : IAiImageModelConfigService
{
    public const string DefaultGptModelCode = "gpt-image-2";
    public const string DefaultNanoBananaModelCode = "nano-banana-2";
    public const string NanoBananaProModelCode = "nano-banana-pro";

    // 单次请求可生成的最大图片数量，依据各供应商官方限制。
    // OpenAI Images API（gpt-image）：n 取值 1-10。
    // Nano Banana（Gemini Image）：单次请求最多 4 张。
    public const int DefaultGptMaxImageCount = 10;
    public const int DefaultNanoBananaMaxImageCount = 4;

    private static readonly Dictionary<string, string> ModelAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nano-banana-2"] = DefaultNanoBananaModelCode,
        ["nanao-banana-2"] = DefaultNanoBananaModelCode,
        ["nano-banana-pro"] = NanoBananaProModelCode
    };

    public async Task<IReadOnlyList<AiImageModelOptionDto>> GetEnabledModelsAsync(CancellationToken cancellationToken)
    {
        var configs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return configs
            .GroupBy(x => NormalizeModelCode(x.ModelCode), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var config = group.OrderBy(x => x.Sort).ThenBy(x => x.Id).First();
                return new AiImageModelOptionDto
                {
                    Code = NormalizeModelCode(config.ModelCode),
                    Name = config.ModelName,
                    Provider = config.Provider,
                    Sort = config.Sort
                };
            })
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.Code)
            .ToList();
    }

    public async Task<ResolvedAiImageModelConfig> ResolveAsync(string? modelCode, string? resolutionCode, CancellationToken cancellationToken)
    {
        var normalizedModelCode = NormalizeModelCode(modelCode);
        var normalizedResolutionCode = NormalizeResolutionCode(resolutionCode);

        var configs = await db.Queryable<AiImageModelConfigEntity>()
            .Where(x => !x.IsDeleted && x.Status == 1)
            .OrderBy(x => x.Sort)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        configs = configs
            .Where(x => string.Equals(NormalizeModelCode(x.ModelCode), normalizedModelCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (configs.Count == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, $"AI image model is not enabled: {normalizedModelCode}");
        }

        var config = configs.FirstOrDefault(x => string.Equals(x.ResolutionCode, normalizedResolutionCode, StringComparison.OrdinalIgnoreCase))
            ?? configs.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.ResolutionCode));

        if (config is null)
        {
            throw new AppException(ErrorCodes.BadRequest, $"AI image model {normalizedModelCode} does not support resolution {normalizedResolutionCode}");
        }

        return Validate(config);
    }

    public static string NormalizeModelCode(string? modelCode)
    {
        var normalized = string.IsNullOrWhiteSpace(modelCode)
            ? DefaultGptModelCode
            : modelCode.Trim().ToLowerInvariant();

        return ModelAliases.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }

    public static bool IsNanoBananaModel(string? modelCode)
    {
        var normalized = NormalizeModelCode(modelCode);
        return string.Equals(normalized, DefaultNanoBananaModelCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, NanoBananaProModelCode, StringComparison.OrdinalIgnoreCase);
    }

    // 根据模型返回单次生图请求允许的最大图片数量。
    public static int GetMaxImageCount(string? modelCode)
    {
        return IsNanoBananaModel(modelCode)
            ? DefaultNanoBananaMaxImageCount
            : DefaultGptMaxImageCount;
    }

    private static string NormalizeResolutionCode(string? resolutionCode)
    {
        return string.IsNullOrWhiteSpace(resolutionCode) ? "1k" : resolutionCode.Trim().ToLowerInvariant();
    }

    private static ResolvedAiImageModelConfig Validate(AiImageModelConfigEntity config)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderModel))
        {
            throw new AppException(ErrorCodes.BadRequest, $"AI image model {config.ModelCode} provider model is not configured");
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            throw new AppException(ErrorCodes.BadRequest, $"AI image model {config.ModelCode} base URL is not configured");
        }

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new AppException(ErrorCodes.BadRequest, $"AI image model {config.ModelCode} API key is not configured");
        }

        var textToImagePath = string.IsNullOrWhiteSpace(config.TextToImagePath)
            ? "/images/generations"
            : config.TextToImagePath.Trim();
        var imageToImagePath = string.IsNullOrWhiteSpace(config.ImageToImagePath)
            ? "/images/edits"
            : config.ImageToImagePath.Trim();

        var normalizedModelCode = NormalizeModelCode(config.ModelCode);
        var providerModel = config.ProviderModel.Trim();

        return new ResolvedAiImageModelConfig
        {
            ModelCode = normalizedModelCode,
            ModelName = config.ModelName.Trim(),
            Provider = config.Provider.Trim(),
            ProviderModel = providerModel,
            ResolutionCode = string.IsNullOrWhiteSpace(config.ResolutionCode) ? null : config.ResolutionCode.Trim().ToLowerInvariant(),
            BaseUrl = config.BaseUrl.Trim(),
            ApiKey = config.ApiKey.Trim(),
            TextToImagePath = textToImagePath,
            ImageToImagePath = imageToImagePath
        };
    }
}
