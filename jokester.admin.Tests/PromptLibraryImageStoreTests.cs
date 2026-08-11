using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using jokester.admin.Application.Services;
using jokester.admin.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace jokester.admin.Tests;

public sealed class PromptLibraryImageStoreTests
{
    [Fact]
    public async Task PrepareAsync_PreservesValidatedImageAndReusesContentAddressedFile()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var imageBytes = CreatePng();
            var handler = new StubHttpMessageHandler(_ => CreateImageResponse(imageBytes));
            var store = CreateStore(root, handler);
            var staging = store.CreateStagingDirectory(42);

            var first = await store.PrepareAsync(
                7,
                "https://cms-assets.youmind.com/example.png",
                null,
                staging,
                CancellationToken.None);
            var expectedHash = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant()[..12];

            Assert.False(first.Reused);
            Assert.Equal($"youmind-gpt-image-2/7-{expectedHash}.png", first.RelativePath);
            var storedPath = Path.Combine(root, first.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(imageBytes, await File.ReadAllBytesAsync(storedPath));

            var reused = await store.PrepareAsync(
                7,
                "https://cms-assets.youmind.com/example.png",
                first.RelativePath,
                staging,
                CancellationToken.None);

            Assert.True(reused.Reused);
            Assert.Equal(first.RelativePath, reused.RelativePath);
            Assert.Equal(1, handler.RequestCount);
            Assert.Equal(imageBytes.Length, store.GetStoredBytes());

            store.DeleteStagingDirectory(staging);
            File.SetLastWriteTimeUtc(storedPath, DateTime.UtcNow.AddDays(-10));
            await store.CleanupAsync(
                new HashSet<string>(),
                DateTime.UtcNow.AddDays(-7),
                CancellationToken.None);
            Assert.False(File.Exists(storedPath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task PrepareAsync_RejectsDisallowedHostBeforeSendingRequest()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var handler = new StubHttpMessageHandler(_ => CreateImageResponse(CreatePng()));
            var store = CreateStore(root, handler);
            var staging = store.CreateStagingDirectory(1);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.PrepareAsync(
                1,
                "https://cms-assets.youmind.com.evil.example/cover.png",
                null,
                staging,
                CancellationToken.None));
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task PrepareAsync_ReplacesCorruptedReusableFile()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var expectedBytes = CreatePng(Color.CornflowerBlue);
            var handler = new StubHttpMessageHandler(_ => CreateImageResponse(expectedBytes));
            var store = CreateStore(root, handler);
            var staging = store.CreateStagingDirectory(8);
            var stored = await store.PrepareAsync(
                8,
                "https://cms-assets.youmind.com/example.png",
                null,
                staging,
                CancellationToken.None);
            var storedPath = Path.Combine(root, stored.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            await File.WriteAllBytesAsync(storedPath, CreatePng(Color.HotPink));
            Assert.False(store.IsStoredImageAvailable(stored.RelativePath));

            var repaired = await store.PrepareAsync(
                8,
                "https://cms-assets.youmind.com/example.png",
                stored.RelativePath,
                staging,
                CancellationToken.None);

            Assert.False(repaired.Reused);
            Assert.Equal(stored.RelativePath, repaired.RelativePath);
            Assert.Equal(2, handler.RequestCount);
            Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(storedPath));
            Assert.True(store.IsStoredImageAvailable(stored.RelativePath));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task PrepareAsync_RejectsRedirects()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://evil.example/cover.png") }
            });
            var store = CreateStore(root, handler);
            var staging = store.CreateStagingDirectory(1);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.PrepareAsync(
                1,
                "https://cms-assets.youmind.com/cover.png",
                null,
                staging,
                CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static PromptLibraryImageStore CreateStore(string root, HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new PromptLibraryImageStore(
            client,
            Options.Create(new PromptLibraryOptions
            {
                Enabled = true,
                ImageRoot = root,
                ImageMaxBytes = 1024 * 1024,
                RetryCount = 0
            }),
            NullLogger<PromptLibraryImageStore>.Instance);
    }

    private static HttpResponseMessage CreateImageResponse(byte[] imageBytes)
    {
        var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static byte[] CreatePng(Color? color = null)
    {
        using var image = new Image<Rgba32>(2, 2, color ?? Color.CornflowerBlue);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "jokester-prompt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jokester-prompt-tests"))
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(
                expectedParent,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responder(request));
        }
    }
}
