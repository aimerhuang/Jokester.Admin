using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Application.Security;

namespace Jokester.PromptLexiconTool;

public sealed partial class LexiconPreparer
{
    private const long MaxInputFileBytes = 20 * 1024 * 1024;
    private static readonly HashSet<string> SupportedSpdxLicenseIdentifiers =
    [
        "AGPL-3.0-only",
        "AGPL-3.0-or-later",
        "Apache-2.0",
        "BSD-2-Clause",
        "BSD-3-Clause",
        "CC0-1.0",
        "GPL-3.0-only",
        "GPL-3.0-or-later",
        "LGPL-3.0-only",
        "MIT",
        "MPL-2.0",
        "Unlicense"
    ];

    public async Task<PreparedLexiconResult> PrepareAsync(
        LexiconManifest manifest,
        string manifestDirectory,
        CancellationToken cancellationToken)
    {
        ValidateManifest(manifest);
        var report = new LexiconPreparationReport
        {
            SourceCode = manifest.SourceCode.Trim(),
            SourceVersion = manifest.SourceVersion.Trim(),
            SourceUrl = manifest.SourceUrl.Trim(),
            License = manifest.License.Trim(),
            GeneratedAtUtc = DateTime.UtcNow
        };
        var candidates = new Dictionary<string, CandidateAccumulator>(StringComparer.Ordinal);

        foreach (var fileSpec in manifest.Files)
        {
            var fileResult = await ReadFileAsync(fileSpec, manifestDirectory, cancellationToken);
            report.RawTermCount += fileResult.RawTermCount;
            report.InvalidTermCount += fileResult.InvalidTermCount;
            report.UnmappedTaggedTermCount += fileResult.UnmappedTaggedTermCount;
            var acceptedFromFile = 0;

            foreach (var input in fileResult.Terms)
            {
                var term = input.Term.Trim();
                var normalizedTerm = AiPromptTextNormalizer.NormalizeRuleTerm(term, fileSpec.MatchMode);
                if (term.Length is < 1 or > 255 || normalizedTerm.Length is < 1 or > 512)
                {
                    report.InvalidTermCount++;
                    fileResult.InvalidTermCount++;
                    continue;
                }

                acceptedFromFile++;
                var termKey = CreateTermKey(fileSpec.MatchMode, normalizedTerm);
                if (candidates.TryGetValue(termKey, out var existing))
                {
                    report.DuplicateCount++;
                    if (!string.Equals(existing.LanguageCode, fileSpec.LanguageCode, StringComparison.Ordinal)
                        || !string.Equals(existing.ProposedAction, fileSpec.ProposedAction, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate normalized term has conflicting language or action: {term}");
                    }

                    existing.SourceFiles.Add(fileSpec.Path);
                    existing.Severity = Math.Max(existing.Severity, fileSpec.Severity);
                    if (string.CompareOrdinal(term, existing.Term) < 0)
                    {
                        existing.Term = term;
                    }
                    if (!string.Equals(existing.CategoryCode, input.CategoryCode, StringComparison.Ordinal))
                    {
                        if (existing.ReviewReasons.Add("category_conflict"))
                        {
                            report.CategoryConflictCount++;
                        }
                        if (string.CompareOrdinal(input.CategoryCode, existing.CategoryCode) < 0)
                        {
                            existing.CategoryCode = input.CategoryCode;
                        }
                    }
                    continue;
                }

                var accumulator = new CandidateAccumulator
                {
                    Term = term,
                    NormalizedTerm = normalizedTerm,
                    TermKey = termKey,
                    LanguageCode = fileSpec.LanguageCode,
                    CategoryCode = input.CategoryCode,
                    MatchMode = fileSpec.MatchMode,
                    ProposedAction = fileSpec.ProposedAction,
                    Severity = fileSpec.Severity
                };
                accumulator.SourceFiles.Add(fileSpec.Path);

                if (normalizedTerm.EnumerateRunes().Count() <= 2)
                {
                    accumulator.ReviewReasons.Add("short_term");
                    report.ShortTermCount++;
                }
                if (LooksLikeUrl(term))
                {
                    accumulator.ReviewReasons.Add("url_like");
                    report.UrlLikeTermCount++;
                }
                if (LooksLikeSpreadsheetFormula(term))
                {
                    accumulator.ReviewReasons.Add("spreadsheet_formula");
                    report.SpreadsheetFormulaTermCount++;
                }

                candidates.Add(termKey, accumulator);
            }

            report.Files.Add(new LexiconInputFileReport(
                fileSpec.Path,
                fileSpec.Format,
                fileResult.SizeBytes,
                fileResult.Sha256,
                fileResult.RawTermCount,
                acceptedFromFile,
                fileResult.InvalidTermCount,
                fileResult.UnmappedTaggedTermCount));
        }

        var prepared = candidates.Values
            .OrderBy(x => x.CategoryCode, StringComparer.Ordinal)
            .ThenBy(x => x.NormalizedTerm, StringComparer.Ordinal)
            .Select(x => new PreparedLexiconCandidate(
                x.Term,
                x.NormalizedTerm,
                x.TermKey,
                x.LanguageCode,
                x.CategoryCode,
                x.MatchMode,
                x.ProposedAction,
                x.Severity,
                0,
                manifest.SourceCode.Trim(),
                manifest.SourceVersion.Trim(),
                manifest.SourceUrl.Trim(),
                manifest.License.Trim(),
                string.Join('|', x.SourceFiles.Order(StringComparer.Ordinal)),
                x.ReviewReasons.Count == 0
                    ? "unreviewed"
                    : string.Join(';', x.ReviewReasons.Order(StringComparer.Ordinal))))
            .ToArray();
        report.CandidateCount = prepared.Length;
        return new PreparedLexiconResult(prepared, report);
    }

    private static async Task<FileReadResult> ReadFileAsync(
        LexiconFileSpec fileSpec,
        string manifestDirectory,
        CancellationToken cancellationToken)
    {
        ValidateFileSpec(fileSpec);
        var fullPath = Path.GetFullPath(fileSpec.Path, manifestDirectory);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"Lexicon input file does not exist: {fileSpec.Path}", fullPath);
        }
        if (fileInfo.Length > MaxInputFileBytes)
        {
            throw new InvalidOperationException(
                $"Lexicon input file exceeds {MaxInputFileBytes} bytes: {fileSpec.Path}");
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var content = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var result = new FileReadResult
        {
            SizeBytes = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
        if (!string.Equals(result.Sha256, fileSpec.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Lexicon input SHA-256 does not match the manifest: {fileSpec.Path}");
        }
        var rawValues = fileSpec.Format switch
        {
            "comma" => content.Split(
                [',', '，', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => content.Split(
                ["\r\n", "\n", "\r"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };

        foreach (var rawValue in rawValues)
        {
            var value = rawValue.Trim();
            if (value.Length == 0 || value.StartsWith('#') || value.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            result.RawTermCount++;
            if (fileSpec.Format == "tagged")
            {
                if (!TryParseTaggedTerm(value, fileSpec, out var taggedTerm))
                {
                    result.UnmappedTaggedTermCount++;
                    continue;
                }
                result.Terms.Add(taggedTerm);
                continue;
            }

            result.Terms.Add(new InputTerm(value, fileSpec.CategoryCode));
        }

        return result;
    }

    private static bool TryParseTaggedTerm(
        string value,
        LexiconFileSpec fileSpec,
        out InputTerm term)
    {
        term = default!;
        var separatorIndex = value.LastIndexOfAny([' ', '\t']);
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        var rawTerm = value[..separatorIndex].TrimEnd();
        var tags = value[(separatorIndex + 1)..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawTerm.Length == 0 || tags.Length == 0 || tags.Any(tag => tag.Any(character => !char.IsAsciiDigit(character))))
        {
            return false;
        }

        IEnumerable<string> priority = fileSpec.TagPriority.Count > 0 ? fileSpec.TagPriority : tags;
        foreach (var tag in priority)
        {
            if (tags.Contains(tag, StringComparer.Ordinal)
                && fileSpec.TagMappings.TryGetValue(tag, out var categoryCode))
            {
                term = new InputTerm(rawTerm, categoryCode);
                return true;
            }
        }

        return false;
    }

    private static void ValidateManifest(LexiconManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SourceCode)
            || manifest.SourceCode.Length > 100
            || string.IsNullOrWhiteSpace(manifest.SourceVersion)
            || manifest.SourceVersion.Length > 100
            || !CommitShaRegex().IsMatch(manifest.SourceVersion.Trim())
            || !Uri.TryCreate(manifest.SourceUrl.Trim(), UriKind.Absolute, out var sourceUri)
            || sourceUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(sourceUri.UserInfo)
            || !string.IsNullOrEmpty(sourceUri.Query)
            || !string.IsNullOrEmpty(sourceUri.Fragment)
            || string.IsNullOrWhiteSpace(manifest.License)
            || !SupportedSpdxLicenseIdentifiers.Contains(manifest.License.Trim()))
        {
            throw new InvalidOperationException(
                "Manifest requires sourceCode, an HTTPS sourceUrl, an immutable commit SHA, and a supported SPDX license identifier");
        }
        if (manifest.Files.Count == 0)
        {
            throw new InvalidOperationException("Manifest must contain at least one input file");
        }
    }

    private static void ValidateFileSpec(LexiconFileSpec fileSpec)
    {
        fileSpec.Path = fileSpec.Path.Trim();
        fileSpec.Format = fileSpec.Format.Trim().ToLowerInvariant();
        fileSpec.LanguageCode = fileSpec.LanguageCode.Trim().ToLowerInvariant();
        fileSpec.CategoryCode = fileSpec.CategoryCode.Trim().ToLowerInvariant();
        fileSpec.MatchMode = fileSpec.MatchMode.Trim().ToLowerInvariant();
        fileSpec.ProposedAction = fileSpec.ProposedAction.Trim().ToLowerInvariant();
        fileSpec.ExpectedSha256 = fileSpec.ExpectedSha256.Trim().ToLowerInvariant();
        fileSpec.TagMappings = fileSpec.TagMappings.ToDictionary(
            pair => pair.Key.Trim(),
            pair => pair.Value.Trim().ToLowerInvariant(),
            StringComparer.Ordinal);
        fileSpec.TagPriority = fileSpec.TagPriority.Select(x => x.Trim()).ToList();

        if (string.IsNullOrWhiteSpace(fileSpec.Path)
            || fileSpec.Format is not ("lines" or "comma" or "tagged")
            || fileSpec.LanguageCode is not ("zh" or "en" or "mixed")
            || !AiPromptFilterMatchModes.IsSupported(fileSpec.MatchMode)
            || !AiPromptFilterActions.IsSupported(fileSpec.ProposedAction)
            || fileSpec.Severity is < 1 or > 5
            || !Sha256Regex().IsMatch(fileSpec.ExpectedSha256))
        {
            throw new InvalidOperationException($"Invalid lexicon file specification: {fileSpec.Path}");
        }

        if (fileSpec.Format == "tagged")
        {
            if (fileSpec.TagMappings.Count == 0
                || fileSpec.TagMappings.Values.Any(category => !IsValidCategory(category)))
            {
                throw new InvalidOperationException(
                    $"Tagged lexicon file requires valid tagMappings: {fileSpec.Path}");
            }
        }
        else if (!IsValidCategory(fileSpec.CategoryCode))
        {
            throw new InvalidOperationException($"Invalid categoryCode for lexicon file: {fileSpec.Path}");
        }
    }

    private static bool IsValidCategory(string value)
    {
        return value.Length is >= 1 and <= 50
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private static string CreateTermKey(string matchMode, string normalizedTerm)
    {
        var value = Encoding.UTF8.GetBytes($"{matchMode}:{normalizedTerm}");
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private static bool LooksLikeUrl(string term)
    {
        return term.Contains("://", StringComparison.Ordinal)
            || term.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || DomainLikeRegex().IsMatch(term);
    }

    private static bool LooksLikeSpreadsheetFormula(string term)
    {
        return term.Length > 0 && term[0] is '=' or '+' or '-' or '@';
    }

    [GeneratedRegex(@"^[0-9a-f]{40}([0-9a-f]{24})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex(@"^[0-9a-f]{64}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(
        @"^[a-z0-9][a-z0-9.-]*\.(com|net|org|cn|io|cc|xyz)(/|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DomainLikeRegex();

    private sealed class CandidateAccumulator
    {
        public string Term { get; set; } = string.Empty;
        public string NormalizedTerm { get; init; } = string.Empty;
        public string TermKey { get; init; } = string.Empty;
        public string LanguageCode { get; init; } = string.Empty;
        public string CategoryCode { get; set; } = string.Empty;
        public string MatchMode { get; init; } = string.Empty;
        public string ProposedAction { get; init; } = string.Empty;
        public int Severity { get; set; }
        public HashSet<string> SourceFiles { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ReviewReasons { get; } = new(StringComparer.Ordinal);
    }

    private sealed class FileReadResult
    {
        public long SizeBytes { get; init; }
        public string Sha256 { get; init; } = string.Empty;
        public int RawTermCount { get; set; }
        public int InvalidTermCount { get; set; }
        public int UnmappedTaggedTermCount { get; set; }
        public List<InputTerm> Terms { get; } = [];
    }

    private sealed record InputTerm(string Term, string CategoryCode);
}
