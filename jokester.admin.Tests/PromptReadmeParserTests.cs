using jokester.admin.Application.Models.PromptLibrary;
using jokester.admin.Infrastructure.PromptLibrary;

namespace jokester.admin.Tests;

public sealed class PromptReadmeParserTests
{
    private static readonly PromptReadmeParseOptions Options = new(
        ["cms-assets.youmind.com", "marketing-assets.youmind.com"]);

    private readonly MarkdigPromptReadmeParser _parser = new();

    [Fact]
    public void Parse_ExtractsRealReadmeShapeAndOptionalDetails()
    {
        const string markdown = """
            ## Featured Prompts

            ### No. 12: VR Headset Exploded View Poster

            ![Language-EN](https://img.shields.io/badge/Language-EN-blue)

            #### 📖 Description

            Generates a high-tech exploded view diagram with **detailed** component callouts.

            #### 📝 Prompt

            ```json
            {
              "type": "exploded view",
              "subject": "VR headset"
            }
            ```

            #### 🖼️ Generated Images

            ##### Image 1

            <div align="center">
            <img src="https://untrusted.example/ignored.jpg" width="700">
            <IMG width="700" SRC='https://cms-assets.youmind.com/media/cover.jpg?x=1&amp;y=2'>
            <img src="https://marketing-assets.youmind.com/media/second.png">
            </div>

            #### 📌 Details

            - **Author:** [Alice &amp; Bob](https://example.com/alice)
            - **Source:** [Twitter Post](https://x.com/alice/status/42)
            - **Published:** April 19, 2026
            - **Languages:** en
            """;

        var result = _parser.Parse(markdown, Options);

        var item = Assert.Single(result.Items);
        Assert.Empty(result.SkippedItems);
        Assert.Equal(12, item.ExternalNo);
        Assert.Equal("VR Headset Exploded View Poster", item.Title);
        Assert.Equal(
            "Generates a high-tech exploded view diagram with detailed component callouts.",
            item.Description);
        Assert.Equal(
            "{\n  \"type\": \"exploded view\",\n  \"subject\": \"VR headset\"\n}",
            item.PromptText.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal("https://cms-assets.youmind.com/media/cover.jpg?x=1&y=2", item.CoverSourceUrl);
        Assert.Equal("Alice & Bob", item.AuthorName);
        Assert.Equal("https://example.com/alice", item.AuthorUrl);
        Assert.Equal("https://x.com/alice/status/42", item.SourceUrl);
        Assert.Equal("April 19, 2026", item.Published);
        Assert.Equal("en", item.Language);
        Assert.Equal(1, item.SourcePosition);
        Assert.Equal(3, item.SourceSpan.StartLine);
        Assert.True(item.SourceSpan.Start >= 0);
        Assert.True(item.SourceSpan.End > item.SourceSpan.Start);
        Assert.Contains(
            result.Diagnostics,
            x => x.Code == PromptReadmeDiagnosticCodes.DisallowedImageUrl
                && x.Severity == PromptReadmeDiagnosticSeverity.Warning);
    }

    [Fact]
    public void Parse_ValidatesEveryEntryAndSkipsMalformedOnes()
    {
        const string markdown = """
            ### No. 1: First valid prompt
            #### Description
            First description.
            #### Prompt
            ```
            first prompt
            ```
            #### Generated Images
            ![cover](https://cms-assets.youmind.com/first.jpg)

            ### No. 2:
            #### Description
            This entry is deliberately malformed.
            #### Prompt
            This is not a fenced code block.
            #### Generated Images
            ![cover](http://cms-assets.youmind.com/insecure.jpg)

            ### No. 3: Valid prompt after malformed entry
            #### Description
            Later description.
            #### Prompt
            ```text
            later prompt
            ```
            #### Generated Images
            ![cover](https://marketing-assets.youmind.com/later.webp)
            """;

        var result = _parser.Parse(markdown, Options);

        Assert.Equal(3, result.CandidateCount);
        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal(1, first.ExternalNo);
                Assert.Equal(1, first.SourcePosition);
            },
            later =>
            {
                Assert.Equal(3, later.ExternalNo);
                Assert.Equal(3, later.SourcePosition);
            });

        var skipped = Assert.Single(result.SkippedItems);
        Assert.Equal(2, skipped.ExternalNo);
        Assert.Equal(2, skipped.SourcePosition);
        Assert.Contains(skipped.Diagnostics, x => x.Code == PromptReadmeDiagnosticCodes.MissingTitle);
        Assert.Contains(skipped.Diagnostics, x => x.Code == PromptReadmeDiagnosticCodes.MissingPrompt);
        Assert.Contains(skipped.Diagnostics, x => x.Code == PromptReadmeDiagnosticCodes.MissingGeneratedImage);
        Assert.Contains(skipped.Diagnostics, x => x.Code == PromptReadmeDiagnosticCodes.DisallowedImageUrl);
    }

    [Fact]
    public void Parse_SelectsFirstExactAllowedHttpsImageHost()
    {
        const string markdown = """
            ### No. 7: Host validation
            #### Description
            Validates image origins.
            #### Prompt
            ```
            draw a test image
            ```
            #### Generated Images
            ![lookalike](https://cms-assets.youmind.com.evil.example/a.jpg)
            ![credentials](https://user@cms-assets.youmind.com/b.jpg)
            ![port](https://cms-assets.youmind.com:444/c.jpg)
            ![http](http://cms-assets.youmind.com/d.jpg)
            ![allowed](https://marketing-assets.youmind.com/first.webp)
            ![later](https://cms-assets.youmind.com/second.jpg)
            """;

        var result = _parser.Parse(markdown, Options);

        var item = Assert.Single(result.Items);
        Assert.Equal("https://marketing-assets.youmind.com/first.webp", item.CoverSourceUrl);
        Assert.Equal(
            4,
            result.Diagnostics.Count(x => x.Code == PromptReadmeDiagnosticCodes.DisallowedImageUrl));
    }

    [Fact]
    public void Parse_UsesPreambleWhenDescriptionSectionIsEmpty()
    {
        const string markdown = """
            ### No. 98: Product Marketing - Edit Cup Color

            Change only the cup color while preserving the character and background.

            ![Language-EN](https://img.shields.io/badge/Language-EN-blue)

            #### Description

            #### Prompt
            ```
            Change the white cup to green.
            ```
            #### Generated Images
            ![cover](https://cms-assets.youmind.com/edit.jpg)

            ### No. 99: Truly missing description
            #### Prompt
            ```
            Draw an image.
            ```
            #### Generated Images
            ![cover](https://cms-assets.youmind.com/missing.jpg)
            """;

        var result = _parser.Parse(markdown, Options);

        var item = Assert.Single(result.Items);
        Assert.Equal(
            "Change only the cup color while preserving the character and background.",
            item.Description);
        Assert.Contains(
            result.Diagnostics,
            x => x.Code == PromptReadmeDiagnosticCodes.DescriptionFallback
                && x.Severity == PromptReadmeDiagnosticSeverity.Warning);

        var skipped = Assert.Single(result.SkippedItems);
        Assert.Equal(99, skipped.ExternalNo);
        Assert.Contains(skipped.Diagnostics, x => x.Code == PromptReadmeDiagnosticCodes.MissingDescription);
    }

    [Fact]
    public void Parse_RequiresConfiguredBareHostNames()
    {
        var exception = Assert.Throws<ArgumentException>(() => _parser.Parse(
            "# README",
            new PromptReadmeParseOptions(["https://cms-assets.youmind.com/"])));

        Assert.Contains("Invalid allowed image host", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReportsDocumentWithoutPromptEntries()
    {
        var result = _parser.Parse("# README\n\nNo prompt entries here.", Options);

        Assert.Empty(result.Items);
        Assert.Empty(result.SkippedItems);
        Assert.Contains(
            result.Diagnostics,
            x => x.Code == PromptReadmeDiagnosticCodes.InvalidHeading
                && x.Severity == PromptReadmeDiagnosticSeverity.Error);
    }
}
