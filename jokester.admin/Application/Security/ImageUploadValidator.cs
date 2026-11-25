using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using ImageMagick;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using System.Buffers.Binary;

namespace jokester.admin.Application.Security;

public static class ImageUploadValidator
{
    private const int MaxWidth = 8192;
    private const int MaxHeight = 8192;
    private const long MaxPixels = 16_777_216;
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(10);
    private static readonly SixLabors.ImageSharp.Configuration ImageConfiguration = CreateImageConfiguration();

    static ImageUploadValidator()
    {
        ResourceLimits.Width = MaxWidth;
        ResourceLimits.Height = MaxHeight;
        ResourceLimits.Area = 128 * 1024 * 1024;
        ResourceLimits.Memory = 128 * 1024 * 1024;
        ResourceLimits.Disk = 256 * 1024 * 1024;
        ResourceLimits.MaxMemoryRequest = 64 * 1024 * 1024;
        ResourceLimits.MaxProfileSize = 8 * 1024 * 1024;
        ResourceLimits.ListLength = 32;
        ResourceLimits.Thread = 2;
        ResourceLimits.Time = checked((ulong)DecodeTimeout.TotalSeconds);
    }

    public static async Task<ValidatedImage> ValidateAsync(IFormFile file, long maxBytes, CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            throw new AppException(ErrorCodes.BadRequest, $"Image size must be between 1 byte and {maxBytes / 1024 / 1024}MB.");
        }

        await using var source = new MemoryStream((int)file.Length);
        await file.CopyToAsync(source, cancellationToken);
        source.Position = 0;

        if (IsHeifContainer(source.GetBuffer().AsSpan(0, checked((int)source.Length))))
        {
            return await ValidateHeifAsync(source.ToArray(), maxBytes, cancellationToken);
        }

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

    /// <summary>校验供应商生成的动画并保留原始 GIF 字节、帧时长及调色板。</summary>
    public static async Task<ValidatedImage> ValidateGeneratedGifAsync(
        byte[] content, int width, int height, int frameCount, int frameDurationMs, CancellationToken cancellationToken)
    {
        static AppException InvalidGif() => new(ErrorCodes.BadRequest, MachineErrorCodes.ProviderOutputInvalid,
            "生成的 GIF 无法解码，或与请求的尺寸、帧数、帧时长不一致。");

        if (content.Length is < 14 or > 25 * 1024 * 1024
            || !(content.AsSpan(0, 6).SequenceEqual("GIF89a"u8) || content.AsSpan(0, 6).SequenceEqual("GIF87a"u8))
            || content[^1] != 0x3B
            || frameCount is < 2 or > 12 || width is <= 0 or > MaxWidth || height is <= 0 or > MaxHeight
            || (long)width * height * frameCount > 8_294_400
            || BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(6)) != width
            || BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(8)) != height)
        {
            throw InvalidGif();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DecodeTimeout);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var options = new DecoderOptions { Configuration = ImageConfiguration, SkipMetadata = true, MaxFrames = (uint)frameCount + 1 };
            using var image = await Image.LoadAsync(options, stream, timeout.Token);
            if (image.Width != width || image.Height != height || image.Frames.Count != frameCount
                || image.Frames.Any(frame => frame.Metadata.GetGifMetadata().FrameDelay * 10 != frameDurationMs))
            {
                throw InvalidGif();
            }
            return new ValidatedImage("image/gif", ".gif", content, width, height);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw InvalidGif();
        }
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

    private static async Task<ValidatedImage> ValidateHeifAsync(
        byte[] content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(DecodeTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var png = await Task.Run(() =>
            {
                using var image = new MagickImage(content);
                ValidateDimensions(checked((int)image.Width), checked((int)image.Height));
                image.AutoOrient();
                image.Strip();
                image.Format = MagickFormat.Png;
                return image.ToByteArray();
            }, linkedCancellation.Token);

            linkedCancellation.Token.ThrowIfCancellationRequested();
            return await ValidateAsync(png, maxBytes, linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AppException(ErrorCodes.BadRequest, "Image decoding timed out.");
        }
        catch (MagickException)
        {
            throw InvalidImage();
        }
        catch (OverflowException)
        {
            throw InvalidImage();
        }
    }

    private static bool IsHeifContainer(ReadOnlySpan<byte> content)
    {
        if (content.Length < 12 || !content.Slice(4, 4).SequenceEqual("ftyp"u8)) return false;
        var boxSize = (uint)(content[0] << 24 | content[1] << 16 | content[2] << 8 | content[3]);
        var scanLength = Math.Min(content.Length, boxSize is >= 16 and <= 1024 ? checked((int)boxSize) : 32);
        for (var offset = 8; offset + 4 <= scanLength; offset += 4)
        {
            var brand = content.Slice(offset, 4);
            if (brand.SequenceEqual("heic"u8)
                || brand.SequenceEqual("heix"u8)
                || brand.SequenceEqual("hevc"u8)
                || brand.SequenceEqual("hevx"u8)
                || brand.SequenceEqual("mif1"u8)
                || brand.SequenceEqual("msf1"u8))
            {
                return true;
            }
        }
        return false;
    }

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
