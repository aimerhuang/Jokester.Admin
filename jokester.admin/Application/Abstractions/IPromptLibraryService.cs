using jokester.admin.Application.DTOs.Prompts;
using jokester.admin.Common;

namespace jokester.admin.Application.Abstractions;

public interface IPromptLibraryService
{
    Task<PagedResult<PromptLibraryListItemDto>> GetPageAsync(
        PromptLibraryQuery query,
        CancellationToken cancellationToken);

    Task<PromptLibraryDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<RecordPromptEventResponse> RecordEventAsync(
        long promptId,
        RecordPromptEventRequest request,
        CancellationToken cancellationToken);
}
