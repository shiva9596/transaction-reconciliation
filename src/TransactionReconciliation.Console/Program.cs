using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TransactionReconciliation.Console.Configuration;
using TransactionReconciliation.Console.Data;
using TransactionReconciliation.Console.Services;
using TransactionReconciliation.Console.Services.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

builder.Services.Configure<FeedOptions>(builder.Configuration.GetSection("TransactionFeed"));
builder.Services.Configure<ProcessingOptions>(builder.Configuration.GetSection("Processing"));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ITransactionFeedClient, MockTransactionFeedClient>();
builder.Services.AddScoped<ICardDataProtector, CardDataProtector>();
builder.Services.AddScoped<IClock, SystemClock>();
builder.Services.AddScoped<IReconciliationService, ReconciliationService>();

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
});

using var host = builder.Build();

using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;

var dbContext = services.GetRequiredService<AppDbContext>();
await DbInitializer.InitializeAsync(dbContext);

var reconciliationService = services.GetRequiredService<IReconciliationService>();
await reconciliationService.ProcessAsync(CancellationToken.None);