using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Logs;
using jokester.admin.Common;
using jokester.admin.Domain.Entities;
using SqlSugar;

namespace jokester.admin.Application.Services;

public sealed class LogService(ISqlSugarClient db) : ILogService
{
    public async Task<PagedResult<LoginLogDto>> GetLoginLogsAsync(LoginLogQuery query, CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var loginStatus = query.LoginStatus;
        var dbQuery = db.Queryable<SysLoginLogEntity>()
            .WhereIF(!string.IsNullOrWhiteSpace(query.UserName), x => x.UserName!.Contains(query.UserName!))
            .WhereIF(!string.IsNullOrWhiteSpace(query.Ip), x => x.Ip != null && x.Ip.Contains(query.Ip!))
            .WhereIF(loginStatus.HasValue, x => x.LoginStatus == loginStatus!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc);

        var items = await dbQuery
            .Select(x => new LoginLogDto
            {
                Id = x.Id,
                UserId = x.UserId,
                UserName = x.UserName ?? string.Empty,
                Ip = x.Ip,
                UserAgent = x.UserAgent,
                LoginStatus = x.LoginStatus,
                ErrorMessage = x.ErrorMessage,
                CreatedAt = x.CreatedAt
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<LoginLogDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task<PagedResult<OperationLogDto>> GetOperationLogsAsync(OperationLogQuery query, CancellationToken cancellationToken)
    {
        RefAsync<int> total = 0;
        var userId = query.UserId;
        var dbQuery = db.Queryable<SysOperationLogEntity>()
            .WhereIF(!string.IsNullOrWhiteSpace(query.ModuleName), x => x.ModuleName!.Contains(query.ModuleName!))
            .WhereIF(!string.IsNullOrWhiteSpace(query.ActionName), x => x.ActionName!.Contains(query.ActionName!))
            .WhereIF(!string.IsNullOrWhiteSpace(query.RequestMethod), x => x.RequestMethod == query.RequestMethod)
            .WhereIF(userId.HasValue, x => x.UserId == userId!.Value)
            .OrderBy(x => x.Id, OrderByType.Desc);

        var items = await dbQuery
            .Select(x => new OperationLogDto
            {
                Id = x.Id,
                UserId = x.UserId,
                ModuleName = x.ModuleName,
                ActionName = x.ActionName,
                RequestMethod = x.RequestMethod,
                RequestUrl = x.RequestUrl,
                RequestData = x.RequestData,
                ResponseData = x.ResponseData,
                Ip = x.Ip,
                ExecutionMs = x.ExecutionMs,
                CreatedAt = x.CreatedAt
            })
            .ToPageListAsync(query.PageIndex, query.PageSize, total);

        return new PagedResult<OperationLogDto>
        {
            Total = total,
            PageIndex = query.PageIndex,
            PageSize = query.PageSize,
            Items = items
        };
    }

    public async Task DeleteLoginLogsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await db.Deleteable<SysLoginLogEntity>().Where(x => ids.Contains(x.Id)).ExecuteCommandAsync();
    }

    public async Task DeleteOperationLogsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await db.Deleteable<SysOperationLogEntity>().Where(x => ids.Contains(x.Id)).ExecuteCommandAsync();
    }
}
