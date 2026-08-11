using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Models.PromptLibrary;
using Microsoft.Extensions.Options;

namespace jokester.admin.Infrastructure.PromptLibrary;

public sealed class YouMarketingPromptSourceClient(
    HttpClient httpClient,
    IPromptReadmeParser readmeParser,
    IOptions<PromptLibraryOptions> options,
    ILogger<YouMarketingPromptSourceClient> logger) : IPromptLibrarySourceClient
{
    private const int MaxSourceBytes = 5 * 1024 * 1024;
    private const int MaxDiagnostics = 200;
    private const int MaxStableIdLength = 200;
    private const int MaxPromptLength = 4000;
    private const int MinimumHanLetterPercentage = 25;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly PromptLibraryOptions settings = options.Value;

    public async Task<PromptLibrarySourceSnapshot> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var markdown = await FetchMarkdownAsync(cancellationToken);
        var parsed = readmeParser.Parse(
            markdown,
            new PromptReadmeParseOptions(settings.ImageAllowedHosts));
        var selectedItems = new List<PromptLibrarySourceItem>();
        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        var skippedCount = parsed.SkippedItems.Count;

        foreach (var diagnostic in parsed.Diagnostics)
        {
            AddDiagnostic(
                diagnostics,
                $"source_position={diagnostic.SourcePosition?.ToString() ?? "document"};reason={diagnostic.Code};message={diagnostic.Message}");
        }

        foreach (var parsedItem in parsed.Items.OrderBy(item => item.SourcePosition))
        {
            if (selectedItems.Count >= settings.TargetCount)
            {
                break;
            }

            var stableId = parsedItem.StableId?.Trim();
            if (string.IsNullOrWhiteSpace(stableId) || stableId.Length > MaxStableIdLength)
            {
                skippedCount++;
                AddDiagnostic(diagnostics, $"source_position={parsedItem.SourcePosition};reason=missing_or_invalid_id");
                continue;
            }
            if (!stableIds.Add(stableId))
            {
                skippedCount++;
                AddDiagnostic(diagnostics, $"source_position={parsedItem.SourcePosition};reason=duplicate_id:{stableId}");
                continue;
            }
            if (!LooksLikeChineseText(parsedItem.Title)
                || !LooksLikeChineseText(parsedItem.Description)
                || !LooksLikeChineseText(parsedItem.PromptText))
            {
                skippedCount++;
                AddDiagnostic(
                    diagnostics,
                    $"source_position={parsedItem.SourcePosition};reason=title_description_and_prompt_must_all_be_substantially_chinese");
                continue;
            }
            if (parsedItem.Title.Length > 300)
            {
                skippedCount++;
                AddDiagnostic(diagnostics, $"source_position={parsedItem.SourcePosition};reason=title_too_long");
                continue;
            }
            if (parsedItem.PromptText.Length > MaxPromptLength)
            {
                skippedCount++;
                AddDiagnostic(diagnostics, $"source_position={parsedItem.SourcePosition};reason=prompt_too_long");
                continue;
            }

            selectedItems.Add(new PromptLibrarySourceItem(
                stableId,
                parsedItem.ExternalNo,
                parsedItem.Title.Trim(),
                parsedItem.Description.Trim(),
                parsedItem.PromptText.Trim(),
                parsedItem.CoverSourceUrl,
                parsedItem.AuthorName,
                parsedItem.AuthorUrl,
                parsedItem.SourceUrl,
                parsedItem.Published,
                "zh-CN",
                selectedItems.Count + 1));
        }

        var hashBytes = JsonSerializer.SerializeToUtf8Bytes(selectedItems);
        var contentHash = Convert.ToHexString(SHA256.HashData(hashBytes)).ToLowerInvariant();
        logger.LogInformation(
            "Fetched prompt source snapshot. CandidateCount={CandidateCount}, SelectedCount={SelectedCount}, SkippedCount={SkippedCount}",
            parsed.CandidateCount,
            selectedItems.Count,
            skippedCount);
        return new PromptLibrarySourceSnapshot(
            selectedItems,
            parsed.CandidateCount,
            skippedCount,
            contentHash,
            diagnostics);
    }

    private async Task<string> FetchMarkdownAsync(CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= settings.RetryCount; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, settings.SourceApiUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
                request.Headers.AcceptLanguage.ParseAdd("zh-CN, zh;q=0.9");
                if (!string.IsNullOrWhiteSpace(settings.SourceApiToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        settings.SourceApiToken.Trim());
                }

                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    throw new InvalidDataException("Prompt source redirects are not allowed.");
                }

                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxSourceBytes)
                {
                    throw new InvalidDataException("Prompt source exceeds the configured safety limit.");
                }
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null
                    && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                    && !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Prompt source returned a non-text response.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var buffer = new MemoryStream();
                await CopyWithLimitAsync(source, buffer, MaxSourceBytes, cancellationToken);
                return StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken) && attempt < settings.RetryCount)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException("Prompt source request failed.");
    }

    private static bool LooksLikeChineseText(string value)
    {
        var hanCount = 0;
        var kanaCount = 0;
        var letterCount = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var codePoint = rune.Value;
            if (Rune.IsLetter(rune))
            {
                letterCount++;
            }
            if (IsHan(codePoint))
            {
                hanCount++;
            }
            else if (IsJapaneseKana(codePoint))
            {
                kanaCount++;
            }
        }

        return hanCount > 0
            && letterCount > 0
            && hanCount >= kanaCount
            && hanCount * 100 >= letterCount * MinimumHanLetterPercentage;
    }

    private static bool IsHan(int codePoint) =>
        codePoint is >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF
            or >= 0x20000 and <= 0x2EBEF;

    private static bool IsJapaneseKana(int codePoint) =>
        codePoint is >= 0x3040 and <= 0x30FF
            or >= 0x31F0 and <= 0x31FF
            or >= 0xFF66 and <= 0xFF9D;

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total = checked(total + read);
            if (total > maxBytes)
            {
                throw new InvalidDataException("Prompt source exceeds the configured safety limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && (exception is IOException or TaskCanceledException
            || exception is HttpRequestException { StatusCode: null }
            || exception is HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests }
            || exception is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError });

    private static void AddDiagnostic(ICollection<string> diagnostics, string diagnostic)
    {
        if (diagnostics.Count < MaxDiagnostics)
        {
            diagnostics.Add(diagnostic);
        }
    }
}
