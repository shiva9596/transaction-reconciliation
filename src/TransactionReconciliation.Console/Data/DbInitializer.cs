using Microsoft.EntityFrameworkCore;

namespace TransactionReconciliation.Console.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}