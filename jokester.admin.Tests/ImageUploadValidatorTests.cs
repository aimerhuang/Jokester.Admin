using System.Text;
using jokester.admin.Application.Security;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace jokester.admin.Tests;

public sealed class ImageUploadValidatorTests
{
    private const long MaxBytes = 10 * 1024 * 1024;

    [Fact]
    public async Task ValidateAsync_DetectsContentAndIgnoresSpoofedNameAndMimeType()
    {
        await using var encoded = new MemoryStream();
        using (var image = new Image<Rgba32>(2, 2))
        {
            await image.SaveAsPngAsync(encoded);
        }

        var result = await ImageUploadValidator.ValidateAsync(
            CreateFile(encoded.ToArray(), "payload.svg", "image/svg+xml"),
            MaxBytes,
            default);

        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(".png", result.Extension);
        using var sanitized = Image.Load(result.Content);
        Assert.Equal(2, sanitized.Width);
        Assert.Equal(2, sanitized.Height);
    }

    [Fact]
    public async Task ValidateAsync_StripsExifMetadataDuringReencode()
    {
        await using var encoded = new MemoryStream();
        using (var image = new Image<Rgba32>(2, 2))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Software, "sensitive-test-marker");
            await image.SaveAsJpegAsync(encoded);
        }

        var result = await ImageUploadValidator.ValidateAsync(encoded.ToArray(), MaxBytes, default);

        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(".png", result.Extension);
        using var sanitized = Image.Load(result.Content);
        Assert.True(sanitized.Metadata.ExifProfile is null || sanitized.Metadata.ExifProfile.Values.Count == 0);
    }

    [Fact]
    public async Task ValidateAsync_RejectsSvgAndOversizedDimensions()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        await Assert.ThrowsAsync<AppException>(() => ImageUploadValidator.ValidateAsync(
            CreateFile(svg, "image.png", "image/png"),
            MaxBytes,
            default));

        await using var encoded = new MemoryStream();
        using (var image = new Image<Rgba32>(8193, 1))
        {
            await image.SaveAsPngAsync(encoded);
        }

        await Assert.ThrowsAsync<AppException>(() => ImageUploadValidator.ValidateAsync(
            CreateFile(encoded.ToArray(), "wide.png", "image/png"),
            MaxBytes,
            default));
    }

    private static FormFile CreateFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
