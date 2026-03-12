using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TransactionReconciliation.Console.Configuration;
using TransactionReconciliation.Console.Domain.Enums;
using TransactionReconciliation.Console.Domain.Models;
using TransactionReconciliation.Console.Services;
using TransactionReconciliation.Console.Services.Interfaces;
using TransactionReconciliation.Tests.TestHelpers;

namespace TransactionReconciliation.Tests;

public class ReconciliationServiceTests
{
    [Fact]
    public async Task ProcessAsync_ShouldInsertNewTransactions_WhenTheyDoNotExist()
    {
        var (dbContext, connection) = SqliteTestDbHelper.CreateContext();

        try
        {
            var testClock = new TestClock
            {
                UtcNow = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc)
            };

            var feedClient = new FakeTransactionFeedClient(new List<IncomingTransactionDto>
            {
                new()
                {
                    TransactionId = "TXN-1001",
                    CardNumber = "4111111111111111",
                    LocationCode = "STORE-100",
                    ProductName = "Fuel-Regular",
                    Amount = 50.25m,
                    Timestamp = new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Utc)
                }
            });

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

            Assert.Equal(1, dbContext.Transactions.Count());
            Assert.Equal(1, dbContext.TransactionAudits.Count());

            var transaction = dbContext.Transactions.Single();
            Assert.Equal("TXN-1001", transaction.TransactionId);
            Assert.Equal(TransactionStatus.Active, transaction.Status);
            Assert.Equal(50.25m, transaction.Amount);
        }
        finally
        {
            await connection.DisposeAsync();
            await dbContext.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProcessAsync_ShouldUpdateExistingTransaction_WhenTrackedFieldsChange()
    {
        var (dbContext, connection) = SqliteTestDbHelper.CreateContext();

        try
        {
            var initialTime = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc);

            var firstFeed = new FakeTransactionFeedClient(new List<IncomingTransactionDto>
            {
                new()
                {
                    TransactionId = "TXN-2001",
                    CardNumber = "4111111111111111",
                    LocationCode = "STORE-100",
                    ProductName = "Fuel-Regular",
                    Amount = 40.00m,
                    Timestamp = new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc)
                }
            });

            var testClock = new TestClock { UtcNow = initialTime };

            var service = new ReconciliationService(
                dbContext,
                firstFeed,
                new CardDataProtector(),
                Options.Create(new ProcessingOptions
                {
                    EnableFinalization = true,
                    LookbackHours = 24
                }),
                testClock,
                NullLogger<ReconciliationService>.Instance);

            await service.ProcessAsync(CancellationToken.None);

            var secondFeed = new FakeTransactionFeedClient(new List<IncomingTransactionDto>
            {
                new()
                {
                    TransactionId = "TXN-2001",
                    CardNumber = "4111111111111111",
                    LocationCode = "STORE-100",
                    ProductName = "Fuel-Premium",
                    Amount = 60.00m,
                    Timestamp = new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc)
                }
            });

            testClock.UtcNow = initialTime.AddHours(1);

            var updatedService = new ReconciliationService(
                dbContext,
                secondFeed,
                new CardDataProtector(),
                Options.Create(new ProcessingOptions
                {
                    EnableFinalization = true,
                    LookbackHours = 24
                }),
                testClock,
                NullLogger<ReconciliationService>.Instance);

            await updatedService.ProcessAsync(CancellationToken.None);

            var transaction = dbContext.Transactions.Single();
            Assert.Equal("Fuel-Premium", transaction.ProductName);
            Assert.Equal(60.00m, transaction.Amount);
            Assert.Equal(3, dbContext.TransactionAudits.Count());
        }
        finally
        {
            await connection.DisposeAsync();
            await dbContext.DisposeAsync();
        }
    }
}

internal class FakeTransactionFeedClient : ITransactionFeedClient
{
    private readonly IReadOnlyList<IncomingTransactionDto> _transactions;

    public FakeTransactionFeedClient(IReadOnlyList<IncomingTransactionDto> transactions)
    {
        _transactions = transactions;
    }

    public Task<IReadOnlyList<IncomingTransactionDto>> GetTransactionsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_transactions);
    }
}