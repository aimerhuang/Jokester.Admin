using jokester.admin.Application.DTOs.Roles;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IRoleService
{
    Task<PagedResult<RoleDto>> GetPageAsync(RoleQuery query, CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, SaveRoleRequest request, CancellationToken cancellationToken);

    Task AssignMenusAsync(long id, AssignRoleMenusRequest request, CancellationToken cancellationToken);

    Task UpdateStatusAsync(long id, UpdateRoleStatusRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
