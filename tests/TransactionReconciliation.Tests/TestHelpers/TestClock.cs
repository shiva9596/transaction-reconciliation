using TransactionReconciliation.Console.Services.Interfaces;

namespace TransactionReconciliation.Tests.TestHelpers;

public class TestClock : IClock
{
    public DateTime UtcNow { get; set; }
}