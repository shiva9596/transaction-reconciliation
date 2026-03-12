using TransactionReconciliation.Console.Domain.Enums;

namespace TransactionReconciliation.Console.Domain.Entities;

public class TransactionRecord
{
    public string TransactionId { get; set; } = string.Empty;

    public string? CardHash { get; set; }
    public string? CardLast4 { get; set; }

    public string LocationCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public DateTime TransactionTimeUtc { get; set; }

    public TransactionStatus Status { get; set; } = TransactionStatus.Active;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
}