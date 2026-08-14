namespace jokester.admin.Application.DTOs.Common;

/// <summary>
/// 分页查询参数。
/// </summary>
public class PageQuery
{
    private int _pageIndex = 1;
    private int _pageSize = 20;

    /// <summary>
    /// 当前页码，从 1 开始。
    /// </summary>
    public int PageIndex
    {
        get => _pageIndex;
        init => _pageIndex = Math.Max(1, value);
    }

    /// <summary>
    /// 每页数据条数。
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, 100);
    }
}
