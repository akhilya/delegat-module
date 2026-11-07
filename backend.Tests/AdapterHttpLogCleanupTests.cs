using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Tests;

public class AdapterHttpLogCleanupTests
{
    private sealed class StubLoggingSettings : ILoggingSettingsService
    {
        public bool IsErrorLoggingEnabled => true;
        public bool IsApiRequestLoggingEnabled => false;
        public bool IsAdapterHttpLoggingEnabled => true;
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LoggingSettingsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new LoggingSettingsDto(true, false, true, DateTime.UtcNow));
        public Task<LoggingSettingsDto> UpdateAsync(UpdateLoggingSettingsRequest request, CancellationToken ct = default) =>
            GetAsync(ct);
    }
    [Fact]
    public async Task CleanupAsync_RemovesOnlyOldLogsAcrossMultipleSmallBatches()
    {
        var previous = Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS");
        Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS", "1");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            db.adapterHttpLogs.AddRange(Enumerable.Range(0, 1_201)
                .Select(i => CreateLog(DateTime.UtcNow.AddDays(-3).AddSeconds(i), $"old-{i}"))
                .Append(CreateLog(DateTime.UtcNow.AddHours(-12), "recent")));
            await db.SaveChangesAsync();

            await using var provider = new ServiceCollection()
                .AddSingleton(db)
                .BuildServiceProvider();
            await using var service = new AdapterHttpLogService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AdapterHttpLogService>.Instance,
                new StubLoggingSettings());

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

    [Fact]
    public Task CleanupAsync_ExpiresPayloadsInBatchesButPreservesSearchableSummaries() =>
        WithServiceAsync(async (db, service) =>
        {
            var oldCreatedAt = DateTime.UtcNow.AddDays(-2);
            db.adapterHttpLogs.AddRange(Enumerable.Range(0, 1_201).Select(i =>
            {
                var row = CreateLog(oldCreatedAt, $"old-{i}");
                row.requestBody = row.responseBody = "large diagnostic body";
                row.requestHeadersJson = row.responseHeadersJson = "{}";
                row.errorMessage = "Diagnostic summary";
                row.statusCode = 500;
                return row;
            }));
            var recent = CreateLog(DateTime.UtcNow.AddMinutes(-10), "recent");
            recent.requestBody = recent.responseBody = "recent body";
            db.adapterHttpLogs.Add(recent);
            await db.SaveChangesAsync();

            await service.CleanupAsync();
            await service.CleanupAsync(); // Compaction is idempotent.

            Assert.Equal(1_202, await db.adapterHttpLogs.CountAsync());
            var old = await db.adapterHttpLogs.Where(l => l.statusCode == 500).ToListAsync();
            Assert.All(old, l =>
            {
                Assert.Null(l.requestBody);
                Assert.Null(l.responseBody);
                Assert.Null(l.requestHeadersJson);
                Assert.Null(l.responseHeadersJson);
                Assert.Equal("Diagnostic summary", l.errorMessage);
            });
            Assert.Equal("recent body", recent.responseBody);
            var (items, total, errors) = await service.QueryAsync(1, new() { Q = "old-", Limit = 10 });
            Assert.Equal(1_201, total);
            Assert.Equal(total, errors);
            Assert.Equal(10, items.Count);
        });

    [Fact]
    public Task CleanupAsync_BoundsRecentDetailsWithoutDeletingSummaries() =>
        WithServiceAsync(async (db, service) =>
        {
            var now = DateTime.UtcNow;
            db.adapterHttpLogs.AddRange(Enumerable.Range(0, AdapterHttpLogService.MaxDetailedRowsSafety + 3)
                .Select(i =>
                {
                    var row = CreateLog(now, $"log-{i}");
                    row.responseBody = "body";
                    return row;
                }));
            await db.SaveChangesAsync();

            await service.CleanupAsync();

            Assert.Equal(AdapterHttpLogService.MaxDetailedRowsSafety + 3, await db.adapterHttpLogs.CountAsync());
            Assert.Equal(AdapterHttpLogService.MaxDetailedRowsSafety,
                await db.adapterHttpLogs.CountAsync(l => l.responseBody != null));
            var first = await db.adapterHttpLogs.OrderBy(l => l.Id).Take(3).ToListAsync();
            Assert.All(first, l => Assert.Null(l.responseBody));
        });

    [Fact]
    public Task DetailAsync_ReturnsBoundedPreviewsAndOnlyRequestedDownloadBody() =>
        WithServiceAsync(async (db, service) =>
        {
            var row = CreateLog(DateTime.UtcNow, "large");
            row.requestBody = new string('q', 2 * 1024 * 1024);
            row.responseBody = "response";
            row.requestHeadersJson = new string('h', AdapterHttpLogService.MaxStoredHeadersChars + 100);
            db.adapterHttpLogs.Add(row);
            await db.SaveChangesAsync();

            var detail = await service.GetDetailAsync(1, row.Id);
            Assert.NotNull(detail);
            Assert.True(detail.DetailsAvailable);
            Assert.True(detail.RequestBodyTruncated);
            Assert.Equal(row.requestBody.Length, detail.RequestBodyLength);
            Assert.Equal(AdapterHttpLogService.MaxPreviewBodyChars + AdapterHttpLogService.TruncationMarker.Length,
                detail.RequestBody!.Length);
            Assert.Equal(AdapterHttpLogService.MaxStoredHeadersChars, detail.RequestHeadersJson!.Length);
            Assert.Equal("response", await service.GetStoredBodyAsync(1, row.Id, response: true));
            Assert.Equal(row.requestBody, await service.GetStoredBodyAsync(1, row.Id, response: false));
            Assert.Null(await service.GetDetailAsync(2, row.Id));
            Assert.Null(await service.GetStoredBodyAsync(2, row.Id, response: false));
        });

    [Fact]
    public Task ExpiredDetails_AreUnavailableEvenBeforeCleanup() =>
        WithServiceAsync(async (db, service) =>
        {
            var row = CreateLog(DateTime.UtcNow.AddDays(-2), "expired");
            row.requestBody = row.responseBody = "must not be returned";
            row.requestHeadersJson = row.responseHeadersJson = "{}";
            db.adapterHttpLogs.Add(row);
            await db.SaveChangesAsync();

            var detail = await service.GetDetailAsync(1, row.Id);
            Assert.NotNull(detail);
            Assert.False(detail.DetailsAvailable);
            Assert.Null(detail.RequestBody);
            Assert.Null(detail.ResponseBody);
            Assert.Null(detail.RequestHeadersJson);
            Assert.Null(detail.ResponseHeadersJson);
            Assert.Equal(0, detail.ResponseBodyLength);
            var error = await Assert.ThrowsAsync<ApiException>(() => service.GetStoredBodyAsync(1, row.Id, response: true));
            Assert.Equal("log_details_unavailable", error.Code);
            Assert.Equal("expired", detail.Path);
        });

    [Fact]
    public Task CompactedDetails_ReturnUnavailableInsteadOfAnEmptyDownload() =>
        WithServiceAsync(async (db, service) =>
        {
            var row = CreateLog(DateTime.UtcNow, "summary-only");
            db.adapterHttpLogs.Add(row);
            await db.SaveChangesAsync();
            var detail = await service.GetDetailAsync(1, row.Id);
            Assert.False(detail!.DetailsAvailable);
            var error = await Assert.ThrowsAsync<ApiException>(() => service.GetStoredBodyAsync(1, row.Id, response: false));
            Assert.Equal("log_details_unavailable", error.Code);
        });

    private static async Task WithServiceAsync(Func<DataContext, AdapterHttpLogService, Task> test)
    {
        var previousDays = Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS");
        var previousHours = Environment.GetEnvironmentVariable("ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS");
        Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS", "7");
        Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS", "24");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            await using var provider = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
            await using var service = new AdapterHttpLogService(
                provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AdapterHttpLogService>.Instance,
                new StubLoggingSettings());
            await test(db, service);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_RETENTION_DAYS", previousDays);
            Environment.SetEnvironmentVariable("ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS", previousHours);
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
