using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class MarketplaceConnectionDomainTests
{
    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService currentUser) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db), new FakeCurrentUserService());
    }

    [Fact]
    public async Task AddConnection_PersistsOwningStoreAndAdapter()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon", "Main Ozon");
        await data.SaveAsync();

        var saved = await db.marketplaceConnections.SingleAsync();

        Assert.Equal(connection.Id, saved.Id);
        Assert.Equal(store.Id, saved.storeId);
        Assert.Equal("ozon", saved.adapterKey);
        Assert.Equal("Main Ozon", saved.name);
    }

    [Fact]
    public async Task AddConnection_StoresCredentialsJson()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        data.AddConnection(store.Id, credentialsJson: """{"clientId":"42","apiKey":"secret"}""");
        await data.SaveAsync();

        var saved = await db.marketplaceConnections.SingleAsync();

        Assert.Equal("""{"clientId":"42","apiKey":"secret"}""", saved.credentialsJson);
    }

    [Fact]
    public async Task ListProjection_DoesNotExposeCredentialsJson()
    {
        var (db, data, currentUser) = Create();
        var store = data.AddStore();
        currentUser.SelectedStoreId = store.Id;
        data.AddConnection(store.Id, credentialsJson: """{"apiKey":"secret"}""");
        await data.SaveAsync();

        var listItem = await db.marketplaceConnections
            .Where(c => c.storeId == currentUser.GetSelectedStoreId())
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .SingleAsync();

        Assert.DoesNotContain(nameof(MarketplaceConnection.credentialsJson),
            listItem.GetType().GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task DetailMapping_ContainsCredentialsForAuthorizedDetailUse()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, credentialsJson: """{"token":"detail-secret"}""");
        await data.SaveAsync();

        var detail = ((IMapToDTO<MarketplaceConnection.ConnectionDetailDTO>)connection)
            .MapToDTO(new ServiceCollection().BuildServiceProvider());

        Assert.Null(detail.credentialsJson);
        Assert.True(detail.hasCredentials);
    }

    [Fact]
    public async Task ListProjection_ReturnsOnlySelectedStoreConnections()
    {
        var (db, data, currentUser) = Create();
        var first = data.AddStore("First");
        var second = data.AddStore("Second");
        currentUser.SelectedStoreId = first.Id;
        var own = data.AddConnection(first.Id);
        data.AddConnection(second.Id);
        await data.SaveAsync();

        var connections = await db.marketplaceConnections
            .Where(c => c.storeId == currentUser.GetSelectedStoreId())
            .ToListAsync();

        Assert.Equal(new[] { own.Id }, connections.Select(c => c.Id));
    }

    [Fact]
    public async Task Connections_AreIsolatedByStoreId()
    {
        var (db, data, _) = Create();
        var first = data.AddStore();
        var second = data.AddStore();
        data.AddConnection(first.Id);
        data.AddConnection(second.Id);
        await data.SaveAsync();

        Assert.Equal(1, await db.marketplaceConnections.CountAsync(c => c.storeId == first.Id));
        Assert.Equal(1, await db.marketplaceConnections.CountAsync(c => c.storeId == second.Id));
    }

    [Fact]
    public async Task AddConnection_DefaultsToActive()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        data.AddConnection(store.Id);
        await data.SaveAsync();

        Assert.True((await db.marketplaceConnections.SingleAsync()).isActive);
    }

    [Fact]
    public async Task AddConnection_CanBeInactive()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        data.AddConnection(store.Id, isActive: false);
        await data.SaveAsync();

        Assert.False((await db.marketplaceConnections.SingleAsync()).isActive);
    }

    [Fact]
    public async Task ActiveConnectionFilter_ExcludesInactiveConnections()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var active = data.AddConnection(store.Id, "ozon", isActive: true);
        data.AddConnection(store.Id, "wildberries", isActive: false);
        await data.SaveAsync();

        var activeIds = await db.marketplaceConnections
            .Where(c => c.storeId == store.Id && c.isActive)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal(new[] { active.Id }, activeIds);
    }

    [Fact]
    public async Task UpdateConnection_TogglesInactiveFlag()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        await data.SaveAsync();

        connection.UpdateFill(new MarketplaceConnection.UpdateForm { isActive = false });
        await db.SaveChangesAsync();

        Assert.False((await db.marketplaceConnections.SingleAsync()).isActive);
    }

    [Fact]
    public async Task UpdateConnection_ChangesCredentialsWithoutChangingAdapter()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon", credentialsJson: """{"key":"old"}""");
        await data.SaveAsync();

        connection.UpdateFill(new MarketplaceConnection.UpdateForm { credentialsJson = """{"key":"new"}""" });
        await db.SaveChangesAsync();

        var saved = await db.marketplaceConnections.SingleAsync();
        Assert.Equal("ozon", saved.adapterKey);
        Assert.Equal("""{"key":"new"}""", saved.credentialsJson);
    }

    [Fact]
    public async Task ConnectionQuota_UnderLimitAllowsAnotherConnection()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_CONNECTIONS");
        Environment.SetEnvironmentVariable("MAX_CONNECTIONS", "2");
        try
        {
            var (db, data, _) = Create();
            var store = data.AddStore();
            data.AddConnection(store.Id);
            await data.SaveAsync();
            var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db));

            await quota.EnsureConnectionQuotaAsync(store.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CONNECTIONS", previous);
        }
    }

    [Fact]
    public async Task ConnectionQuota_AtLimitThrowsConflict()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_CONNECTIONS");
        Environment.SetEnvironmentVariable("MAX_CONNECTIONS", "1");
        try
        {
            var (db, data, _) = Create();
            var store = data.AddStore();
            data.AddConnection(store.Id);
            await data.SaveAsync();
            var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db));

            var error = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureConnectionQuotaAsync(store.Id));

            Assert.Equal(409, error.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CONNECTIONS", previous);
        }
    }

    [Fact]
    public async Task ConnectionQuota_DoesNotCountOtherStores()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_CONNECTIONS");
        Environment.SetEnvironmentVariable("MAX_CONNECTIONS", "1");
        try
        {
            var (db, data, _) = Create();
            var first = data.AddStore();
            var second = data.AddStore();
            data.AddConnection(second.Id);
            await data.SaveAsync();
            var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db));

            await quota.EnsureConnectionQuotaAsync(first.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CONNECTIONS", previous);
        }
    }

    [Fact]
    public async Task ConnectionQuota_NoPositiveLimitAllowsConnections()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_CONNECTIONS");
        Environment.SetEnvironmentVariable("MAX_CONNECTIONS", "0");
        try
        {
            var (db, data, _) = Create();
            var store = data.AddStore();
            data.AddConnection(store.Id);
            data.AddConnection(store.Id, "wildberries");
            await data.SaveAsync();
            var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db));

            await quota.EnsureConnectionQuotaAsync(store.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_CONNECTIONS", previous);
        }
    }
}
