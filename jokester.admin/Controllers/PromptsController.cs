using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.Prompts;
using jokester.admin.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[AllowAnonymous]
[Route("api/prompts")]
public sealed class PromptsController(IPromptLibraryService promptLibraryService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetPage(
        [FromQuery] PromptLibraryQuery query,
        CancellationToken cancellationToken)
    {
        var result = await promptLibraryService.GetPageAsync(query, cancellationToken);
        return Success(result);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await promptLibraryService.GetByIdAsync(id, cancellationToken);
        if (result is null)
        {
            throw new NotFoundException($"Prompt does not exist: {id}");
        }

        return Success(result);
    }

    [HttpPost("{id:long}/events")]
    public async Task<IActionResult> RecordEvent(
        long id,
        [FromBody] RecordPromptEventRequest request,
        CancellationToken cancellationToken)
    {
        var result = await promptLibraryService.RecordEventAsync(id, request, cancellationToken);
        return Success(result);
    }
}
