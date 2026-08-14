using System.Text.Json;
using jokester.admin.Application.Models.AiPromptFilter;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace jokester.admin.Tests;

public sealed class AiPromptFilterPipelineTests
{
    [Fact]
    public async Task RejectedPrompt_ReturnsStableBusinessCodeWithoutMatchedTerm()
    {
        var result = new AiPromptFilterResult(
            false,
            9,
            new AiPromptFilterMatch(12, "private-rule-term", "en", "test", "word", "block", 5));
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new AiPromptRejectedException("prompt", result),
            NullLogger<GlobalExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(MachineErrorCodes.PromptBlocked, document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private-rule-term", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }
}
