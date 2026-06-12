namespace jokester.admin.Application.Abstractions;

public interface IPermissionService
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(long userId, bool isSuperAdmin, CancellationToken cancellationToken);
}
