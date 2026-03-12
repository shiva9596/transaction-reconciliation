using TransactionReconciliation.Console.Domain.Entities;
using TransactionReconciliation.Console.Domain.Models;

namespace TransactionReconciliation.Console.Utilities;

public static class TransactionComparer
{
    public static IReadOnlyList<FieldChange> GetChanges(
        TransactionRecord existing,
        NormalizedTransaction incoming)
    {
        var changes = new List<FieldChange>();

        if (existing.CardHash != incoming.CardHash)
        {
            changes.Add(new FieldChange
            {
                FieldName = nameof(existing.CardHash),
                OldValue = existing.CardHash,
                NewValue = incoming.CardHash
            });
        }

        if (existing.CardLast4 != incoming.CardLast4)
        {
            changes.Add(new FieldChange
            {
                FieldName = nameof(existing.CardLast4),
                OldValue = existing.CardLast4,
                NewValue = incoming.CardLast4
            });
        }

        if (existing.LocationCode != incoming.LocationCode)
        {
            changes.Add(new FieldChange
            {
                FieldName = nameof(existing.LocationCode),
                OldValue = existing.LocationCode,
                NewValue = incoming.LocationCode
            });
        }

        if (existing.ProductName != incoming.ProductName)
        {
            changes.Add(new FieldChange
            {
                FieldName = nameof(existing.ProductName),
                OldValue = existing.ProductName,
                NewValue = incoming.ProductName
            });
        }

        if (existing.Amount != incoming.Amount)
        {
            changes.Add(new FieldChange
            {
                FieldName = nameof(existing.Amount),
                OldValue = existing.Amount.ToString("F2"),
                NewValue = incoming.Amount.ToString("F2")
            });
        }

        if (existing.TransactionTimeUtc != incoming.TransactionTimeUtc)
        {
            changes.Add(new FieldChange
            {
                FieldName = nameof(existing.TransactionTimeUtc),
                OldValue = existing.TransactionTimeUtc.ToString("O"),
                NewValue = incoming.TransactionTimeUtc.ToString("O")
            });
        }

        return changes;
    }
}