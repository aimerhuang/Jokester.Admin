using jokester.admin.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace jokester.admin.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/dev/bootstrap")]
public sealed class DevBootstrapController(
    IAdminBootstrapService adminBootstrapService,
    IWebHostEnvironment environment,
    IConfiguration configuration) : BaseApiController
{
    private const string BootstrapSecretHeader = "X-Bootstrap-Secret";

    [HttpPost("super-admin")]
    public async Task<IActionResult> UpsertSuperAdmin(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var expectedSecret = configuration["BootstrapAdmin:Secret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            return StatusCode(503, "BootstrapAdmin:Secret is not configured.");
        }

        if (!Request.Headers.TryGetValue(BootstrapSecretHeader, out var providedSecret)
            || !string.Equals(expectedSecret, providedSecret, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        var userName = configuration["BootstrapAdmin:UserName"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Missing BootstrapAdmin configuration for development bootstrap.");
        }

        var id = await adminBootstrapService.UpsertSuperAdminAsync(userName, password, cancellationToken);
        return Success(new { id, userName });
    }
}
