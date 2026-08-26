namespace jokester.admin.Application.Abstractions;

public interface IAiImageProviderGate
{
    Task<IAiImageProviderLease> AcquireAsync(CancellationToken cancellationToken);

    Task ReportSuccessAsync();

    Task ReportFailureAsync();
}

public interface IAiImageProviderLease : IAsyncDisposable
{
    bool IsValid { get; }

    CancellationToken LeaseLostToken { get; }

    void ThrowIfLost();
}
