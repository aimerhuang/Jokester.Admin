using jokester.admin.Application.Abstractions;
using jokester.admin.Controllers;
using jokester.admin.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;

namespace jokester.admin.Tests;

public sealed class AiMediaPathResolverTests
{
    [Fact]
    public void UsesConfiguredAbsoluteRoot_IndependentlyOfContentRoot()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var contentRoot = Path.Combine(testRoot, "app");
            var mediaRoot = Path.Combine(testRoot, "persistent-data", "ai");
            Directory.CreateDirectory(contentRoot);

            var resolver = CreateResolver(contentRoot, mediaRoot);

            Assert.Equal(Path.GetFullPath(mediaRoot), resolver.RootPath);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(mediaRoot, "42", "202608", "image.png")),
                resolver.ResolveFilePath("42/202608/image.png"));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void DefaultRoot_FindsRepositoryRootFromNestedContentRoot()
    {
        var testRoot = CreateTestRoot();
        try
        {
            File.WriteAllText(Path.Combine(testRoot, "jokester.slnx"), string.Empty);
            var contentRoot = Path.Combine(testRoot, "jokester.admin");
            Directory.CreateDirectory(contentRoot);

            var resolver = CreateResolver(contentRoot);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(testRoot, "private-media", "ai")),
                resolver.RootPath);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void DefaultRoot_FallsBackToContentRootWithoutRepositoryMarker()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var contentRoot = Path.Combine(testRoot, "standalone-app");
            Directory.CreateDirectory(contentRoot);

            var resolver = CreateResolver(contentRoot);

            Assert.Equal(
                Path.GetFullPath(Path.Combine(contentRoot, "private-media", "ai")),
                resolver.RootPath);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("42/../../outside.png")]
    [InlineData("..\\outside.png")]
    public void ResolveFilePath_RejectsTraversal(string relativePath)
    {
        var testRoot = CreateTestRoot();
        try
        {
            var contentRoot = Path.Combine(testRoot, "app");
            var mediaRoot = Path.Combine(testRoot, "media");
            Directory.CreateDirectory(contentRoot);
            var resolver = CreateResolver(contentRoot, mediaRoot);

            Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath(relativePath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveFilePath_RejectsAbsolutePath()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var contentRoot = Path.Combine(testRoot, "app");
            var mediaRoot = Path.Combine(testRoot, "media");
            Directory.CreateDirectory(contentRoot);
            var resolver = CreateResolver(contentRoot, mediaRoot);
            var absolutePath = Path.GetFullPath(Path.Combine(testRoot, "outside.png"));

            Assert.Throws<InvalidOperationException>(() => resolver.ResolveFilePath(absolutePath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PrivateMediaController_ReadsFromConfiguredRoot()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var contentRoot = Path.Combine(testRoot, "app");
            var mediaRoot = Path.Combine(testRoot, "persistent-data", "ai");
            Directory.CreateDirectory(contentRoot);
            var resolver = CreateResolver(contentRoot, mediaRoot);
            const string relativePath = "42/202608/image.png";
            var filePath = resolver.ResolveFilePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, [1, 2, 3]);

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(42L);
            var controller = new PrivateMediaController(
                currentUser.Object,
                Mock.Of<ISqlSugarClient>(),
                resolver);

            var result = await controller.Download(relativePath, CancellationToken.None);

            var physicalFile = Assert.IsType<PhysicalFileResult>(result);
            Assert.Equal(filePath, physicalFile.FileName);
            Assert.Equal("image/png", physicalFile.ContentType);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void OptionsValidator_RejectsRelativeAndFilesystemRootPaths()
    {
        var validator = new AiMediaStorageOptionsValidator();

        Assert.True(validator.Validate(null, new AiMediaStorageOptions()).Succeeded);
        Assert.True(validator.Validate(null, new AiMediaStorageOptions { RootPath = Path.GetFullPath("media") }).Succeeded);
        Assert.False(validator.Validate(null, new AiMediaStorageOptions { RootPath = "relative/media" }).Succeeded);
        Assert.False(validator.Validate(null, new AiMediaStorageOptions { RootPath = Path.GetPathRoot(Path.GetFullPath("."))! }).Succeeded);
    }

    private static AiMediaPathResolver CreateResolver(string contentRoot, string? configuredRoot = null)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(contentRoot);
        return new AiMediaPathResolver(
            Options.Create(new AiMediaStorageOptions { RootPath = configuredRoot ?? string.Empty }),
            environment.Object,
            NullLogger<AiMediaPathResolver>.Instance);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "jokester-ai-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
