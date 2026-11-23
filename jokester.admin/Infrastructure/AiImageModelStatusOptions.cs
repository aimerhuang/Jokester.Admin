namespace jokester.admin.Infrastructure;

/// <summary>
/// Display parameters for the public model status page. These are not secrets, so they have code defaults.
/// </summary>
public sealed class AiImageModelStatusOptions
{
    public const string SectionName = "AiImageModelStatus";

    public int SampleLimit { get; set; } = 60;

    public int WindowHours { get; set; } = 24;

    public int MinSampleCount { get; set; } = 5;

    public int NormalThresholdPercent { get; set; } = 90;

    public int DegradedThresholdPercent { get; set; } = 70;

    public int RefreshSeconds { get; set; } = 30;
}
