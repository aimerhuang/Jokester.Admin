using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using jokester.admin.Presentation;

namespace jokester.admin.Application.DTOs.Users;

public class AdminPageQuery
{
    [Range(1, int.MaxValue)] public int PageIndex { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
}

public sealed class AdminUserQuery : AdminPageQuery
{
    [MaxLength(100)] public string? Keyword { get; init; }
    [Range(0, 1)] public int? Status { get; init; }
    public bool? IsSuperAdmin { get; init; }
    public string SortBy { get; init; } = "createdAt";
    public string SortOrder { get; init; } = "desc";
}

public class AdminPointRange : AdminPageQuery
{
    [ModelBinder(BinderType = typeof(OffsetTimestampBinder))]
    public DateTimeOffset? From { get; init; }
    [ModelBinder(BinderType = typeof(OffsetTimestampBinder))]
    public DateTimeOffset? To { get; init; }
}

public sealed class AdminPointQuery : AdminPointRange
{
    public string[] Source { get; init; } = [];
    public string? Direction { get; init; }
}

public sealed record AdminMembership(string TierCode, DateTime ExpiresAt);
public record AdminUserProfile
{
    public long Id { get; init; }
    public required string UserName { get; init; }
    public string? NickName { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public int Status { get; init; }
    public bool IsSuperAdmin { get; init; }
    public AdminMembership? Membership { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginTime { get; set; }
}
public sealed record AdminUserListItem : AdminUserProfile
{
    public int PointBalance { get; init; }
}
public sealed record AdminUserDetail : AdminUserProfile
{
    public string? Phone { get; init; }
    public string? Signature { get; init; }
    public string? Remark { get; init; }
    public string? LastLoginIp { get; init; }
    public AdminPoints Points { get; set; } = null!;
}
public sealed record AdminPoints(int AvailablePoints, int PermanentPoints, int ExpiringPoints, int NextExpiringPoints, DateTime? NextExpireAt);
public sealed record AdminSourceTotal(string Source, int Count, long TotalPoints);
public sealed record AdminPointSummary(DateTime? From, DateTime? To, long EarnedPoints, long ImageSpentPoints,
    long ExpiredPoints, long ClawedBackPoints, int ImageTaskCount, IReadOnlyList<AdminSourceTotal> BySource);
public sealed record AdminStatusRequest([property: System.Text.Json.Serialization.JsonRequired] int Status, string? Reason);
public sealed record AdminStatusResult(long Id, int Status);
public sealed record AdminStatusAudit(long Id, string Action, string? Reason, long OperatorUserId, string? OperatorUserName, DateTime CreatedAt);
