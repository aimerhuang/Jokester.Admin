namespace jokester.admin.Application.Abstractions;

public interface IAiImageTaskQueue
{
    bool TryQueue(long taskId);

    IAsyncEnumerable<long> DequeueAllAsync(CancellationToken cancellationToken);
}
