namespace TransactionReconciliation.Console.Domain.Models;

public class NormalizedTransaction
{
    public string TransactionId { get; set; } = string.Empty;

    public string? CardHash { get; set; }
    public string? CardLast4 { get; set; }

    public string LocationCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public DateTime TransactionTimeUtc { get; set; }
}