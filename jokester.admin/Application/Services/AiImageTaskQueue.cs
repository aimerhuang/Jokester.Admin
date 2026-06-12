using System.Threading.Channels;
using jokester.admin.Application.Abstractions;

namespace jokester.admin.Application.Services;

public sealed class AiImageTaskQueue : IAiImageTaskQueue
{
    private const int Capacity = 1000;

    private readonly Channel<long> channel = Channel.CreateBounded<long>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public bool TryQueue(long taskId) => channel.Writer.TryWrite(taskId);

    public IAsyncEnumerable<long> DequeueAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
