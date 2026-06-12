namespace jokester.admin.Application.Abstractions;

public interface IAiImageTaskProcessor
{
    Task ProcessAsync(long taskId, CancellationToken cancellationToken);
}
