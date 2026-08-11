using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jokester.PromptLexiconTool;

namespace jokester.admin.Tests;

public sealed class PromptLexiconToolTests
{
    [Fact]
    public async Task PrepareAsync_DisablesAndFlagsUnreviewedCandidatesWithVerifiedProvenance()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var taggedPath = Path.Combine(temporaryRoot, "tagged.txt");
            var violencePath = Path.Combine(temporaryRoot, "violence.txt");
            await File.WriteAllTextAsync(
                taggedPath,
                "long safe term 2\nx 2\nignored broad term 0\nexample.com 4\nduplicate phrase 2,4\n=1+1 4\n",
                new UTF8Encoding(false));
            await File.WriteAllTextAsync(
                violencePath,
                "duplicate phrase\nseparate violence phrase\n",
                new UTF8Encoding(false));
            var manifest = CreateManifest(
                new LexiconFileSpec
                {
                    Path = "tagged.txt",
                    Format = "tagged",
                    LanguageCode = "zh",
                    MatchMode = "compact",
                    ProposedAction = "audit",
                    Severity = 3,
                    ExpectedSha256 = await ComputeSha256Async(taggedPath),
                    TagMappings = new Dictionary<string, string>
                    {
                        ["2"] = "sexual_content",
                        ["4"] = "illegal_activity"
                    },
                    TagPriority = ["2", "4"]
                },
                new LexiconFileSpec
                {
                    Path = "violence.txt",
                    Format = "lines",
                    LanguageCode = "zh",
                    CategoryCode = "graphic_violence",
                    MatchMode = "compact",
                    ProposedAction = "audit",
                    Severity = 4,
                    ExpectedSha256 = await ComputeSha256Async(violencePath)
                });

            var result = await new LexiconPreparer().PrepareAsync(
                manifest,
                temporaryRoot,
                default);

            Assert.All(result.Candidates, candidate => Assert.Equal(0, candidate.Status));
            Assert.All(result.Candidates, candidate => Assert.Equal("Apache-2.0", candidate.License));
            Assert.All(result.Candidates, candidate => Assert.Equal(manifest.SourceUrl, candidate.SourceUrl));
            Assert.Contains(result.Candidates, candidate => candidate.ReviewReason.Contains("short_term"));
            Assert.Contains(result.Candidates, candidate => candidate.ReviewReason.Contains("url_like"));
            Assert.Contains(result.Candidates, candidate => candidate.ReviewReason.Contains("spreadsheet_formula"));
            Assert.Contains(result.Candidates, candidate => candidate.ReviewReason.Contains("category_conflict"));
            Assert.Equal(1, result.Report.UnmappedTaggedTermCount);
            Assert.Equal(1, result.Report.DuplicateCount);
            Assert.Equal(1, result.Report.SpreadsheetFormulaTermCount);
            Assert.Equal(2, result.Report.Files.Count);
            Assert.All(result.Report.Files, file => Assert.Equal(64, file.Sha256.Length));
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    [Fact]
    public async Task Cli_NeutralizesSpreadsheetFormulasAndNeverOverwritesOutputs()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var inputPath = Path.Combine(temporaryRoot, "input.txt");
            var manifestPath = Path.Combine(temporaryRoot, "manifest.json");
            var outputPath = Path.Combine(temporaryRoot, "candidates.csv");
            var reportPath = Path.Combine(temporaryRoot, "report.json");
            await File.WriteAllTextAsync(inputPath, "=1+1\n", new UTF8Encoding(false));
            var manifest = CreateManifest(new LexiconFileSpec
            {
                Path = "input.txt",
                Format = "lines",
                LanguageCode = "mixed",
                CategoryCode = "review",
                MatchMode = "compact",
                ProposedAction = "audit",
                Severity = 3,
                ExpectedSha256 = await ComputeSha256Async(inputPath)
            });
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest),
                new UTF8Encoding(false));

            var firstExitCode = await CliProgram.Main(
                ["prepare", "--manifest", manifestPath, "--output", outputPath, "--report", reportPath]);
            var originalCsv = await File.ReadAllTextAsync(outputPath);
            var secondExitCode = await CliProgram.Main(
                ["prepare", "--manifest", manifestPath, "--output", outputPath, "--report", reportPath]);

            Assert.Equal(0, firstExitCode);
            Assert.Contains("'=1+1", originalCsv, StringComparison.Ordinal);
            Assert.Equal(1, secondExitCode);
            Assert.Equal(originalCsv, await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_RejectsInputHashMismatch()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var inputPath = Path.Combine(temporaryRoot, "input.txt");
            await File.WriteAllTextAsync(inputPath, "term\n", new UTF8Encoding(false));
            var manifest = CreateManifest(new LexiconFileSpec
            {
                Path = "input.txt",
                Format = "lines",
                LanguageCode = "zh",
                CategoryCode = "review",
                MatchMode = "compact",
                ProposedAction = "audit",
                Severity = 3,
                ExpectedSha256 = new string('0', 64)
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new LexiconPreparer().PrepareAsync(manifest, temporaryRoot, default));

            Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_RejectsDuplicateLanguageOrActionConflicts()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var inputPath = Path.Combine(temporaryRoot, "input.txt");
            await File.WriteAllTextAsync(inputPath, "duplicate term\n", new UTF8Encoding(false));
            var hash = await ComputeSha256Async(inputPath);
            var manifest = CreateManifest(
                new LexiconFileSpec
                {
                    Path = "input.txt",
                    Format = "lines",
                    LanguageCode = "zh",
                    CategoryCode = "review",
                    MatchMode = "compact",
                    ProposedAction = "audit",
                    Severity = 3,
                    ExpectedSha256 = hash
                },
                new LexiconFileSpec
                {
                    Path = "input.txt",
                    Format = "lines",
                    LanguageCode = "en",
                    CategoryCode = "review",
                    MatchMode = "compact",
                    ProposedAction = "block",
                    Severity = 4,
                    ExpectedSha256 = hash
                });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new LexiconPreparer().PrepareAsync(manifest, temporaryRoot, default));

            Assert.Contains("conflicting language or action", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryRoot, true);
        }
    }

    private static LexiconManifest CreateManifest(params LexiconFileSpec[] files)
    {
        return new LexiconManifest
        {
            SourceCode = "test-source",
            SourceVersion = "0123456789abcdef0123456789abcdef01234567",
            SourceUrl = "https://example.com/lexicon",
            License = "Apache-2.0",
            Files = [.. files]
        };
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"jokester-lexicon-tool-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
