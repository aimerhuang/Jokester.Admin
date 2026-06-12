using jokester.admin.Application.DTOs.Logs;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface ILogService
{
    Task<PagedResult<LoginLogDto>> GetLoginLogsAsync(LoginLogQuery query, CancellationToken cancellationToken);

    Task<PagedResult<OperationLogDto>> GetOperationLogsAsync(OperationLogQuery query, CancellationToken cancellationToken);

    Task DeleteLoginLogsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    Task DeleteOperationLogsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);
}
