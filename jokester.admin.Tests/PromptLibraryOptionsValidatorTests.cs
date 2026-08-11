using jokester.admin.Infrastructure;

namespace jokester.admin.Tests;

public sealed class PromptLibraryOptionsValidatorTests
{
    private readonly PromptLibraryOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsEnabledProductionShape()
    {
        var options = CreateValidOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsRelativeStorageAndUnsafeImageHosts()
    {
        var options = CreateValidOptions();
        options.ImageRoot = "relative/prompt-images";
        options.ImageAllowedHosts = ["https://cms-assets.youmind.com/"];

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("absolute path", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("plain host names", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DoesNotRequireStorageWhenFeatureIsDisabled()
    {
        var result = _validator.Validate(null, new PromptLibraryOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }

    private static PromptLibraryOptions CreateValidOptions() => new()
    {
        Enabled = true,
        ImageRoot = Path.Combine(Path.GetTempPath(), "jokester-prompt-options-test"),
        SyncCron = "0 30 2,14 * * *"
    };
}
