using jokester.admin.Application.DTOs.Menus;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IMenuService
{
    Task<IReadOnlyCollection<MenuTreeNodeDto>> GetTreeAsync(long? siteId, CancellationToken cancellationToken);

    Task<PagedResult<MenuListItemDto>> GetPageAsync(MenuQuery query, CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveMenuRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, SaveMenuRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task UpdateStatusAsync(long id, UpdateMenuStatusRequest request, CancellationToken cancellationToken);
}
