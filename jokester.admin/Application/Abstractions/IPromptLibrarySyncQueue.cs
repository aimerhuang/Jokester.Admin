namespace jokester.admin.Application.Abstractions;

public interface IPromptLibrarySyncQueue
{
    bool IsQueued { get; }

    bool IsRunning { get; }

    bool IsSwitchingSnapshot { get; }

    bool TryEnqueue(PromptLibrarySyncTrigger trigger);

    IAsyncEnumerable<PromptLibrarySyncTrigger> DequeueAllAsync(CancellationToken cancellationToken);

    void MarkStarted();

    void MarkCompleted();

    bool TryBeginSnapshotSwitch();

    void EndSnapshotSwitch();
}

public enum PromptLibrarySyncTrigger
{
    Startup,
    Scheduled,
    Manual
}
