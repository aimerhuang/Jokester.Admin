using System.Text;
using System.Text.Json;

namespace Jokester.PromptLexiconTool;

public static class CliProgram
{
    public static Task<int> Main(string[] args) => RunAsync(args);

    private static async Task<int> RunAsync(string[] arguments)
    {
        try
        {
            if (arguments.Length == 0 || !string.Equals(arguments[0], "prepare", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                return 2;
            }

            var options = ParseOptions(arguments[1..]);
            var manifestPath = RequirePath(options, "manifest");
            var outputPath = RequirePath(options, "output");
            var reportPath = RequirePath(options, "report");
            if (PathsEqual(outputPath, reportPath)
                || PathsEqual(outputPath, manifestPath)
                || PathsEqual(reportPath, manifestPath))
            {
                throw new InvalidOperationException("Manifest, candidate output, and report paths must be different");
            }
            EnsureOutputDoesNotExist(outputPath);
            EnsureOutputDoesNotExist(reportPath);

            var manifestJson = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<LexiconManifest>(manifestJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Manifest JSON is empty");
            var manifestDirectory = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException("Manifest path has no parent directory");
            foreach (var file in manifest.Files)
            {
                var inputPath = Path.GetFullPath(file.Path, manifestDirectory);
                if (PathsEqual(inputPath, outputPath) || PathsEqual(inputPath, reportPath))
                {
                    throw new InvalidOperationException("Output paths cannot overwrite a lexicon input file");
                }
            }

            var result = await new LexiconPreparer().PrepareAsync(
                manifest,
                manifestDirectory,
                CancellationToken.None);
            var csv = BuildCsv(result.Candidates);
            var report = JsonSerializer.Serialize(result.Report, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await WriteAtomicAsync(outputPath, csv);
            await WriteAtomicAsync(reportPath, report + Environment.NewLine);

            Console.WriteLine(
                $"Prepared {result.Report.CandidateCount} disabled candidates from "
                + $"{result.Report.RawTermCount} raw terms. "
                + $"duplicates={result.Report.DuplicateCount}, invalid={result.Report.InvalidTermCount}, "
                + $"unmapped={result.Report.UnmappedTaggedTermCount}, short={result.Report.ShortTermCount}, "
                + $"urlLike={result.Report.UrlLikeTermCount}, "
                + $"spreadsheetFormula={result.Report.SpreadsheetFormulaTermCount}, "
                + $"conflicts={result.Report.CategoryConflictCount}");
            Console.WriteLine($"Candidates: {outputPath}");
            Console.WriteLine($"Report: {reportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lexicon preparation failed: {ex.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Options must use --name value pairs");
            }

            var name = arguments[index][2..];
            if (!result.TryAdd(name, arguments[index + 1]))
            {
                throw new InvalidOperationException($"Duplicate option: --{name}");
            }
        }

        return result;
    }

    private static string RequirePath(IReadOnlyDictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option: --{name}");
        }

        return Path.GetFullPath(value);
    }

    private static string BuildCsv(IReadOnlyList<PreparedLexiconCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "term,normalized_term,term_key,language_code,category_code,match_mode,proposed_action,severity,status,source_code,source_version,source_url,license,source_file,review_reason");
        foreach (var candidate in candidates)
        {
            var values = new object[]
            {
                candidate.Term,
                candidate.NormalizedTerm,
                candidate.TermKey,
                candidate.LanguageCode,
                candidate.CategoryCode,
                candidate.MatchMode,
                candidate.ProposedAction,
                candidate.Severity,
                candidate.Status,
                candidate.SourceCode,
                candidate.SourceVersion,
                candidate.SourceUrl,
                candidate.License,
                candidate.SourceFile,
                candidate.ReviewReason
            };
            builder.AppendLine(string.Join(',', values.Select(value => EscapeCsv(Convert.ToString(value) ?? string.Empty))));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = $"'{value}";
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private static void EnsureOutputDoesNotExist(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new InvalidOperationException($"Output path already exists: {path}");
        }
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Output path has no parent directory: {path}");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet run --project tools/Jokester.PromptLexiconTool -- prepare "
            + "--manifest <manifest.json> --output <candidates.csv> --report <report.json>");
    }
}
