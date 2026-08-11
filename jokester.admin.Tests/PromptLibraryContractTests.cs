using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiImages;
using jokester.admin.Application.DTOs.NanoBananaImages;
using jokester.admin.Application.Models.PromptLibrary;
using jokester.admin.Application.Security;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Controllers;
using jokester.admin.Infrastructure.PromptLibrary;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace jokester.admin.Tests;

public sealed class PromptLibraryContractTests
{
    private const long SourcePromptId = 42;
    private static readonly PromptReadmeParseOptions ParseOptions = new(["cms-assets.youmind.com"]);

    [Fact]
    public void SourceKeys_RemainStable_WhenUnrelatedItemsAreInsertedOrReordered()
    {
        var firstSnapshot = new[]
        {
            CreateParsedItem(1, "URL identity", "prompt-a", "https://source.example/a", 1),
            CreateParsedItem(2, "Prompt identity", "prompt-b", null, 2)
        };
        var changedSnapshot = new[]
        {
            CreateParsedItem(99, "Unrelated", "prompt-c", "https://source.example/c", 1),
            CreateParsedItem(2, "Prompt identity", "prompt-b", null, 2),
            CreateParsedItem(1, "URL identity", "prompt-a", "https://source.example/a", 3)
        };

        var originalKeys = AssignmentsByTitle(firstSnapshot);
        var changedKeys = AssignmentsByTitle(changedSnapshot);

        Assert.Equal(originalKeys["URL identity"].SourceKey, changedKeys["URL identity"].SourceKey);
        Assert.Equal(originalKeys["Prompt identity"].SourceKey, changedKeys["Prompt identity"].SourceKey);
        Assert.All(originalKeys.Values, assignment => Assert.Equal(64, assignment.SourceKey.Length));
    }

    [Fact]
    public void SourceKeys_DistinguishSameUrlVariants_WithoutDependingOnOrderOrDeletion()
    {
        var firstDuplicate = CreateParsedItem(
            7,
            "First duplicate",
            "first version",
            "https://source.example/repeated",
            1);
        var secondDuplicate = CreateParsedItem(
            7,
            "Second duplicate",
            "second version",
            "https://source.example/repeated",
            3);
        var unrelated = CreateParsedItem(8, "Unrelated", "other", "https://source.example/other", 2);

        var assignments = AssignmentsByTitle([firstDuplicate, unrelated, secondDuplicate]);
        var afterReorder = AssignmentsByTitle([secondDuplicate, unrelated, firstDuplicate]);
        var afterDeletion = AssignmentsByTitle([unrelated, secondDuplicate]);

        Assert.Equal(1, assignments["First duplicate"].ExternalOccurrence);
        Assert.Equal(1, assignments["First duplicate"].SourceOccurrence);
        Assert.Equal(2, assignments["Second duplicate"].ExternalOccurrence);
        Assert.Equal(1, assignments["Second duplicate"].SourceOccurrence);
        Assert.NotEqual(assignments["First duplicate"].SourceKey, assignments["Second duplicate"].SourceKey);
        Assert.Equal(assignments["First duplicate"].SourceKey, afterReorder["First duplicate"].SourceKey);
        Assert.Equal(assignments["Second duplicate"].SourceKey, afterReorder["Second duplicate"].SourceKey);
        Assert.Equal(assignments["Second duplicate"].SourceKey, afterDeletion["Second duplicate"].SourceKey);
    }

    [Fact]
    public void SourceKeys_PreserveExistingStableKey_WhenSourceContentIsEdited()
    {
        const string sourceUrl = "https://source.example/stable";
        var original = CreateParsedItem(9, "Original title", "original prompt", sourceUrl, 1);
        var originalAssignment = PromptLibrarySourceKeyFactory.CreateAssignments([original])[0];
        var existing = new ExistingPromptLibrarySourceKey(
            originalAssignment.SourceKey,
            sourceUrl,
            Hash("original prompt"),
            original.Title,
            original.ExternalNo,
            1,
            original.SourcePosition,
            true);
        var edited = CreateParsedItem(9, "Edited title", "edited prompt", sourceUrl, 1);

        var editedAssignment = PromptLibrarySourceKeyFactory.CreateAssignments([edited], [existing])[0];

        Assert.Equal(originalAssignment.SourceKey, editedAssignment.SourceKey);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(4000)]
    public void ReadmeParser_PreservesSupportedPromptTextLengths(int length)
    {
        var prompt = new string('p', length);
        var markdown = $"""
            ### No. 1: Boundary prompt
            #### Description
            Boundary description.
            #### Prompt
            ```
            {prompt}
            ```
            #### Generated Images
            ![cover](https://cms-assets.youmind.com/boundary.jpg)
            """;

        var result = new MarkdigPromptReadmeParser().Parse(markdown, ParseOptions);

        var item = Assert.Single(result.Items);
        Assert.Equal(length, item.PromptText.Length);
        Assert.Equal(prompt, item.PromptText);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2001)]
    [InlineData(4000)]
    public async Task FourSourcePromptApiPaths_PreserveSupportedPromptLengths(int length)
    {
        var prompt = new string('p', length);
        Assert.Equal(prompt, AiImagePromptValidator.Validate(prompt));

        var aiImageService = new Mock<IAiImageService>(MockBehavior.Strict);
        var nanoBananaService = new Mock<INanoBananaImageService>(MockBehavior.Strict);
        var gptGenerate = new GenerateAiImageRequest { SourcePromptId = SourcePromptId, Prompt = prompt };
        var gptCreate = new CreateAiImageTaskRequest { SourcePromptId = SourcePromptId, Prompt = prompt };
        var nanoGenerate = new GenerateNanoBananaImageRequest { SourcePromptId = SourcePromptId, Prompt = prompt };
        var nanoCreate = new CreateNanoBananaImageTaskRequest { SourcePromptId = SourcePromptId, Prompt = prompt };

        aiImageService
            .Setup(x => x.GenerateAsync(gptGenerate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateAiImageResponse { SourcePromptId = SourcePromptId, Prompt = prompt });
        aiImageService
            .Setup(x => x.CreateTasksAsync(gptCreate, It.IsAny<CancellationToken>()))
            .ReturnsAsync([101L]);
        nanoBananaService
            .Setup(x => x.GenerateAsync(nanoGenerate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateNanoBananaImageResponse { SourcePromptId = SourcePromptId, Prompt = prompt });
        nanoBananaService
            .Setup(x => x.CreateAsync(nanoCreate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(102L);

        var controller = new AiImagesController(
            aiImageService.Object,
            nanoBananaService.Object,
            Mock.Of<IAiImageModelConfigService>());

        await controller.Generate(gptGenerate, default);
        await controller.Create(gptCreate, default);
        await controller.GenerateNanoBananaImage(nanoGenerate, default);
        await controller.CreateNanoBananaImage(nanoCreate, default);

        aiImageService.VerifyAll();
        nanoBananaService.VerifyAll();
        Assert.All(
            new[] { gptGenerate.Prompt, gptCreate.Prompt, nanoGenerate.Prompt, nanoCreate.Prompt },
            value => Assert.Equal(length, value.Length));
        Assert.All(
            new[] { gptGenerate.SourcePromptId, gptCreate.SourcePromptId, nanoGenerate.SourcePromptId, nanoCreate.SourcePromptId },
            value => Assert.Equal(SourcePromptId, value));
    }

    [Fact]
    public void PromptValidator_RejectsOnlyBeyondCurrentFourThousandCharacterContract()
    {
        var exception = Assert.Throws<AppException>(() =>
            AiImagePromptValidator.Validate(new string('p', AiImagePromptValidator.MaxLength + 1)));

        Assert.Equal(ErrorCodes.BadRequest, exception.Code);
    }

    private static Dictionary<string, PromptLibrarySourceKeyAssignment> AssignmentsByTitle(
        IReadOnlyList<ParsedPromptReadmeItem> items)
    {
        var assignments = PromptLibrarySourceKeyFactory.CreateAssignments(items);
        return items
            .Select((item, index) => new { item.Title, Assignment = assignments[index] })
            .ToDictionary(x => x.Title, x => x.Assignment, StringComparer.Ordinal);
    }

    private static ParsedPromptReadmeItem CreateParsedItem(
        int externalNo,
        string title,
        string prompt,
        string? sourceUrl,
        int sourcePosition)
    {
        return new ParsedPromptReadmeItem(
            externalNo,
            null,
            title,
            "description",
            prompt,
            "https://cms-assets.youmind.com/cover.jpg",
            null,
            null,
            sourceUrl,
            null,
            "en",
            sourcePosition,
            new PromptReadmeSourceSpan(0, 0, 1, 1));
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
