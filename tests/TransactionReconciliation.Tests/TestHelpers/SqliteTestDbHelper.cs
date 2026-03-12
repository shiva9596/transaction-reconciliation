using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransactionReconciliation.Console.Data;

namespace TransactionReconciliation.Tests.TestHelpers;

public static class SqliteTestDbHelper
{
    public static (AppDbContext DbContext, SqliteConnection Connection) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new AppDbContext(options);
        dbContext.Database.EnsureCreated();

        return (dbContext, connection);
    }
}