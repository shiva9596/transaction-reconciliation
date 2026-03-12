using TransactionReconciliation.Console.Domain.Models;

namespace TransactionReconciliation.Console.Services.Interfaces;

public interface ITransactionFeedClient
{
    Task<IReadOnlyList<IncomingTransactionDto>> GetTransactionsAsync(CancellationToken cancellationToken);
}