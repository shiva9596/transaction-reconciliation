using TransactionReconciliation.Console.Domain.Enums;

namespace TransactionReconciliation.Console.Domain.Entities;

public class TransactionAudit
{
    public long Id { get; set; }

    public string TransactionId { get; set; } = string.Empty;
    public AuditChangeType ChangeType { get; set; }

    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public string RunId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}