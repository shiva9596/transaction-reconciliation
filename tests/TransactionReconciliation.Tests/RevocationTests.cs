using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransactionReconciliation.Console.Configuration;
using TransactionReconciliation.Console.Domain.Enums;
using TransactionReconciliation.Console.Domain.Models;
using TransactionReconciliation.Console.Services;
using TransactionReconciliation.Tests.TestHelpers;

namespace TransactionReconciliation.Tests;

public class RevocationTests
{
    [Fact]
    public async Task ProcessAsync_ShouldRevokeMissingInWindowTransaction()
    {
        var (dbContext, connection) = SqliteTestDbHelper.CreateContext();

        try
        {
            var testClock = new TestClock
            {
                UtcNow = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc)
            };

            var initialFeed = new FakeTransactionFeedClient(new List<IncomingTransactionDto>
            {
                new()
                {
                    TransactionId = "TXN-3001",
                    CardNumber = "4111111111111111",
                    LocationCode = "STORE-100",
                    ProductName = "Fuel-Regular",
                    Amount = 32.00m,
                    Timestamp = new DateTime(2026, 3, 12, 8, 0, 0, DateTimeKind.Utc)
                }
            });

            var service = new ReconciliationService(
                dbContext,
                initialFeed,
                new CardDataProtector(),
                Options.Create(new ProcessingOptions
                {
                    EnableFinalization = true,
                    LookbackHours = 24
                }),
                testClock,
                NullLogger<ReconciliationService>.Instance);

            await service.ProcessAsync(CancellationToken.None);

            var emptyFeed = new FakeTransactionFeedClient(new List<IncomingTransactionDto>());

            testClock.UtcNow = testClock.UtcNow.AddHours(1);

            var secondRun = new ReconciliationService(
                dbContext,
                emptyFeed,
                new CardDataProtector(),
                Options.Create(new ProcessingOptions
                {
                    EnableFinalization = true,
                    LookbackHours = 24
                }),
                testClock,
                NullLogger<ReconciliationService>.Instance);

            await secondRun.ProcessAsync(CancellationToken.None);

            var transaction = dbContext.Transactions.Single();
            transaction.Status.Should().Be(TransactionStatus.Revoked);
            transaction.RevokedAtUtc.Should().NotBeNull();

            dbContext.TransactionAudits.Any(x => x.ChangeType == AuditChangeType.Revoked).Should().BeTrue();
        }
        finally
        {
            await connection.DisposeAsync();
            await dbContext.DisposeAsync();
        }
    }
}