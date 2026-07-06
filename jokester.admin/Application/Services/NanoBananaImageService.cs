using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class NanoBananaImageService(
    HttpClient httpClient,
    IAiImageModelConfigService modelConfigService,
    IPointService pointService,
    ISqlSugarClient db,
    ICurrentUser currentUser,
    IAiImageTaskQueue taskQueue,
    IWebHostEnvironment environment) : INanoBananaImageService
{
    private const string MimeType = "image/png";
    private const int MaxInputImageCount = 6;
    private const long MaxInputImageSizeBytes = 10 * 1024 * 1024;
    private const int NanoBanana1KLongSide = 1024;
    private const int NanoBanana2KLongSide = 2048;
    private const int NanoBanana4KLongSide = 4096;
    private const string AutoSize = "auto";
    private const string AiImageSiteCode = "ai_image";

    public async Task<GenerateNanoBananaImageResponse> GenerateAsync(GenerateNanoBananaImageRequest request, CancellationToken cancellationToken)
    {
        var imageUrls = ValidateImageUrls(request.ImageUrls);
        var parameters = ResolveNanoBananaParameters(request.ResolutionCode, ResolveAspectRatioInput(request.AspectRatioCode, request.AspectRatios), request.Size, imageUrls);
        var imageCount = ValidateImageCount(request.ImageCount);
        var modelCode = ResolveRequestModelCode(request.ModelCode, request.ModelName, AiImageModelConfigService.DefaultNanoBananaModelCode);
        var modelConfig = await modelConfigService.ResolveAsync(modelCode, null, cancellationToken);
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var pointCost = await GetNanoBananaImageCostAsync(modelConfig.ModelCode, parameters, imageCount, cancellationToken);

        await pointService.ConsumeForImageAsync(userId, 0, modelConfig.ModelCode, pointCost.ResolutionCode, string.Empty, pointCost.Points, cancellationToken);
        try
        {
            GenerateNanoBananaImageResponse? firstResponse = null;
            var urls = new List<string>(imageCount);
            for (var i = 0; i < imageCount; i++)
            {
                var response = await GenerateCoreAsync(request, cancellationToken);
                firstResponse ??= response;
                urls.Add(response.Url);
            }

            return BuildMultiImageResponse(firstResponse!, urls);
        }
        catch
        {
            await pointService.RefundForImageAsync(userId, 0, pointCost.Points, CancellationToken.None);
            throw;
        }
    }

    private static GenerateNanoBananaImageResponse BuildMultiImageResponse(GenerateNanoBananaImageResponse source, IReadOnlyList<string> urls)
    {
        return new GenerateNanoBananaImageResponse
        {
            ModelName = source.ModelName,
            ModelCode = source.ModelCode,
            ProviderModel = source.ProviderModel,
            Prompt = source.Prompt,
            Size = source.Size,
            Quality = source.Quality,
            MimeType = source.MimeType,
            Url = source.Url,
            Urls = urls,
            Base64 = source.Base64,
            DataUrl = source.DataUrl,
            IsImageToImage = source.IsImageToImage,
            ImageUrls = source.ImageUrls,
            RevisedPrompt = source.RevisedPrompt
        };
    }

    private async Task<GenerateNanoBananaImageResponse> GenerateCoreAsync(GenerateNanoBananaImageRequest request, CancellationToken cancellationToken)
    {
        var imageUrls = ValidateImageUrls(request.ImageUrls);
        var normalizedPrompt = ValidatePrompt(request.Prompt, imageUrls.Count > 0);
        var parameters = ResolveNanoBananaParameters(request.ResolutionCode, ResolveAspectRatioInput(request.AspectRatioCode, request.AspectRatios), request.Size, imageUrls);
        var modelCode = ResolveRequestModelCode(request.ModelCode, request.ModelName, AiImageModelConfigService.DefaultNanoBananaModelCode);
        var modelConfig = await modelConfigService.ResolveAsync(modelCode, null, cancellationToken);

        httpClient.DefaultRequestHeaders.Authorization = null;

        using var httpRequest = imageUrls.Count == 0
            ? BuildTextToImageRequest(modelConfig, normalizedPrompt, parameters.AspectRatioCode)
            : BuildImageToImageRequest(modelConfig, normalizedPrompt, parameters.AspectRatioCode, imageUrls);
        httpRequest.Headers.Remove("Authorization");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", modelConfig.ApiKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Nano Banana2 image generation failed: {ReadProviderError(document.RootElement)}");
        }

        var image = ReadFirstImage(document.RootElement);
        var base64 = image.Base64;
        var url = await SaveImageAsync(base64, cancellationToken);
        return new GenerateNanoBananaImageResponse
        {
            ModelName = modelConfig.ModelName,
            ModelCode = modelConfig.ModelCode,
            ProviderModel = modelConfig.ProviderModel,
            Prompt = normalizedPrompt,
            Size = parameters.Size,
            Quality = string.Empty,
            MimeType = image.MimeType,
            Url = url,
            Urls = [url],
            Base64 = base64,
            DataUrl = $"data:{image.MimeType};base64,{base64}",
            IsImageToImage = imageUrls.Count > 0,
            ImageUrls = imageUrls,
            RevisedPrompt = null
        };
    }

    public async Task<long> CreateAsync(CreateNanoBananaImageTaskRequest request, CancellationToken cancellationToken)
    {
        var imageCount = ValidateImageCount(request.ImageCount);
        var imageUrls = ValidateImageUrls(request.ImageUrls);
        var parameters = ResolveNanoBananaParameters(request.ResolutionCode, ResolveAspectRatioInput(request.AspectRatioCode, request.AspectRatios), request.Size, imageUrls);
        var normalizedPrompt = ValidatePrompt(request.Prompt, imageUrls.Count > 0);
        var modelCode = ResolveRequestModelCode(request.ModelCode, request.ModelName, AiImageModelConfigService.DefaultNanoBananaModelCode);
        var modelConfig = await modelConfigService.ResolveAsync(modelCode, null, cancellationToken);
        var userId = currentUser.UserId ?? throw new AppException(ErrorCodes.Unauthorized, "User is not authenticated");
        var siteId = await ResolveAiImageSiteIdAsync(cancellationToken);
        var pointCost = await GetNanoBananaImageCostAsync(modelConfig.ModelCode, parameters, imageCount, cancellationToken);

        var entity = new AiImageTaskEntity
        {
            SiteId = siteId,
            UserId = userId,
            Prompt = normalizedPrompt,
            ModelName = modelConfig.ModelCode,
            ImageCount = imageCount,
            ResolutionCode = parameters.ResolutionCode,
            QualityCode = string.Empty,
            AspectRatioCode = parameters.AspectRatioCode,
            Width = parameters.Width,
            Height = parameters.Height,
            Size = parameters.Size,
            Quality = string.Empty,
            ReferenceImageUrls = imageUrls.Count == 0 ? null : JsonSerializer.Serialize(imageUrls),
            Status = 0,
            CreatedAt = HongKongNow(),
            IsDeleted = false
        };

        entity.Id = await db.Insertable(entity).ExecuteReturnBigIdentityAsync();
        await pointService.ConsumeForImageAsync(userId, entity.Id, modelConfig.ModelCode, pointCost.ResolutionCode, string.Empty, pointCost.Points, cancellationToken);
        if (!taskQueue.TryQueue(entity.Id))
        {
            await db.Updateable<AiImageTaskEntity>()
                .SetColumns(x => new AiImageTaskEntity
                {
                    Status = 2,
                    ErrorMessage = "Nano Banana2 image task queue is full",
                    UpdatedAt = HongKongNow()
                })
                .Where(x => x.Id == entity.Id)
                .ExecuteCommandAsync();
            throw new AppException(ErrorCodes.ServerError, "Nano Banana2 image task queue is full");
        }

        return entity.Id;
    }

    public Task<GenerateNanoBananaImageResponse> GenerateFromTaskAsync(string prompt, string? modelCode, string resolutionCode, string aspectRatioCode, string size, IReadOnlyList<string> imageUrls, CancellationToken cancellationToken)
    {
        return GenerateCoreAsync(new GenerateNanoBananaImageRequest
        {
            Prompt = prompt,
            ModelCode = modelCode ?? string.Empty,
            ResolutionCode = resolutionCode,
            AspectRatioCode = aspectRatioCode,
            Size = size,
            ImageUrls = imageUrls
        }, cancellationToken);
    }

    private static DateTime HongKongNow()
    {
        return DateTime.UtcNow.AddHours(8);
    }

    private async Task<NanoBananaImageCost> GetNanoBananaImageCostAsync(string modelCode, NanoBananaParameters parameters, int imageCount, CancellationToken cancellationToken)
    {
        // 积分价格仅按业务分辨率档位（1k/2k/4k）匹配，与 aspectRatio/size 无关。
        // aspectRatioCode=auto 时上游 size 会传 auto，但绝不能拿 size（auto 或 1024x576 这类尺寸串）去查价格表。
        var points = await pointService.GetImageGenerateCostAsync(modelCode, parameters.ResolutionCode, string.Empty, imageCount, cancellationToken);
        return new NanoBananaImageCost(points, parameters.ResolutionCode);
    }

    private static string ResolveAspectRatioInput(string? aspectRatioCode, string? aspectRatios)
    {
        return string.IsNullOrWhiteSpace(aspectRatioCode) ? aspectRatios ?? string.Empty : aspectRatioCode;
    }

    private NanoBananaParameters ResolveNanoBananaParameters(string? resolutionCode, string? aspectRatioCode, string? size, IReadOnlyList<string>? imageUrls = null)
    {
        if (IsAuto(size))
        {
            var normalizedResolutionCode = string.IsNullOrWhiteSpace(resolutionCode)
                ? "1k"
                : NormalizeResolutionCode(resolutionCode);
            return new NanoBananaParameters(
                normalizedResolutionCode,
                AutoSize,
                0,
                0,
                AutoSize);
        }

        if (!string.IsNullOrWhiteSpace(resolutionCode))
        {
            var normalizedResolutionCode = NormalizeResolutionCode(resolutionCode);
            var normalizedAspectRatioCode = NormalizeAspectRatioCode(aspectRatioCode);
            if (IsAuto(normalizedAspectRatioCode))
            {
                return new NanoBananaParameters(
                    normalizedResolutionCode,
                    AutoSize,
                    0,
                    0,
                    AutoSize);
            }

            var (ratioWidth, ratioHeight) = ParseAspectRatio(normalizedAspectRatioCode);
            var longSide = ResolveLongSide(normalizedResolutionCode);
            var (width, height) = CalculateSize(longSide, ratioWidth, ratioHeight);

            return new NanoBananaParameters(
                normalizedResolutionCode,
                normalizedAspectRatioCode,
                width,
                height,
                $"{width}x{height}");
        }

        if (TryParseSize(size, out var parsedWidth, out var parsedHeight))
        {
            var cappedWidth = Math.Min(parsedWidth, NanoBanana4KLongSide);
            var cappedHeight = Math.Min(parsedHeight, NanoBanana4KLongSide);
            var inferredResolutionCode = InferResolutionCode(Math.Max(cappedWidth, cappedHeight));
            var inferredAspectRatioCode = ResolveAspectRatioCode(cappedWidth, cappedHeight);

            return new NanoBananaParameters(
                inferredResolutionCode,
                inferredAspectRatioCode,
                cappedWidth,
                cappedHeight,
                $"{cappedWidth}x{cappedHeight}");
        }

        return ResolveNanoBananaParameters("1k", "1:1", null, imageUrls);
    }

    private static string NormalizeResolutionCode(string resolutionCode)
    {
        var normalized = resolutionCode.Trim().ToLowerInvariant();
        return normalized is "1k" or "2k" or "4k"
            ? normalized
            : throw new AppException(ErrorCodes.BadRequest, "Unsupported Nano Banana image resolution");
    }

    private static int ResolveLongSide(string resolutionCode)
    {
        return resolutionCode switch
        {
            "1k" => NanoBanana1KLongSide,
            "2k" => NanoBanana2KLongSide,
            "4k" => NanoBanana4KLongSide,
            _ => throw new AppException(ErrorCodes.BadRequest, "Unsupported Nano Banana image resolution")
        };
    }

    private static string NormalizeAspectRatioCode(string? aspectRatioCode)
    {
        if (string.IsNullOrWhiteSpace(aspectRatioCode))
        {
            return "1:1";
        }

        return aspectRatioCode.Trim().ToLowerInvariant();
    }

    private static bool IsAuto(string? value)
    {
        return string.Equals(value?.Trim(), AutoSize, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Width, int Height) ParseAspectRatio(string aspectRatioCode)
    {
        var parts = aspectRatioCode.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var width)
            || !int.TryParse(parts[1], out var height)
            || width <= 0
            || height <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid Nano Banana image aspect ratio");
        }

        return (width, height);
    }

    private static (int Width, int Height) CalculateSize(int longSide, int ratioWidth, int ratioHeight)
    {
        if (ratioWidth >= ratioHeight)
        {
            var height = Math.Max(1, (int)Math.Round((double)longSide * ratioHeight / ratioWidth, MidpointRounding.AwayFromZero));
            return (longSide, height);
        }

        var width = Math.Max(1, (int)Math.Round((double)longSide * ratioWidth / ratioHeight, MidpointRounding.AwayFromZero));
        return (width, longSide);
    }

    private static bool TryParseSize(string? size, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(size))
        {
            return false;
        }

        var parts = size.Trim().Split('x', 'X');
        return parts.Length == 2
            && int.TryParse(parts[0], out width)
            && int.TryParse(parts[1], out height)
            && width > 0
            && height > 0;
    }

    private static string InferResolutionCode(int longSide)
    {
        if (longSide <= NanoBanana1KLongSide)
        {
            return "1k";
        }

        return longSide <= NanoBanana2KLongSide ? "2k" : "4k";
    }

    private readonly record struct NanoBananaParameters(string ResolutionCode, string AspectRatioCode, int Width, int Height, string Size);

    private readonly record struct NanoBananaImageCost(int Points, string ResolutionCode);

    private async Task<long> ResolveAiImageSiteIdAsync(CancellationToken cancellationToken)
    {
        var siteId = await db.Queryable<SysSiteEntity>()
            .Where(x => x.SiteCode == AiImageSiteCode && !x.IsDeleted)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);
        if (siteId <= 0)
        {
            throw new NotFoundException($"站点不存在: {AiImageSiteCode}");
        }

        return siteId;
    }

    private static int ValidateImageCount(int imageCount)
    {
        const int maxImageCount = AiImageModelConfigService.DefaultNanoBananaMaxImageCount;
        if (imageCount < 1 || imageCount > maxImageCount)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Image count must be between 1 and {maxImageCount}");
        }

        return imageCount;
    }

    private static string ResolveAspectRatioCode(int width, int height)
    {
        var divisor = GreatestCommonDivisor(width, height);
        return $"{width / divisor}:{height / divisor}";
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            var temp = right;
            right = left % right;
            left = temp;
        }

        return Math.Abs(left);
    }

    private static string ValidatePrompt(string prompt, bool allowEmptyForImageToImage = false)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            if (allowEmptyForImageToImage)
            {
                return string.Empty;
            }

            throw new AppException(ErrorCodes.BadRequest, "Prompt is required");
        }

        var trimmed = prompt.Trim();
        if (trimmed.Length > 4000)
        {
            throw new AppException(ErrorCodes.BadRequest, "Prompt is too long");
        }

        return trimmed;
    }

    private static string ResolveRequestModelCode(string? modelCode, string? modelName, string defaultModelCode)
    {
        if (!string.IsNullOrWhiteSpace(modelCode))
        {
            return AiImageModelConfigService.NormalizeModelCode(modelCode);
        }

        return string.IsNullOrWhiteSpace(modelName)
            ? defaultModelCode
            : AiImageModelConfigService.NormalizeModelCode(modelName);
    }

    private static HttpRequestMessage BuildTextToImageRequest(ResolvedAiImageModelConfig config, string prompt, string aspectRatioCode)
    {
        return new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(config.BaseUrl, config.TextToImagePath, config.ProviderModel))
        {
            Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new { parts = new object[] { new { text = prompt } } }
                },
                generationConfig = BuildGenerationConfig(aspectRatioCode)
            })
        };
    }

    private HttpRequestMessage BuildImageToImageRequest(ResolvedAiImageModelConfig config, string prompt, string aspectRatioCode, IReadOnlyList<string> imageUrls)
    {
        var parts = new List<object>(imageUrls.Count + 1);
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            parts.Add(new { text = prompt });
        }

        foreach (var imageUrl in imageUrls)
        {
            var file = ResolveInputImageFile(imageUrl);
            var data = Convert.ToBase64String(File.ReadAllBytes(file.Path));
            parts.Add(new
            {
                inlineData = new { mimeType = file.MimeType, data }
            });
        }

        return new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(config.BaseUrl, config.ImageToImagePath, config.ProviderModel))
        {
            Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new { parts = parts.ToArray() }
                },
                generationConfig = BuildGenerationConfig(aspectRatioCode)
            })
        };
    }

    // Gemini 图片模型要求 responseModalities 同时包含 TEXT 与 IMAGE，仅传 IMAGE 会请求失败。
    private static object BuildGenerationConfig(string aspectRatioCode)
    {
        var responseModalities = new[] { "TEXT", "IMAGE" };
        if (IsAuto(aspectRatioCode) || !aspectRatioCode.Contains(':'))
        {
            return new { responseModalities };
        }

        return new
        {
            responseModalities,
            imageConfig = new { aspectRatio = aspectRatioCode }
        };
    }

    private static Uri BuildEndpoint(string baseUrl, string path, string providerModel)
    {
        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        normalizedPath = normalizedPath.StartsWith('/') ? normalizedPath : $"/{normalizedPath}";
        // Gemini 端点形如 /v1beta/models/{model}:generateContent，需要把 {model} 占位符替换成真实模型 ID。
        normalizedPath = normalizedPath.Replace("{model}", providerModel, StringComparison.OrdinalIgnoreCase);
        return new Uri($"{normalizedBaseUrl}{normalizedPath}", UriKind.Absolute);
    }

    private async Task<string> SaveImageAsync(string base64, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new AppException(ErrorCodes.BadRequest, "Nano Banana2 image generation returned invalid base64 image data");
        }

        var storageKey = $"nano-banana2-images/{DateTime.UtcNow:yyyyMM}/{Guid.NewGuid():N}.png";
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var savePath = Path.Combine(webRootPath, storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await File.WriteAllBytesAsync(savePath, bytes, cancellationToken);

        return $"/{storageKey.Replace('\\', '/')}";
    }

    private ReferenceImageFile ResolveInputImageFile(string imageUrl)
    {
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var webRootFullPath = Path.GetFullPath(webRootPath);
        var relativeUrl = imageUrl.Split('?', '#')[0].TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(webRootFullPath, relativeUrl.Replace('/', Path.DirectorySeparatorChar)));

        if (!fullPath.StartsWith(webRootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image URL");
        }

        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new AppException(ErrorCodes.BadRequest, "Input image does not exist");
        }

        if (fileInfo.Length is <= 0 or > MaxInputImageSizeBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "Input image size must be between 1 byte and 10MB");
        }

        var mimeType = GetImageMimeType(fileInfo.Extension);
        return new ReferenceImageFile(fullPath, fileInfo.Name, mimeType);
    }

    private static string GetImageMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => throw new AppException(ErrorCodes.BadRequest, "Unsupported input image type")
        };
    }

    private static (int Width, int Height) ReadImageDimensions(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => ReadJpegDimensions(stream),
                ".png" => ReadPngDimensions(stream),
                ".webp" => ReadWebpDimensions(stream),
                _ => throw new AppException(ErrorCodes.BadRequest, "Unsupported input image type")
            };
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppException(ErrorCodes.BadRequest, "Input image cannot be read");
        }
    }

    private static (int Width, int Height) ReadPngDimensions(Stream stream)
    {
        Span<byte> header = stackalloc byte[24];
        ReadExactly(stream, header);
        if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        return (ReadBigEndianInt32(header[16..20]), ReadBigEndianInt32(header[20..24]));
    }

    private static (int Width, int Height) ReadJpegDimensions(Stream stream)
    {
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        while (stream.Position < stream.Length)
        {
            var markerPrefix = stream.ReadByte();
            if (markerPrefix < 0)
            {
                break;
            }

            if (markerPrefix != 0xFF)
            {
                continue;
            }

            int marker;
            do
            {
                marker = stream.ReadByte();
            }
            while (marker == 0xFF);

            if (marker < 0 || marker == 0xD9 || marker == 0xDA)
            {
                break;
            }

            var segmentLength = ReadBigEndianUInt16(stream);
            if (segmentLength < 2)
            {
                throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
            }

            if (IsJpegStartOfFrame(marker))
            {
                _ = stream.ReadByte();
                var height = ReadBigEndianUInt16(stream);
                var width = ReadBigEndianUInt16(stream);
                if (width <= 0 || height <= 0)
                {
                    throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
                }

                return (width, height);
            }

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }

        throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
    }

    private static (int Width, int Height) ReadWebpDimensions(Stream stream)
    {
        Span<byte> header = stackalloc byte[30];
        ReadExactly(stream, header);
        if (header[0] != 0x52 || header[1] != 0x49 || header[2] != 0x46 || header[3] != 0x46
            || header[8] != 0x57 || header[9] != 0x45 || header[10] != 0x42 || header[11] != 0x50)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        var chunk = $"{(char)header[12]}{(char)header[13]}{(char)header[14]}{(char)header[15]}";
        return chunk switch
        {
            "VP8X" => (1 + ReadLittleEndian24(header[24..27]), 1 + ReadLittleEndian24(header[27..30])),
            "VP8L" => ReadWebpLosslessDimensions(header),
            "VP8 " => ReadWebpLossyDimensions(header),
            _ => throw new AppException(ErrorCodes.BadRequest, "Invalid input image data")
        };
    }

    private static (int Width, int Height) ReadWebpLosslessDimensions(ReadOnlySpan<byte> header)
    {
        if (header[20] != 0x2F)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        var bits = header[21] | (header[22] << 8) | (header[23] << 16) | (header[24] << 24);
        var width = (bits & 0x3FFF) + 1;
        var height = ((bits >> 14) & 0x3FFF) + 1;
        return (width, height);
    }

    private static (int Width, int Height) ReadWebpLossyDimensions(ReadOnlySpan<byte> header)
    {
        if (header[23] != 0x9D || header[24] != 0x01 || header[25] != 0x2A)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        var width = (header[26] | (header[27] << 8)) & 0x3FFF;
        var height = (header[28] | (header[29] << 8)) & 0x3FFF;
        return (width, height);
    }

    private static bool IsJpegStartOfFrame(int marker)
    {
        return marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
    }

    private static int ReadBigEndianUInt16(Stream stream)
    {
        var high = stream.ReadByte();
        var low = stream.ReadByte();
        if (high < 0 || low < 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        return (high << 8) | low;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
    {
        var value = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        if (value <= 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        return value;
    }

    private static int ReadLittleEndian24(ReadOnlySpan<byte> bytes)
    {
        var value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
        if (value < 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }

        return value;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        if (stream.Read(buffer) != buffer.Length)
        {
            throw new AppException(ErrorCodes.BadRequest, "Invalid input image data");
        }
    }

    private static IReadOnlyList<string> ValidateImageUrls(IReadOnlyList<string>? imageUrls)
    {
        if (imageUrls is null || imageUrls.Count == 0)
        {
            return [];
        }

        if (imageUrls.Count > MaxInputImageCount)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Input images must not exceed {MaxInputImageCount}");
        }

        var normalized = new List<string>(imageUrls.Count);
        foreach (var imageUrl in imageUrls)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                throw new AppException(ErrorCodes.BadRequest, "Input image URL is required");
            }

            var trimmed = imageUrl.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Relative, out var uri) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                throw new AppException(ErrorCodes.BadRequest, "Input image URL must be an internal URL");
            }

            normalized.Add(uri.OriginalString);
        }

        return normalized;
    }

    // 解析 Gemini generateContent 响应：图片以内联 base64 存放在 candidates[].content.parts[].inlineData。
    private static (string Base64, string MimeType) ReadFirstImage(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw new AppException(ErrorCodes.BadRequest, "Nano Banana2 image generation returned no image data");
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                // 同时兼容 camelCase(inlineData) 与 snake_case(inline_data) 两种返回风格。
                if (!part.TryGetProperty("inlineData", out var inlineData)
                    && !part.TryGetProperty("inline_data", out inlineData))
                {
                    continue;
                }

                var base64 = TryGetString(inlineData, "data");
                if (string.IsNullOrWhiteSpace(base64))
                {
                    continue;
                }

                var mimeType = TryGetString(inlineData, "mimeType")
                    ?? TryGetString(inlineData, "mime_type")
                    ?? MimeType;
                return (base64, mimeType);
            }
        }

        throw new AppException(ErrorCodes.BadRequest, "Nano Banana2 image generation response did not contain base64 image data");
    }

    private static string ReadProviderError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            if (TryGetString(error, "message") is { } message)
            {
                return message;
            }

            return error.ToString();
        }

        return root.ToString();
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record ReferenceImageFile(string Path, string FileName, string MimeType);
}
