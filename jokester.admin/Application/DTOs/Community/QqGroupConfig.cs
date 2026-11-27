using System.ComponentModel.DataAnnotations;

namespace jokester.admin.Application.DTOs.Community;

public sealed record QqGroupConfig(bool Enabled, string? GroupName = null, string? GroupNumber = null,
    string? JoinUrl = null, string? Title = null, string? Description = null, int? RemindIntervalDays = null,
    int? PromptVersion = null);

public sealed record AdminQqGroupConfig(bool Enabled, string GroupName, string GroupNumber, string? JoinUrl,
    string Title, string Description, int RemindIntervalDays, int PromptVersion, int Revision,
    long? UpdatedBy, string? UpdatedByName, DateTime? UpdatedAt);

public sealed class SaveQqGroupConfig
{
    [Required] public bool? Enabled { get; init; }
    public string? GroupName { get; init; }
    public string? GroupNumber { get; init; }
    public string? JoinUrl { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    [Required] public int? RemindIntervalDays { get; init; }
    [Required] public int? Revision { get; init; }
    public bool BumpPromptVersion { get; init; }
}
