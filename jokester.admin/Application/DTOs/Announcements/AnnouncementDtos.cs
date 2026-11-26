namespace jokester.admin.Application.DTOs.Announcements;

public class AnnouncementListItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public string? VersionLabel { get; set; }
    public bool IsPinned { get; set; }
    public bool IsImportant { get; set; }
    public bool HasDetail { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class AnnouncementDetail : AnnouncementListItem
{
    public string? Content { get; set; }
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
}

public sealed record AnnouncementSnapshot(IReadOnlyList<AnnouncementListItem> Items, DateTime? LatestPublishedAt);

public sealed record AnnouncementFeed(int Total, int PageIndex, int PageSize, DateTime? LatestPublishedAt,
    IReadOnlyList<AnnouncementListItem> Items)
{
    public bool HasMore => (long)PageIndex * PageSize < Total;
}
