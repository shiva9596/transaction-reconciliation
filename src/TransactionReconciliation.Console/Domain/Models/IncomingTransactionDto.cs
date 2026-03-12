namespace TransactionReconciliation.Console.Domain.Models;

public class IncomingTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
}