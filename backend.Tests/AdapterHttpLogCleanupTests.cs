using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AdapterHttpLogCleanupTests
{
    [Fact]
    public async Task CleanupAsync_RemovesOnlyLogsOlderThanConfiguredRetention()
    {
        var previous = Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS");
        Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS", "1");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            db.adapterHttpLogs.AddRange(
                CreateLog(DateTime.UtcNow.AddDays(-3), "old"),
                CreateLog(DateTime.UtcNow.AddHours(-12), "recent"));
            await db.SaveChangesAsync();

            await using var provider = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            await using var service = new AdapterHttpLogService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AdapterHttpLogService>.Instance);

            await service.CleanupAsync();

            var logs = await db.adapterHttpLogs.OrderBy(log => log.path).ToListAsync();
            Assert.Single(logs);
            Assert.Equal("recent", logs[0].path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS", previous);
        }
    }

    private static AdapterHttpLog CreateLog(DateTime createdAt, string path) => new()
    {
        createdAt = createdAt,
        storeId = 1,
        connectionId = 1,
        adapterKey = "yandex",
        method = "GET",
        url = $"https://example.test/{path}",
        path = path,
        statusCode = 200
    };
}
