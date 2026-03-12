namespace TransactionReconciliation.Console.Configuration;

public class ProcessingOptions
{
    public bool EnableFinalization { get; set; } = true;
    public int LookbackHours { get; set; } = 24;
}