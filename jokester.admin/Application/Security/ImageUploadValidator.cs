using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;

namespace jokester.admin.Application.Security;

public static class ImageUploadValidator
{
    private const int MaxWidth = 8192;
    private const int MaxHeight = 8192;
    private const long MaxPixels = 16_777_216;
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(10);
    private static readonly SixLabors.ImageSharp.Configuration ImageConfiguration = CreateImageConfiguration();

    public static async Task<ValidatedImage> ValidateAsync(IFormFile file, long maxBytes, CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Image size must be between 1 byte and {maxBytes / 1024 / 1024}MB.");
        }

        await using var source = new MemoryStream((int)file.Length);
        await file.CopyToAsync(source, cancellationToken);
        source.Position = 0;

        using var timeout = new CancellationTokenSource(DecodeTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var decoderOptions = new DecoderOptions
            {
                Configuration = ImageConfiguration,
                SkipMetadata = true,
                MaxFrames = 2
            };
            var imageInfo = await Image.IdentifyAsync(decoderOptions, source, linkedCancellation.Token)
                ?? throw InvalidImage();
            var format = ResolveFormat(imageInfo.Metadata.DecodedImageFormat);
            ValidateDimensions(imageInfo.Width, imageInfo.Height);

            source.Position = 0;
            using var image = await Image.LoadAsync(decoderOptions, source, linkedCancellation.Token);
            if (image.Frames.Count != 1)
            {
                throw new AppException(ErrorCodes.BadRequest, "Animated or multi-frame images are not supported.");
            }

            ValidateDimensions(image.Width, image.Height);
            StripMetadata(image);

            await using var sanitized = new MemoryStream();
            await image.SaveAsync(sanitized, format.Encoder, linkedCancellation.Token);
            if (sanitized.Length <= 0 || sanitized.Length > maxBytes)
            {
                throw new AppException(ErrorCodes.BadRequest, "The sanitized image exceeds the upload size limit.");
            }

            return new ValidatedImage(
                format.MimeType,
                format.Extension,
                sanitized.ToArray(),
                image.Width,
                image.Height);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image decoding timed out.");
        }
        catch (UnknownImageFormatException)
        {
            throw InvalidImage();
        }
        catch (InvalidImageContentException)
        {
            throw InvalidImage();
        }
    }

    public static async Task<ValidatedImage> ValidateAsync(byte[] content, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(content, writable: false);
        var file = new FormFile(stream, 0, content.Length, "image", "provider-image");
        var validated = await ValidateAsync(file, maxBytes, cancellationToken);
        using var image = Image.Load(new DecoderOptions { Configuration = ImageConfiguration }, validated.Content);
        await using var png = new MemoryStream();
        await image.SaveAsync(png, new PngEncoder(), cancellationToken);
        if (png.Length > maxBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, "The sanitized image exceeds the upload size limit.");
        }

        return new ValidatedImage("image/png", ".png", png.ToArray(), image.Width, image.Height);
    }

    private static SupportedImageFormat ResolveFormat(IImageFormat? format)
    {
        if (format == JpegFormat.Instance)
        {
            return new SupportedImageFormat("image/jpeg", ".jpg", new JpegEncoder { Quality = 90 });
        }
        if (format == PngFormat.Instance)
        {
            return new SupportedImageFormat("image/png", ".png", new PngEncoder());
        }
        if (format == WebpFormat.Instance)
        {
            return new SupportedImageFormat("image/webp", ".webp", new WebpEncoder());
        }

        throw new AppException(ErrorCodes.BadRequest, "Only JPEG, PNG, and WebP images are supported.");
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxWidth || height > MaxHeight || (long)width * height > MaxPixels)
        {
            throw new AppException(
                ErrorCodes.BadRequest,
                $"Image dimensions must not exceed {MaxWidth}x{MaxHeight} or {MaxPixels:N0} pixels.");
        }
    }

    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.IccProfile = null;
            frame.Metadata.IptcProfile = null;
            frame.Metadata.XmpProfile = null;
        }
    }

    private static AppException InvalidImage() =>
        new(ErrorCodes.BadRequest, "The file is not a valid supported image.");

    private static SixLabors.ImageSharp.Configuration CreateImageConfiguration()
    {
        var configuration = SixLabors.ImageSharp.Configuration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            MaximumPoolSizeMegabytes = 64,
            AllocationLimitMegabytes = 128
        });
        return configuration;
    }

    private sealed record SupportedImageFormat(string MimeType, string Extension, IImageEncoder Encoder);
}

public sealed record ValidatedImage(string MimeType, string Extension, byte[] Content, int Width, int Height);
