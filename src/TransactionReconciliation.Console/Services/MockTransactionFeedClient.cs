using System.Text.Json;
using Microsoft.Extensions.Options;
using TransactionReconciliation.Console.Configuration;
using TransactionReconciliation.Console.Domain.Models;
using TransactionReconciliation.Console.Services.Interfaces;

namespace TransactionReconciliation.Console.Services;

public class MockTransactionFeedClient : ITransactionFeedClient
{
    private readonly FeedOptions _feedOptions;

    public MockTransactionFeedClient(IOptions<FeedOptions> feedOptions)
    {
        _feedOptions = feedOptions.Value;
    }

    public async Task<IReadOnlyList<IncomingTransactionDto>> GetTransactionsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_feedOptions.JsonFilePath))
            throw new InvalidOperationException("Transaction feed JSON file path is not configured.");

        var resolvedPath = Path.IsPathRooted(_feedOptions.JsonFilePath)
            ? _feedOptions.JsonFilePath
            : Path.Combine(AppContext.BaseDirectory, _feedOptions.JsonFilePath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(
                $"Transaction feed file was not found at path '{resolvedPath}'.");

        await using var stream = File.OpenRead(resolvedPath);

        var transactions = await JsonSerializer.DeserializeAsync<List<IncomingTransactionDto>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        return transactions ?? new List<IncomingTransactionDto>();
    }
}