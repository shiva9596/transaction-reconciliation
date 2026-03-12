using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransactionReconciliation.Console.Configuration;
using TransactionReconciliation.Console.Domain.Models;
using TransactionReconciliation.Console.Services;
using TransactionReconciliation.Tests.TestHelpers;

namespace TransactionReconciliation.Tests;

public class IdempotencyTests
{
    [Fact]
    public async Task ProcessAsync_ShouldNotCreateDuplicateTransactionsOrAudits_WhenInputIsUnchanged()
    {
        var (dbContext, connection) = SqliteTestDbHelper.CreateContext();

        try
        {
            var transactionFeed = new List<IncomingTransactionDto>
            {
                new()
                {
                    TransactionId = "TXN-4001",
                    CardNumber = "4111111111111111",
                    LocationCode = "STORE-100",
                    ProductName = "Fuel-Regular",
                    Amount = 25.50m,
                    Timestamp = new DateTime(2026, 3, 12, 7, 0, 0, DateTimeKind.Utc)
                }
            };

            var feedClient = new FakeTransactionFeedClient(transactionFeed);

            var testClock = new TestClock
            {
                UtcNow = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc)
            };

            var service = new ReconciliationService(
                dbContext,
                feedClient,
                new CardDataProtector(),
                Options.Create(new ProcessingOptions
                {
                    EnableFinalization = true,
                    LookbackHours = 24
                }),
                testClock,
                NullLogger<ReconciliationService>.Instance);

            await service.ProcessAsync(CancellationToken.None);

            var transactionCountAfterFirstRun = dbContext.Transactions.Count();
            var auditCountAfterFirstRun = dbContext.TransactionAudits.Count();

            testClock.UtcNow = testClock.UtcNow.AddHours(1);

            var secondService = new ReconciliationService(
                dbContext,
                feedClient,
                new CardDataProtector(),
                Options.Create(new ProcessingOptions
                {
                    EnableFinalization = true,
                    LookbackHours = 24
                }),
                testClock,
                NullLogger<ReconciliationService>.Instance);

            await secondService.ProcessAsync(CancellationToken.None);

            Assert.Equal(transactionCountAfterFirstRun, dbContext.Transactions.Count());
            Assert.Equal(auditCountAfterFirstRun, dbContext.TransactionAudits.Count());
        }
        finally
        {
            await connection.DisposeAsync();
            await dbContext.DisposeAsync();
        }
    }
}