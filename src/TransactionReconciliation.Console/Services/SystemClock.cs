using TransactionReconciliation.Console.Services.Interfaces;

namespace TransactionReconciliation.Console.Services;

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}