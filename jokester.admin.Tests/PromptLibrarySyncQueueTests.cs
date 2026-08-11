using jokester.admin.Application.Abstractions;
using jokester.admin.Application.Services;

namespace jokester.admin.Tests;

public sealed class PromptLibrarySyncQueueTests
{
    [Fact]
    public async Task Queue_AllowsOnlyOneQueuedOrRunningSynchronization()
    {
        var queue = new PromptLibrarySyncQueue();

        Assert.True(queue.TryEnqueue(PromptLibrarySyncTrigger.Manual));
        Assert.True(queue.IsQueued);
        Assert.False(queue.TryEnqueue(PromptLibrarySyncTrigger.Scheduled));
        Assert.False(queue.TryBeginSnapshotSwitch());

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.DequeueAllAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(PromptLibrarySyncTrigger.Manual, reader.Current);
        Assert.True(queue.IsRunning);
        Assert.False(queue.TryEnqueue(PromptLibrarySyncTrigger.Scheduled));
        Assert.False(queue.TryBeginSnapshotSwitch());

        queue.MarkCompleted();
        Assert.False(queue.IsRunning);
        Assert.True(queue.TryEnqueue(PromptLibrarySyncTrigger.Scheduled));
    }

    [Fact]
    public void SnapshotSwitchLease_IsMutuallyExclusiveWithSynchronization()
    {
        var queue = new PromptLibrarySyncQueue();

        Assert.True(queue.TryBeginSnapshotSwitch());
        Assert.True(queue.IsSwitchingSnapshot);
        Assert.False(queue.TryBeginSnapshotSwitch());
        Assert.False(queue.TryEnqueue(PromptLibrarySyncTrigger.Manual));

        queue.EndSnapshotSwitch();

        Assert.False(queue.IsSwitchingSnapshot);
        Assert.True(queue.TryEnqueue(PromptLibrarySyncTrigger.Manual));
        Assert.False(queue.TryBeginSnapshotSwitch());
    }
}
