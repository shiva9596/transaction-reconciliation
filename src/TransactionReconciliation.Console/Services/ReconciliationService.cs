namespace TransactionReconciliation.Console.Services.Interfaces;

public interface IReconciliationService
{
    Task ProcessAsync(CancellationToken cancellationToken);
}