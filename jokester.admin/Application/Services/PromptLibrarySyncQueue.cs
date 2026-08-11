using System.Threading.Channels;
using jokester.admin.Application.Abstractions;

namespace jokester.admin.Application.Services;

public sealed class PromptLibrarySyncQueue : IPromptLibrarySyncQueue
{
    private readonly Channel<PromptLibrarySyncTrigger> _channel = Channel.CreateBounded<PromptLibrarySyncTrigger>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    private int _state;

    public bool IsQueued => Volatile.Read(ref _state) == 1;

    public bool IsRunning => Volatile.Read(ref _state) == 2;

    public bool IsSwitchingSnapshot => Volatile.Read(ref _state) == 3;

    public bool TryEnqueue(PromptLibrarySyncTrigger trigger)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return false;
        }

        if (_channel.Writer.TryWrite(trigger))
        {
            return true;
        }

        Volatile.Write(ref _state, 0);
        return false;
    }

    public async IAsyncEnumerable<PromptLibrarySyncTrigger> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var trigger in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
            {
                throw new InvalidOperationException("Prompt sync queue state is inconsistent.");
            }
            yield return trigger;
        }
    }

    public void MarkStarted() => Interlocked.CompareExchange(ref _state, 2, 1);

    public void MarkCompleted() => Volatile.Write(ref _state, 0);

    public bool TryBeginSnapshotSwitch() => Interlocked.CompareExchange(ref _state, 3, 0) == 0;

    public void EndSnapshotSwitch()
    {
        if (Interlocked.CompareExchange(ref _state, 0, 3) != 3)
        {
            throw new InvalidOperationException("Prompt snapshot switch state is inconsistent.");
        }
    }
}
