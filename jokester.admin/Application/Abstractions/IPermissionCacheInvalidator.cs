namespace jokester.admin.Application.Abstractions;

public interface IPermissionCacheInvalidator
{
    Task RemoveUserAsync(long userId, CancellationToken cancellationToken);

    Task RemoveAllAsync(CancellationToken cancellationToken);
}
