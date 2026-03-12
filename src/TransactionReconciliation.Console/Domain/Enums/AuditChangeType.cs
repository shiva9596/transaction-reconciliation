namespace TransactionReconciliation.Console.Domain.Enums;

public enum AuditChangeType
{
    Insert = 1,
    Update = 2,
    Revoked = 3,
    Finalized = 4
}