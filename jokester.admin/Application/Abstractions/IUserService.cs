using jokester.admin.Application.DTOs.Common;
using jokester.admin.Application.DTOs.Users;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IUserService
{
    Task<PagedResult<UserListItemDto>> GetPageAsync(PageQuery query, CancellationToken cancellationToken);

    Task<PagedResult<UserPointDetailDto>> GetPointDetailsAsync(long id, PageQuery query, CancellationToken cancellationToken);

    Task<UserPermissionTreeDto> GetPermissionTreeAsync(long id, long? siteId, CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveUserRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, UpdateUserInfoRequest request, CancellationToken cancellationToken);

    Task AssignMenusAsync(long id, AssignUserMenusRequest request, CancellationToken cancellationToken);

    Task UpdateNickNameAsync(long id, UpdateUserNickNameRequest request, CancellationToken cancellationToken);

    Task UpdatePasswordAsync(long id, UpdateUserPasswordRequest request, CancellationToken cancellationToken);

    Task<string> UploadAvatarAsync(long id, IFormFile file, CancellationToken cancellationToken);

    Task UpdateSignatureAsync(long id, UpdateUserSignatureRequest request, CancellationToken cancellationToken);

    Task UpdateStatusAsync(long id, UpdateUserStatusRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
