using Delegat.Infrastructure.Configuration;
using Delegat.Models;
using Delegat.Services.PlatformLink;
using Delegat.Services.Plugins;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ReferenceCatalogServiceTests
{
    [Fact]
    public async Task GetCurrencies_EmptyDb_ReturnsEmpty()
    {
        var db = TestDbFactory.CreateDb();
        var svc = CreateService(db, linked: false);
        Assert.Empty(await svc.GetCurrenciesAsync());
    }

    [Fact]
    public async Task GetCurrencies_ReadsFromDb()
    {
        var db = TestDbFactory.CreateDb();
        db.referenceCurrencies.Add(new ReferenceCurrency
        {
            code = "USD",
            label = "US Dollar",
            symbol = "$",
            syncedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, linked: false);
        var items = await svc.GetCurrenciesAsync();
        Assert.Single(items);
    }

    [Fact]
    public async Task GetStatus_Ready_WhenCurrenciesAndTimezonesPresent()
    {
        var db = TestDbFactory.CreateDb();
        db.referenceCurrencies.Add(new ReferenceCurrency { code = "RUB", label = "Ruble", syncedAt = DateTime.UtcNow });
        db.referenceTimezones.Add(new ReferenceTimezone { id = "Europe/Moscow", label = "Moscow", syncedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc = CreateService(db, linked: true);
        var status = await svc.GetStatusAsync();
        Assert.True(status.Ready);
        Assert.True(status.Linked);
        Assert.Equal(1, status.CurrenciesCount);
        Assert.Equal(1, status.TimezonesCount);
    }

    [Fact]
    public async Task SyncFromControlPlane_UpsertsCurrencies()
    {
        var db = TestDbFactory.CreateDb();
        var fake = new FakePlatformLinkClient
        {
            Currencies =
            [
                new PlatformCurrencyDto { Code = "EUR", Label = "Euro", Symbol = "€" },
                new PlatformCurrencyDto { Code = "USD", Label = "US Dollar", Symbol = "$" },
            ],
        };
        var svc = CreateService(db, linked: true, client: fake);
        await svc.SyncFromControlPlaneAsync();

        Assert.Equal(2, await db.referenceCurrencies.CountAsync());
        Assert.Contains(db.referenceCurrencies, c => c.code == "EUR");
    }

    [Fact]
    public async Task SyncFromControlPlane_WhenDisabled_DoesNothing()
    {
        var db = TestDbFactory.CreateDb();
        var fake = new FakePlatformLinkClient
        {
            Enabled = false,
            Currencies = [new PlatformCurrencyDto { Code = "USD", Label = "USD" }],
        };
        var svc = CreateService(db, linked: false, client: fake);
        await svc.SyncFromControlPlaneAsync();
        Assert.Empty(db.referenceCurrencies);
    }

    private static ReferenceCatalogService CreateService(
        DataContext db,
        bool linked,
        IPlatformLinkClient? client = null)
    {
        var opts = linked
            ? new PlatformLinkOptions
            {
                ControlPlaneUrl = "https://cp.test",
                InstanceId = "inst_1",
                InstanceLinkSecret = "sekrit",
            }
            : new PlatformLinkOptions();

        client ??= new FakePlatformLinkClient { Enabled = linked };

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Plugins:Directory"] = Path.Combine(Path.GetTempPath(), "delegat-plugins-test-" + Guid.NewGuid()),
        }).Build();
        var plugins = new PluginManager(config, NullLogger<PluginManager>.Instance);

        return new ReferenceCatalogService(
            db,
            client,
            opts,
            plugins,
            NullLogger<ReferenceCatalogService>.Instance);
    }

    private sealed class FakePlatformLinkClient : IPlatformLinkClient
    {
        public bool Enabled { get; set; } = true;
        public List<PlatformCurrencyDto> Currencies { get; set; } = [];
        public List<PlatformTimezoneDto> Timezones { get; set; } = [];

        public bool IsEnabled => Enabled;
        public string? InstanceId => "inst_1";

        public Task<IReadOnlyList<PlatformCurrencyDto>> GetCurrenciesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlatformCurrencyDto>>(Currencies);

        public Task<IReadOnlyList<PlatformTimezoneDto>> GetTimezonesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlatformTimezoneDto>>(Timezones);

        public Task ReportErrorAsync(PlatformErrorReportDto report, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendHeartbeatAsync(PlatformHeartbeatDto heartbeat, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PlatformEntitlementsDto?> GetEntitlementsAsync(CancellationToken ct = default) =>
            Task.FromResult<PlatformEntitlementsDto?>(null);

        public Task<PlatformMarketplaceDocDto?> GetMarketplaceDocAsync(
            string adapter, string? query, CancellationToken ct = default) =>
            Task.FromResult<PlatformMarketplaceDocDto?>(null);
    }
}
