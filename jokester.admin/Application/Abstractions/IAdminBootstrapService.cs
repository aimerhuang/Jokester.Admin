namespace jokester.admin.Application.Abstractions;

public interface IAdminBootstrapService
{
    Task<long> UpsertSuperAdminAsync(string userName, string password, CancellationToken cancellationToken);
}
