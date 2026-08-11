using System.Globalization;
using System.Net;
using System.Text;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Models.PromptLibrary;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.WebUtilities;

namespace jokester.admin.Infrastructure.PromptLibrary;

public sealed class MarkdigPromptReadmeParser : IPromptReadmeParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .Build();

    public PromptReadmeParseResult Parse(string markdown, PromptReadmeParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);

        var allowedHosts = NormalizeAllowedHosts(options.AllowedImageHosts);
        var document = Markdown.Parse(markdown, Pipeline);
        var blocks = document.ToList();
        var items = new List<ParsedPromptReadmeItem>();
        var skippedItems = new List<SkippedPromptReadmeItem>();
        var diagnostics = new List<PromptReadmeDiagnostic>();
        var sourcePosition = 0;

        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index] is not HeadingBlock { Level: 3 } heading)
            {
                continue;
            }

            var headingText = GetInlineText(heading.Inline).Trim();
            if (!LooksLikePromptHeading(headingText))
            {
                continue;
            }

            sourcePosition++;
            var endIndex = FindEntryEnd(blocks, index + 1);
            var endBlock = endIndex > index + 1 ? blocks[endIndex - 1] : heading;
            var entrySpan = ToSourceSpan(heading, endBlock);
            var entryDiagnostics = new List<PromptReadmeDiagnostic>();
            var parsedHeading = ParseHeading(headingText);

            if (parsedHeading.ErrorCode is not null)
            {
                AddDiagnostic(
                    entryDiagnostics,
                    parsedHeading.ErrorCode,
                    parsedHeading.ErrorMessage!,
                    PromptReadmeDiagnosticSeverity.Error,
                    sourcePosition,
                    ToSourceSpan(heading));
            }

            var sections = FindSections(blocks, index + 1, endIndex, sourcePosition, entryDiagnostics);
            var stableId = ExtractStableId(blocks, index + 1, endIndex);
            var description = ExtractDescription(GetFirstSection(sections, SectionKind.Description), blocks);
            if (string.IsNullOrWhiteSpace(description))
            {
                description = ExtractPreambleDescription(blocks, index + 1, endIndex);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    AddDiagnostic(
                        entryDiagnostics,
                        PromptReadmeDiagnosticCodes.DescriptionFallback,
                        "The Description section was empty; text before the first subsection was used instead.",
                        PromptReadmeDiagnosticSeverity.Warning,
                        sourcePosition,
                        entrySpan);
                }
            }

            var prompt = ExtractPrompt(GetFirstSection(sections, SectionKind.Prompt), blocks);
            var generatedImageSection = GetFirstSection(sections, SectionKind.GeneratedImages);
            var coverSourceUrl = ExtractCoverSourceUrl(
                generatedImageSection,
                blocks,
                allowedHosts,
                sourcePosition,
                entryDiagnostics);
            var details = ExtractDetails(
                GetFirstSection(sections, SectionKind.Details),
                blocks,
                sourcePosition,
                entryDiagnostics);

            if (string.IsNullOrWhiteSpace(description))
            {
                AddDiagnostic(
                    entryDiagnostics,
                    PromptReadmeDiagnosticCodes.MissingDescription,
                    "The prompt entry does not contain a non-empty Description section.",
                    PromptReadmeDiagnosticSeverity.Error,
                    sourcePosition,
                    entrySpan);
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                AddDiagnostic(
                    entryDiagnostics,
                    PromptReadmeDiagnosticCodes.MissingPrompt,
                    "The Prompt section does not contain a non-empty fenced code block.",
                    PromptReadmeDiagnosticSeverity.Error,
                    sourcePosition,
                    entrySpan);
            }

            if (coverSourceUrl is null)
            {
                AddDiagnostic(
                    entryDiagnostics,
                    PromptReadmeDiagnosticCodes.MissingGeneratedImage,
                    "The Generated Images section does not contain an allowed HTTPS image URL.",
                    PromptReadmeDiagnosticSeverity.Error,
                    sourcePosition,
                    entrySpan);
            }

            diagnostics.AddRange(entryDiagnostics);
            if (entryDiagnostics.Any(x => x.Severity == PromptReadmeDiagnosticSeverity.Error))
            {
                skippedItems.Add(new SkippedPromptReadmeItem(
                    sourcePosition,
                    parsedHeading.ExternalNo,
                    headingText,
                    entrySpan,
                    entryDiagnostics));
            }
            else
            {
                items.Add(new ParsedPromptReadmeItem(
                    parsedHeading.ExternalNo!.Value,
                    stableId,
                    parsedHeading.Title!,
                    description!,
                    prompt!,
                    coverSourceUrl!,
                    details.AuthorName,
                    details.AuthorUrl,
                    details.SourceUrl,
                    details.Published,
                    details.Language,
                    sourcePosition,
                    entrySpan));
            }

            index = endIndex - 1;
        }

        if (sourcePosition == 0)
        {
            diagnostics.Add(new PromptReadmeDiagnostic(
                PromptReadmeDiagnosticCodes.InvalidHeading,
                "The document does not contain any level-three 'No. N: title' prompt headings.",
                PromptReadmeDiagnosticSeverity.Error,
                null,
                null));
        }

        return new PromptReadmeParseResult(items, skippedItems, diagnostics);
    }

    private static HashSet<string> NormalizeAllowedHosts(IReadOnlyCollection<string>? hosts)
    {
        if (hosts is null || hosts.Count == 0)
        {
            throw new ArgumentException("At least one allowed image host is required.", nameof(hosts));
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var idn = new IdnMapping();
        foreach (var configuredHost in hosts)
        {
            var host = configuredHost?.Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                throw new ArgumentException($"Invalid allowed image host: '{configuredHost}'.", nameof(hosts));
            }

            normalized.Add(idn.GetAscii(host));
        }

        return normalized;
    }

    private static int FindEntryEnd(IReadOnlyList<Block> blocks, int startIndex)
    {
        for (var index = startIndex; index < blocks.Count; index++)
        {
            if (blocks[index] is HeadingBlock { Level: <= 3 })
            {
                return index;
            }
        }

        return blocks.Count;
    }

    private static bool LooksLikePromptHeading(string heading)
    {
        return heading.StartsWith("No.", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedHeading ParseHeading(string heading)
    {
        var cursor = 3;
        SkipWhitespace(heading, ref cursor);
        var numberStart = cursor;
        while (cursor < heading.Length && char.IsAsciiDigit(heading[cursor]))
        {
            cursor++;
        }

        int? externalNo = null;
        if (numberStart < cursor
            && int.TryParse(
                heading.AsSpan(numberStart, cursor - numberStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedNo)
            && parsedNo > 0)
        {
            externalNo = parsedNo;
        }

        if (externalNo is null)
        {
            return new ParsedHeading(
                null,
                null,
                PromptReadmeDiagnosticCodes.InvalidHeading,
                "The prompt heading must contain a positive integer after 'No.'.");
        }

        SkipWhitespace(heading, ref cursor);
        if (cursor >= heading.Length || heading[cursor] != ':')
        {
            return new ParsedHeading(
                externalNo,
                null,
                PromptReadmeDiagnosticCodes.InvalidHeading,
                "The prompt heading must use the format 'No. N: title'.");
        }

        var title = heading[(cursor + 1)..].Trim();
        return title.Length == 0
            ? new ParsedHeading(
                externalNo,
                null,
                PromptReadmeDiagnosticCodes.MissingTitle,
                "The prompt heading does not contain a title after the colon.")
            : new ParsedHeading(externalNo, title, null, null);
    }

    private static void SkipWhitespace(string value, ref int cursor)
    {
        while (cursor < value.Length && char.IsWhiteSpace(value[cursor]))
        {
            cursor++;
        }
    }

    private static Dictionary<SectionKind, List<Section>> FindSections(
        IReadOnlyList<Block> blocks,
        int startIndex,
        int endIndex,
        int sourcePosition,
        ICollection<PromptReadmeDiagnostic> diagnostics)
    {
        var sections = new Dictionary<SectionKind, List<Section>>();
        for (var index = startIndex; index < endIndex; index++)
        {
            if (blocks[index] is not HeadingBlock { Level: 4 } heading
                || !TryGetSectionKind(GetInlineText(heading.Inline), out var kind))
            {
                continue;
            }

            var sectionEnd = endIndex;
            for (var candidate = index + 1; candidate < endIndex; candidate++)
            {
                if (blocks[candidate] is HeadingBlock { Level: <= 4 })
                {
                    sectionEnd = candidate;
                    break;
                }
            }

            if (!sections.TryGetValue(kind, out var matches))
            {
                matches = [];
                sections.Add(kind, matches);
            }

            matches.Add(new Section(index + 1, sectionEnd, heading));
            if (matches.Count > 1)
            {
                AddDiagnostic(
                    diagnostics,
                    PromptReadmeDiagnosticCodes.DuplicateSection,
                    $"The prompt entry contains more than one {GetSectionName(kind)} section; the first is used.",
                    PromptReadmeDiagnosticSeverity.Warning,
                    sourcePosition,
                    ToSourceSpan(heading));
            }
        }

        return sections;
    }

    private static bool TryGetSectionKind(string heading, out SectionKind kind)
    {
        var normalized = NormalizeSectionHeading(heading);
        if (normalized.Equals("Description", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("描述", StringComparison.Ordinal))
        {
            kind = SectionKind.Description;
            return true;
        }

        if (normalized.Equals("Prompt", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("提示词", StringComparison.Ordinal))
        {
            kind = SectionKind.Prompt;
            return true;
        }

        if (normalized.Equals("Generated Images", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("生成图片", StringComparison.Ordinal))
        {
            kind = SectionKind.GeneratedImages;
            return true;
        }

        if (normalized.Equals("Details", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("详情", StringComparison.Ordinal))
        {
            kind = SectionKind.Details;
            return true;
        }

        kind = default;
        return false;
    }

    private static string NormalizeSectionHeading(string heading)
    {
        var normalized = heading.Trim();
        var firstLetter = 0;
        while (firstLetter < normalized.Length && !char.IsLetter(normalized, firstLetter))
        {
            firstLetter++;
        }

        normalized = firstLetter < normalized.Length ? normalized[firstLetter..].Trim() : string.Empty;
        return normalized.TrimEnd(':').Trim();
    }

    private static string GetSectionName(SectionKind kind) => kind switch
    {
        SectionKind.Description => "Description",
        SectionKind.Prompt => "Prompt",
        SectionKind.GeneratedImages => "Generated Images",
        SectionKind.Details => "Details",
        _ => kind.ToString()
    };

    private static Section? GetFirstSection(
        IReadOnlyDictionary<SectionKind, List<Section>> sections,
        SectionKind kind)
    {
        return sections.TryGetValue(kind, out var matches) && matches.Count > 0
            ? matches[0]
            : null;
    }

    private static string? ExtractDescription(Section? section, IReadOnlyList<Block> blocks)
    {
        if (section is null)
        {
            return null;
        }

        var paragraphs = new List<string>();
        foreach (var block in EnumerateSectionBlocks(section, blocks))
        {
            if (block is ParagraphBlock paragraph)
            {
                var text = GetInlineText(paragraph.Inline).Trim();
                if (text.Length > 0)
                {
                    paragraphs.Add(text);
                }
            }
        }

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static string? ExtractPreambleDescription(
        IReadOnlyList<Block> blocks,
        int startIndex,
        int endIndex)
    {
        var paragraphs = new List<string>();
        for (var index = startIndex; index < endIndex; index++)
        {
            if (blocks[index] is HeadingBlock { Level: <= 4 })
            {
                break;
            }

            foreach (var paragraph in EnumerateBlockAndDescendants(blocks[index]).OfType<ParagraphBlock>())
            {
                var text = GetInlineText(paragraph.Inline).Trim();
                if (text.Length > 0)
                {
                    paragraphs.Add(text);
                }
            }
        }

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static string? ExtractPrompt(Section? section, IReadOnlyList<Block> blocks)
    {
        if (section is null)
        {
            return null;
        }

        var codeBlock = EnumerateSectionBlocks(section, blocks).OfType<FencedCodeBlock>().FirstOrDefault();
        if (codeBlock is null)
        {
            return null;
        }

        var prompt = codeBlock.Lines.ToString();
        return string.IsNullOrWhiteSpace(prompt) ? null : prompt;
    }

    private static string? ExtractCoverSourceUrl(
        Section? section,
        IReadOnlyList<Block> blocks,
        IReadOnlySet<string> allowedHosts,
        int sourcePosition,
        ICollection<PromptReadmeDiagnostic> diagnostics)
    {
        if (section is null)
        {
            return null;
        }

        string? firstAllowedUrl = null;
        foreach (var candidate in EnumerateImageCandidates(section, blocks))
        {
            if (IsAllowedImageUrl(candidate.Url, allowedHosts))
            {
                firstAllowedUrl ??= WebUtility.HtmlDecode(candidate.Url.Trim());
                continue;
            }

            AddDiagnostic(
                diagnostics,
                PromptReadmeDiagnosticCodes.DisallowedImageUrl,
                $"Ignored Generated Images URL because it is not an allowed HTTPS origin: {candidate.Url}",
                PromptReadmeDiagnosticSeverity.Warning,
                sourcePosition,
                candidate.SourceSpan);
        }

        return firstAllowedUrl;
    }

    private static bool IsAllowedImageUrl(string value, IReadOnlySet<string> allowedHosts)
    {
        var decoded = WebUtility.HtmlDecode(value.Trim());
        return Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.IsDefaultPort
            && allowedHosts.Contains(uri.IdnHost.TrimEnd('.'));
    }

    private static IEnumerable<ImageCandidate> EnumerateImageCandidates(
        Section section,
        IReadOnlyList<Block> blocks)
    {
        foreach (var block in EnumerateSectionBlocks(section, blocks))
        {
            var sourceSpan = ToSourceSpan(block);
            if (block is HtmlBlock htmlBlock)
            {
                foreach (var source in ExtractHtmlImageSources(htmlBlock.Lines.ToString()))
                {
                    yield return new ImageCandidate(source, sourceSpan);
                }
            }

            if (block is not LeafBlock { Inline: not null } leafBlock)
            {
                continue;
            }

            foreach (var inline in EnumerateInlines(leafBlock.Inline))
            {
                if (inline is LinkInline { IsImage: true, Url: not null } imageLink)
                {
                    yield return new ImageCandidate(imageLink.Url, sourceSpan);
                }
                else if (inline is HtmlInline htmlInline)
                {
                    foreach (var source in ExtractHtmlImageSources(htmlInline.Tag))
                    {
                        yield return new ImageCandidate(source, sourceSpan);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ExtractHtmlImageSources(string html)
    {
        var cursor = 0;
        while (cursor < html.Length)
        {
            var tagStart = html.IndexOf('<', cursor);
            if (tagStart < 0)
            {
                yield break;
            }

            if (html.AsSpan(tagStart).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = html.IndexOf("-->", tagStart + 4, StringComparison.Ordinal);
                cursor = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }

            var nameStart = tagStart + 1;
            while (nameStart < html.Length && char.IsWhiteSpace(html[nameStart]))
            {
                nameStart++;
            }

            if (nameStart >= html.Length || html[nameStart] is '/' or '!' or '?')
            {
                cursor = nameStart + 1;
                continue;
            }

            var nameEnd = nameStart;
            while (nameEnd < html.Length && IsHtmlNameCharacter(html[nameEnd]))
            {
                nameEnd++;
            }

            var tagEnd = FindHtmlTagEnd(html, nameEnd);
            if (!html.AsSpan(nameStart, nameEnd - nameStart).Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                cursor = tagEnd < html.Length ? tagEnd + 1 : html.Length;
                continue;
            }

            var attributeCursor = nameEnd;
            while (attributeCursor < tagEnd)
            {
                while (attributeCursor < tagEnd
                    && (char.IsWhiteSpace(html[attributeCursor]) || html[attributeCursor] == '/'))
                {
                    attributeCursor++;
                }

                var attributeNameStart = attributeCursor;
                while (attributeCursor < tagEnd && IsHtmlNameCharacter(html[attributeCursor]))
                {
                    attributeCursor++;
                }

                if (attributeNameStart == attributeCursor)
                {
                    attributeCursor++;
                    continue;
                }

                var attributeName = html.AsSpan(attributeNameStart, attributeCursor - attributeNameStart);
                while (attributeCursor < tagEnd && char.IsWhiteSpace(html[attributeCursor]))
                {
                    attributeCursor++;
                }

                if (attributeCursor >= tagEnd || html[attributeCursor] != '=')
                {
                    continue;
                }

                attributeCursor++;
                while (attributeCursor < tagEnd && char.IsWhiteSpace(html[attributeCursor]))
                {
                    attributeCursor++;
                }

                var valueStart = attributeCursor;
                var valueEnd = attributeCursor;
                if (attributeCursor < tagEnd && html[attributeCursor] is '\'' or '"')
                {
                    var quote = html[attributeCursor++];
                    valueStart = attributeCursor;
                    while (attributeCursor < tagEnd && html[attributeCursor] != quote)
                    {
                        attributeCursor++;
                    }

                    valueEnd = attributeCursor;
                    if (attributeCursor < tagEnd)
                    {
                        attributeCursor++;
                    }
                }
                else
                {
                    valueStart = attributeCursor;
                    while (attributeCursor < tagEnd
                        && !char.IsWhiteSpace(html[attributeCursor])
                        && html[attributeCursor] != '>')
                    {
                        attributeCursor++;
                    }

                    valueEnd = attributeCursor;
                }

                if (attributeName.Equals("src", StringComparison.OrdinalIgnoreCase) && valueEnd > valueStart)
                {
                    yield return WebUtility.HtmlDecode(html[valueStart..valueEnd]);
                }
            }

            cursor = tagEnd < html.Length ? tagEnd + 1 : html.Length;
        }
    }

    private static int FindHtmlTagEnd(string html, int cursor)
    {
        char quote = '\0';
        while (cursor < html.Length)
        {
            var character = html[cursor];
            if (quote == '\0' && character is '\'' or '"')
            {
                quote = character;
            }
            else if (quote != '\0' && character == quote)
            {
                quote = '\0';
            }
            else if (quote == '\0' && character == '>')
            {
                return cursor;
            }

            cursor++;
        }

        return html.Length;
    }

    private static bool IsHtmlNameCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or ':';
    }

    private static ParsedDetails ExtractDetails(
        Section? section,
        IReadOnlyList<Block> blocks,
        int sourcePosition,
        ICollection<PromptReadmeDiagnostic> diagnostics)
    {
        if (section is null)
        {
            return new ParsedDetails();
        }

        var details = new ParsedDetails();
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in EnumerateSectionBlocks(section, blocks).OfType<ParagraphBlock>())
        {
            var text = GetInlineText(paragraph.Inline).Trim();
            var separator = FindDetailSeparator(text);
            if (separator <= 0)
            {
                continue;
            }

            var field = CanonicalizeDetailField(text[..separator]);
            var value = text[(separator + 1)..].Trim();
            if (field is null)
            {
                continue;
            }

            if (!seenFields.Add(field))
            {
                AddDiagnostic(
                    diagnostics,
                    PromptReadmeDiagnosticCodes.DuplicateField,
                    $"The Details section contains more than one {field} field; the first is used.",
                    PromptReadmeDiagnosticSeverity.Warning,
                    sourcePosition,
                    ToSourceSpan(paragraph));
                continue;
            }

            var linkUrl = FindFirstNonImageLink(paragraph.Inline);
            switch (field)
            {
                case "Author":
                    details.AuthorName = NullIfWhiteSpace(value);
                    details.AuthorUrl = ParseOptionalUrl(linkUrl, "Author", paragraph, sourcePosition, diagnostics);
                    break;
                case "Source":
                    details.SourceUrl = ParseOptionalUrl(
                        linkUrl ?? value,
                        "Source",
                        paragraph,
                        sourcePosition,
                        diagnostics);
                    break;
                case "Published":
                    details.Published = NullIfWhiteSpace(value);
                    break;
                case "Language":
                    details.Language = NullIfWhiteSpace(value);
                    break;
            }
        }

        return details;
    }

    private static int FindDetailSeparator(string value)
    {
        var asciiSeparator = value.IndexOf(':');
        var fullWidthSeparator = value.IndexOf('：');
        return asciiSeparator < 0
            ? fullWidthSeparator
            : fullWidthSeparator < 0
                ? asciiSeparator
                : Math.Min(asciiSeparator, fullWidthSeparator);
    }

    private static string? CanonicalizeDetailField(string value)
    {
        var field = value.Trim();
        if (field.Equals("Author", StringComparison.OrdinalIgnoreCase)
            || field.Equals("作者", StringComparison.Ordinal))
        {
            return "Author";
        }

        if (field.Equals("Source", StringComparison.OrdinalIgnoreCase)
            || field.Equals("来源", StringComparison.Ordinal))
        {
            return "Source";
        }

        if (field.Equals("Published", StringComparison.OrdinalIgnoreCase)
            || field.Equals("发布时间", StringComparison.Ordinal)
            || field.Equals("发布日期", StringComparison.Ordinal))
        {
            return "Published";
        }

        return field.Equals("Language", StringComparison.OrdinalIgnoreCase)
            || field.Equals("Languages", StringComparison.OrdinalIgnoreCase)
            || field.Equals("语言", StringComparison.Ordinal)
            || field.Equals("多语言", StringComparison.Ordinal)
                ? "Language"
                : null;
    }

    private static string? ExtractStableId(
        IReadOnlyList<Block> blocks,
        int startIndex,
        int endIndex)
    {
        for (var index = startIndex; index < endIndex; index++)
        {
            foreach (var block in EnumerateBlockAndDescendants(blocks[index]))
            {
                if (block is not LeafBlock { Inline: not null } leafBlock)
                {
                    continue;
                }

                foreach (var inline in EnumerateInlines(leafBlock.Inline))
                {
                    var url = inline switch
                    {
                        LinkInline { IsImage: false, Url: not null } link => link.Url,
                        AutolinkInline { Url: not null } autolink => autolink.Url,
                        _ => null
                    };
                    if (TryReadStableId(url, out var stableId))
                    {
                        return stableId;
                    }
                }
            }
        }

        return null;
    }

    private static bool TryReadStableId(string? value, out string stableId)
    {
        stableId = string.Empty;
        if (!Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var uri)
            || !uri.IdnHost.Equals("youmind.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.EndsWith("/gpt-image-2-prompts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("id", out var values))
        {
            return false;
        }

        stableId = values.FirstOrDefault()?.Trim() ?? string.Empty;
        return stableId.Length > 0;
    }

    private static string? ParseOptionalUrl(
        string? value,
        string field,
        Block sourceBlock,
        int sourcePosition,
        ICollection<PromptReadmeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = WebUtility.HtmlDecode(value.Trim());
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return trimmed;
        }

        AddDiagnostic(
            diagnostics,
            PromptReadmeDiagnosticCodes.InvalidOptionalUrl,
            $"Ignored the {field} URL because it is not an absolute HTTP(S) URL: {value}",
            PromptReadmeDiagnosticSeverity.Warning,
            sourcePosition,
            ToSourceSpan(sourceBlock));
        return null;
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? FindFirstNonImageLink(ContainerInline? container)
    {
        if (container is null)
        {
            return null;
        }

        foreach (var inline in EnumerateInlines(container))
        {
            if (inline is LinkInline { IsImage: false, Url: not null } link)
            {
                return link.Url;
            }

            if (inline is AutolinkInline { Url: not null } autolink)
            {
                return autolink.Url;
            }
        }

        return null;
    }

    private static IEnumerable<Block> EnumerateSectionBlocks(Section section, IReadOnlyList<Block> blocks)
    {
        for (var index = section.StartIndex; index < section.EndIndex; index++)
        {
            foreach (var block in EnumerateBlockAndDescendants(blocks[index]))
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<Block> EnumerateBlockAndDescendants(Block block)
    {
        yield return block;
        if (block is not ContainerBlock container)
        {
            yield break;
        }

        foreach (var child in container)
        {
            foreach (var descendant in EnumerateBlockAndDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateInlines(ContainerInline container)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            yield return inline;
            if (inline is not ContainerInline nested)
            {
                continue;
            }

            foreach (var descendant in EnumerateInlines(nested))
            {
                yield return descendant;
            }
        }
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendInlineText(container, builder);
        return WebUtility.HtmlDecode(builder.ToString());
    }

    private static void AppendInlineText(ContainerInline container, StringBuilder builder)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.ToString());
                    break;
                case LineBreakInline:
                    builder.AppendLine();
                    break;
                case LinkInline { IsImage: true }:
                    break;
                case ContainerInline nested:
                    AppendInlineText(nested, builder);
                    break;
            }
        }
    }

    private static PromptReadmeSourceSpan ToSourceSpan(Block block)
    {
        return ToSourceSpan(block, block);
    }

    private static PromptReadmeSourceSpan ToSourceSpan(Block startBlock, Block endBlock)
    {
        return new PromptReadmeSourceSpan(
            startBlock.Span.Start,
            endBlock.Span.End,
            startBlock.Line + 1,
            startBlock.Column + 1);
    }

    private static void AddDiagnostic(
        ICollection<PromptReadmeDiagnostic> diagnostics,
        string code,
        string message,
        PromptReadmeDiagnosticSeverity severity,
        int? sourcePosition,
        PromptReadmeSourceSpan? sourceSpan)
    {
        diagnostics.Add(new PromptReadmeDiagnostic(code, message, severity, sourcePosition, sourceSpan));
    }

    private enum SectionKind
    {
        Description,
        Prompt,
        GeneratedImages,
        Details
    }

    private sealed record ParsedHeading(
        int? ExternalNo,
        string? Title,
        string? ErrorCode,
        string? ErrorMessage);

    private sealed record Section(int StartIndex, int EndIndex, HeadingBlock Heading);

    private sealed record ImageCandidate(string Url, PromptReadmeSourceSpan SourceSpan);

    private sealed class ParsedDetails
    {
        public string? AuthorName { get; set; }

        public string? AuthorUrl { get; set; }

        public string? SourceUrl { get; set; }

        public string? Published { get; set; }

        public string? Language { get; set; }
    }
}
