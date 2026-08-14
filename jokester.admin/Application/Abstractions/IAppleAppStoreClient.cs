namespace jokester.admin.Application.Abstractions;

public interface IAppleAppStoreClient
{
    Task<AppleTransactionLookupResult> GetTransactionAsync(
        string transactionId,
        string environment,
        CancellationToken cancellationToken);
}

public sealed record AppleTransactionLookupResult(string SignedTransactionInfo, string Environment);
