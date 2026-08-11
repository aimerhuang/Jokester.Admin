using jokester.admin.Application.DTOs.Prompts;

namespace jokester.admin.Application.Abstractions;

public interface IPromptLibrarySyncAdminService
{
    Task<PromptLibrarySyncStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    QueuePromptLibrarySyncResponse QueueRun();

    Task<SwitchPromptLibrarySnapshotResponse> SwitchSnapshotAsync(
        long snapshotId,
        CancellationToken cancellationToken);
}
