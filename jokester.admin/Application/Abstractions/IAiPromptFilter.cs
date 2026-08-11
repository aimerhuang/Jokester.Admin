using jokester.admin.Application.Models.AiPromptFilter;

namespace jokester.admin.Application.Abstractions;

public interface IAiPromptFilter
{
    long CurrentRevision { get; }

    Task EnsureLoadedAsync(CancellationToken cancellationToken);

    Task<AiPromptFilterResult> CheckAsync(string? text, CancellationToken cancellationToken);

    Task<AiPromptFilterResult> EnsureAllowedAsync(string? text, string fieldName, CancellationToken cancellationToken);

    Task<long> EnsureAllAllowedAsync(IReadOnlyList<AiPromptFilterText> texts, CancellationToken cancellationToken);

    Task<long> RefreshAsync(bool force, bool publish, CancellationToken cancellationToken);
}
