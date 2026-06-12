using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Logs;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/logs")]
public sealed class LogsController(ILogService logService) : BaseApiController
{
    /// <summary>
    /// 分页查询登录日志。
    /// </summary>
    [Permission("System.Log.Login.View")]
    [HttpGet("login")]
    public async Task<IActionResult> GetLoginLogs([FromQuery] LoginLogQuery query, CancellationToken cancellationToken)
    {
        var result = await logService.GetLoginLogsAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 分页查询操作日志。
    /// </summary>
    [Permission("System.Log.Operation.View")]
    [HttpGet("operation")]
    public async Task<IActionResult> GetOperationLogs([FromQuery] OperationLogQuery query, CancellationToken cancellationToken)
    {
        var result = await logService.GetOperationLogsAsync(query, cancellationToken);
        return Success(result);
    }

    /// <summary>
    /// 批量删除登录日志。
    /// </summary>
    [Permission("System.Log.Login.Delete")]
    [HttpDelete("login")]
    public async Task<IActionResult> DeleteLoginLogs([FromBody] DeleteLogsRequest request, CancellationToken cancellationToken)
    {
        await logService.DeleteLoginLogsAsync(request.Ids, cancellationToken);
        return Success();
    }

    /// <summary>
    /// 批量删除操作日志。
    /// </summary>
    [Permission("System.Log.Operation.Delete")]
    [HttpDelete("operation")]
    public async Task<IActionResult> DeleteOperationLogs([FromBody] DeleteLogsRequest request, CancellationToken cancellationToken)
    {
        await logService.DeleteOperationLogsAsync(request.Ids, cancellationToken);
        return Success();
    }
}
