using jokester.admin.Application.DTOs.AiPromptFilter;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IAiPromptSensitiveWordService
{
    Task<PagedResult<AiPromptSensitiveWordDto>> GetPageAsync(AiPromptSensitiveWordQuery query, CancellationToken cancellationToken);

    Task<AiPromptFilterStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task<long> CreateAsync(SaveAiPromptSensitiveWordRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(long id, SaveAiPromptSensitiveWordRequest request, CancellationToken cancellationToken);

    Task UpdateStatusAsync(long id, UpdateAiPromptSensitiveWordStatusRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task<TestAiPromptFilterResponse> TestAsync(TestAiPromptFilterRequest request, CancellationToken cancellationToken);
}
