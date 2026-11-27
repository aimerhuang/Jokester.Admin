namespace jokester.admin.Application.DTOs.Announcements;

public sealed class AdminAnnouncementQuery
{
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int? Status { get; init; }
    public string? Keyword { get; init; }
}

public class AdminAnnouncementListItem : AnnouncementListItem
{
    public int Status { get; set; }
    public int Revision { get; set; }
    public long CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public string? UpdatedByName { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AdminAnnouncementDetail : AdminAnnouncementListItem
{
    public string? Content { get; set; }
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
}

public abstract record AnnouncementContentRequest
{
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Category { get; init; }
    public string? Content { get; init; }
    public string? CoverUrl { get; init; }
    public string? VersionLabel { get; init; }
    public bool IsPinned { get; init; }
    public bool IsImportant { get; init; }
    public string? LinkUrl { get; init; }
    public string? LinkText { get; init; }
}

public sealed record CreateAnnouncementRequest : AnnouncementContentRequest
{
    public int? Status { get; init; }
}

public sealed record UpdateAnnouncementRequest : AnnouncementContentRequest
{
    public int? Revision { get; init; }
}

public sealed record AnnouncementStatusRequest(int? Status, int? Revision);
public sealed record AnnouncementCoverUpload(string Url);
