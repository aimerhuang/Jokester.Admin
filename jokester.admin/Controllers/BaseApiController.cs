using jokester.admin.Common;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult Success<T>(T data, string message = "success")
    {
        return Ok(ApiResponse<T>.Success(data, message));
    }

    protected IActionResult Success(string message = "success")
    {
        return Ok(ApiResponse.Success(message));
    }
}
