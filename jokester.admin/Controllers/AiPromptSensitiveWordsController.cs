using jokester.admin.Application.Abstractions;
using jokester.admin.Application.DTOs.AiPromptFilter;
using jokester.admin.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace jokester.admin.Controllers;

[Authorize]
[Route("api/ai/prompt-sensitive-words")]
public sealed class AiPromptSensitiveWordsController(IAiPromptSensitiveWordService service) : BaseApiController
{
    [Permission("AiImage.SensitiveWord.View")]
    [HttpGet]
    public async Task<IActionResult> GetPage(
        [FromQuery] AiPromptSensitiveWordQuery query,
        CancellationToken cancellationToken)
    {
        return Success(await service.GetPageAsync(query, cancellationToken));
    }

    [Permission("AiImage.SensitiveWord.View")]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        return Success(await service.GetStatusAsync(cancellationToken));
    }

    [Permission("AiImage.SensitiveWord.Manage")]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] SaveAiPromptSensitiveWordRequest request,
        CancellationToken cancellationToken)
    {
        var id = await service.CreateAsync(request, cancellationToken);
        return Success(new { id });
    }

    [Permission("AiImage.SensitiveWord.Manage")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] SaveAiPromptSensitiveWordRequest request,
        CancellationToken cancellationToken)
    {
        await service.UpdateAsync(id, request, cancellationToken);
        return Success();
    }

    [Permission("AiImage.SensitiveWord.Manage")]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> UpdateStatus(
        long id,
        [FromBody] UpdateAiPromptSensitiveWordStatusRequest request,
        CancellationToken cancellationToken)
    {
        await service.UpdateStatusAsync(id, request, cancellationToken);
        return Success();
    }

    [Permission("AiImage.SensitiveWord.Manage")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return Success();
    }

    [Permission("AiImage.SensitiveWord.Test")]
    [HttpPost("test")]
    public async Task<IActionResult> Test(
        [FromBody] TestAiPromptFilterRequest request,
        CancellationToken cancellationToken)
    {
        return Success(await service.TestAsync(request, cancellationToken));
    }
}
