using System.Threading.Channels;
using System.Collections.Concurrent;
using jokester.admin.Application.Abstractions;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskQueue : IAiImageTaskQueue
{
    private const int QueueCapacity = 1000;
    private readonly ConcurrentDictionary<long, byte> queuedTaskIds = new();

    private readonly Channel<long> channel = Channel.CreateBounded<long>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public int Capacity => QueueCapacity;

    public int BacklogCount => queuedTaskIds.Count;

    public bool TryQueue(long taskId)
    {
        if (!queuedTaskIds.TryAdd(taskId, 0))
        {
            return true;
        }
        if (!channel.Writer.TryWrite(taskId))
        {
            queuedTaskIds.TryRemove(taskId, out _);
            return false;
        }
        return true;
    }

    public async IAsyncEnumerable<long> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var taskId in channel.Reader.ReadAllAsync(cancellationToken))
        {
            queuedTaskIds.TryRemove(taskId, out _);
            yield return taskId;
        }
    }
}
