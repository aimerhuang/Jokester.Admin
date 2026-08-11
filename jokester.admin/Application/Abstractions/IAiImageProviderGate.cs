namespace jokester.admin.Application.Abstractions;

public interface IAiImageProviderGate
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);

    Task ReportSuccessAsync();

    Task ReportFailureAsync();
}
