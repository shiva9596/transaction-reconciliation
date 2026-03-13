using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransactionReconciliation.Console.Configuration;
using TransactionReconciliation.Console.Data;
using TransactionReconciliation.Console.Domain.Entities;
using TransactionReconciliation.Console.Domain.Enums;
using TransactionReconciliation.Console.Domain.Models;
using TransactionReconciliation.Console.Services.Interfaces;
using TransactionReconciliation.Console.Utilities;
namespace TransactionReconciliation.Console.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly AppDbContext _dbContext;
    private readonly ITransactionFeedClient _feedClient;
    private readonly ICardDataProtector _cardDataProtector;
    private readonly ProcessingOptions _processingOptions;
    private readonly ILogger<ReconciliationService> _logger;
    private readonly IClock _clock;

    public ReconciliationService(
        AppDbContext dbContext,
        ITransactionFeedClient feedClient,
        ICardDataProtector cardDataProtector,
        IOptions<ProcessingOptions> processingOptions,
        IClock clock,
        ILogger<ReconciliationService> logger)
    {
        _dbContext = dbContext;
        _feedClient = feedClient;
        _cardDataProtector = cardDataProtector;
        _processingOptions = processingOptions.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var runTimeUtc = _clock.UtcNow;
        var cutoffUtc = runTimeUtc.AddHours(-_processingOptions.LookbackHours);

        _logger.LogInformation("Starting reconciliation run {RunId} at {RunTimeUtc}", runId, runTimeUtc);

        var incomingTransactions = await _feedClient.GetTransactionsAsync(cancellationToken);
        var normalizedTransactions = NormalizeTransactions(incomingTransactions);

        _logger.LogInformation("Loaded {Count} transactions from mocked feed", normalizedTransactions.Count);

        var incomingIds = normalizedTransactions
            .Select(x => x.TransactionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingTransactions = await _dbContext.Transactions
            .Where(x => incomingIds.Contains(x.TransactionId))
            .ToDictionaryAsync(x => x.TransactionId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var insertCount = 0;
        var updateCount = 0;
        var revokeCount = 0;
        var finalizeCount = 0;

        foreach (var incoming in normalizedTransactions)
        {
            if (!existingTransactions.TryGetValue(incoming.TransactionId, out var existing))
            {
                var newRecord = new TransactionRecord
                {
                    TransactionId = incoming.TransactionId,
                    CardHash = incoming.CardHash,
                    CardLast4 = incoming.CardLast4,
                    LocationCode = incoming.LocationCode,
                    ProductName = incoming.ProductName,
                    Amount = incoming.Amount,
                    TransactionTimeUtc = incoming.TransactionTimeUtc,
                    Status = TransactionStatus.Active,
                    CreatedAtUtc = runTimeUtc,
                    UpdatedAtUtc = runTimeUtc,
                    LastSeenAtUtc = runTimeUtc
                };

                _dbContext.Transactions.Add(newRecord);

                _dbContext.TransactionAudits.Add(new TransactionAudit
                {
                    TransactionId = newRecord.TransactionId,
                    ChangeType = AuditChangeType.Insert,
                    NewValue = "Inserted",
                    RunId = runId,
                    ChangedAtUtc = runTimeUtc
                });

                insertCount++;
                continue;
            }

            if (existing.Status == TransactionStatus.Finalized)
                continue;

            var changes = TransactionComparer.GetChanges(existing, incoming);

            if (changes.Count > 0)
            {
                foreach (var change in changes)
                {
                    _dbContext.TransactionAudits.Add(new TransactionAudit
                    {
                        TransactionId = existing.TransactionId,
                        ChangeType = AuditChangeType.Update,
                        FieldName = change.FieldName,
                        OldValue = change.OldValue,
                        NewValue = change.NewValue,
                        RunId = runId,
                        ChangedAtUtc = runTimeUtc
                    });
                }

                existing.CardHash = incoming.CardHash;
                existing.CardLast4 = incoming.CardLast4;
                existing.LocationCode = incoming.LocationCode;
                existing.ProductName = incoming.ProductName;
                existing.Amount = incoming.Amount;
                existing.TransactionTimeUtc = incoming.TransactionTimeUtc;
                existing.Status = TransactionStatus.Active;
                existing.UpdatedAtUtc = runTimeUtc;
                existing.LastSeenAtUtc = runTimeUtc;
                existing.RevokedAtUtc = null;

                updateCount++;
            }
            else
            {
                existing.LastSeenAtUtc = runTimeUtc;

                if (existing.Status == TransactionStatus.Revoked)
                {
                    existing.Status = TransactionStatus.Active;
                    existing.UpdatedAtUtc = runTimeUtc;
                    existing.RevokedAtUtc = null;

                    _dbContext.TransactionAudits.Add(new TransactionAudit
                    {
                        TransactionId = existing.TransactionId,
                        ChangeType = AuditChangeType.Update,
                        FieldName = "Status",
                        OldValue = "Revoked",
                        NewValue = "Active",
                        RunId = runId,
                        ChangedAtUtc = runTimeUtc
                    });
                }
            }
        }

        var inWindowStoredTransactions = await _dbContext.Transactions
            .Where(x => x.TransactionTimeUtc >= cutoffUtc && x.Status != TransactionStatus.Finalized)
            .ToListAsync(cancellationToken);

        foreach (var stored in inWindowStoredTransactions)
        {
            if (!incomingIds.Contains(stored.TransactionId) && stored.Status != TransactionStatus.Revoked)
            {
                stored.Status = TransactionStatus.Revoked;
                stored.RevokedAtUtc = runTimeUtc;
                stored.UpdatedAtUtc = runTimeUtc;

                _dbContext.TransactionAudits.Add(new TransactionAudit
                {
                    TransactionId = stored.TransactionId,
                    ChangeType = AuditChangeType.Revoked,
                    FieldName = "Status",
                    OldValue = "Active",
                    NewValue = "Revoked",
                    RunId = runId,
                    ChangedAtUtc = runTimeUtc
                });

                revokeCount++;
            }
        }

        if (_processingOptions.EnableFinalization)
        {
            var oldFromDb = await _dbContext.Transactions
                .Where(x => x.TransactionTimeUtc < cutoffUtc && x.Status != TransactionStatus.Finalized)
                .ToListAsync(cancellationToken);

            var newlyInsertedOld = _dbContext.ChangeTracker.Entries<TransactionRecord>()
                .Where(e => e.State == EntityState.Added
                         && e.Entity.TransactionTimeUtc < cutoffUtc
                         && e.Entity.Status != TransactionStatus.Finalized)
                .Select(e => e.Entity)
                .ToList();

            var oldTransactions = oldFromDb
                .Concat(newlyInsertedOld)
                .DistinctBy(x => x.TransactionId)
                .ToList();

            foreach (var oldTransaction in oldTransactions)
            {
                var previousStatus = oldTransaction.Status;

                oldTransaction.Status = TransactionStatus.Finalized;
                oldTransaction.FinalizedAtUtc = runTimeUtc;
                oldTransaction.UpdatedAtUtc = runTimeUtc;

                _dbContext.TransactionAudits.Add(new TransactionAudit
                {
                    TransactionId = oldTransaction.TransactionId,
                    ChangeType = AuditChangeType.Finalized,
                    FieldName = "Status",
                    OldValue = previousStatus.ToString(),
                    NewValue = "Finalized",
                    RunId = runId,
                    ChangedAtUtc = runTimeUtc
                });

                finalizeCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Completed reconciliation run {RunId}. Inserted: {InsertCount}, Updated: {UpdateCount}, Revoked: {RevokeCount}, Finalized: {FinalizeCount}",
            runId, insertCount, updateCount, revokeCount, finalizeCount);
    }

    private List<NormalizedTransaction> NormalizeTransactions(IReadOnlyList<IncomingTransactionDto> incomingTransactions)
    {
        return incomingTransactions
            .Where(x => !string.IsNullOrWhiteSpace(x.TransactionId))
            .Select(x => new NormalizedTransaction
            {
                TransactionId = x.TransactionId.Trim(),
                CardHash = _cardDataProtector.ComputeHash(x.CardNumber),
                CardLast4 = _cardDataProtector.GetLast4(x.CardNumber),
                LocationCode = x.LocationCode?.Trim() ?? string.Empty,
                ProductName = x.ProductName?.Trim() ?? string.Empty,
                Amount = x.Amount,
                TransactionTimeUtc = x.Timestamp.Kind == DateTimeKind.Utc
                    ? x.Timestamp
                    : DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc)
            })
            .GroupBy(x => x.TransactionId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
}