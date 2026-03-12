namespace TransactionReconciliation.Console.Services.Interfaces;

public interface IClock
{
    DateTime UtcNow { get; }
}