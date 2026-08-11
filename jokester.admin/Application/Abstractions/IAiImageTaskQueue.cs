namespace jokester.admin.Application.Abstractions;

public interface IAiImageTaskQueue
{
    int Capacity { get; }

    int BacklogCount { get; }

    bool TryQueue(long taskId);

    IAsyncEnumerable<long> DequeueAllAsync(CancellationToken cancellationToken);
}
