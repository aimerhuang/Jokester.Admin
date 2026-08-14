namespace jokester.admin.Common;

/// <summary>
/// 分页结果。
/// </summary>
public sealed class PagedResult<T>
{
    /// <summary>
    /// 总记录数。
    /// </summary>
    public long Total { get; init; }

    /// <summary>
    /// 当前页码。
    /// </summary>
    public int PageIndex { get; init; }

    /// <summary>
    /// 每页数据条数。
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// 当前页数据列表。
    /// </summary>
    public IReadOnlyCollection<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>
    /// 是否还有下一页。
    /// </summary>
    public bool HasMore => (long)PageIndex * PageSize < Total;
}
