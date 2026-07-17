using Delegat.Enums;
using Delegat.Models.Marketplace;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class GroupMarketplaceLinkDomainTests
{
    private static ProductGroupMarketplaceLink NewLink(int groupId, int connectionId, string externalId = "group-1") => new()
    {
        productGroupId = groupId,
        connectionId = connectionId,
        externalGroupId = externalId,
        externalGroupName = "Marketplace group",
        status = ProductMarketplaceStatus.NotSynced
    };

    [Fact]
    public async Task GroupMarketplaceLink_Create_PersistsAssociation()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var group = data.AddGroup(store.Id);
        var connection = data.AddConnection(store.Id);
        db.productGroupMarketplaceLinks.Add(NewLink(group.Id, connection.Id));
        await data.SaveAsync();

        var link = db.productGroupMarketplaceLinks.Single();
        Assert.Equal(group.Id, link.productGroupId);
        Assert.Equal(connection.Id, link.connectionId);
        Assert.Equal("group-1", link.externalGroupId);
    }

    [Fact]
    public async Task GroupMarketplaceLink_AddFill_SetsInitialStatusAndFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var group = data.AddGroup(store.Id);
        var connection = data.AddConnection(store.Id);
        var link = new ProductGroupMarketplaceLink();

        Assert.True(link.AddFill(new ProductGroupMarketplaceLink.AddForm
        {
            productGroupId = group.Id, connectionId = connection.Id, externalGroupId = "external-2",
            externalGroupName = "External", status = ProductMarketplaceStatus.Synced
        }));
        db.productGroupMarketplaceLinks.Add(link);
        await data.SaveAsync();

        Assert.Equal(ProductMarketplaceStatus.Synced, db.productGroupMarketplaceLinks.Single().status);
    }

    [Fact]
    public async Task GroupMarketplaceLink_UpdateFill_UpdatesSyncState()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var link = NewLink(data.AddGroup(store.Id).Id, data.AddConnection(store.Id).Id);
        db.productGroupMarketplaceLinks.Add(link);
        await data.SaveAsync();
        var syncedAt = DateTime.UtcNow;

        link.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
        {
            status = ProductMarketplaceStatus.SyncError, lastErrorMessage = "timeout", lastSyncAt = syncedAt
        });
        await db.SaveChangesAsync();

        var saved = db.productGroupMarketplaceLinks.Single();
        Assert.Equal(ProductMarketplaceStatus.SyncError, saved.status);
        Assert.Equal("timeout", saved.lastErrorMessage);
        Assert.Equal(syncedAt, saved.lastSyncAt);
    }

    [Fact]
    public async Task GroupMarketplaceLink_UpdateFill_PreservesExternalIdWhenEmpty()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var link = NewLink(data.AddGroup(store.Id).Id, data.AddConnection(store.Id).Id, "keep");
        db.productGroupMarketplaceLinks.Add(link);
        await data.SaveAsync();

        link.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm { externalGroupId = "" });
        await db.SaveChangesAsync();

        Assert.Equal("keep", db.productGroupMarketplaceLinks.Single().externalGroupId);
    }

    [Fact]
    public async Task GroupMarketplaceLink_Unlink_RemovesOnlyTarget()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var group = data.AddGroup(store.Id);
        var first = NewLink(group.Id, data.AddConnection(store.Id).Id, "first");
        var second = NewLink(group.Id, data.AddConnection(store.Id).Id, "second");
        db.productGroupMarketplaceLinks.AddRange(first, second);
        await data.SaveAsync();

        db.productGroupMarketplaceLinks.Remove(first);
        await db.SaveChangesAsync();

        Assert.Single(db.productGroupMarketplaceLinks);
        Assert.Equal("second", db.productGroupMarketplaceLinks.Single().externalGroupId);
    }

    [Fact]
    public async Task GroupMarketplaceLink_IsolatedByConnection()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var group = data.AddGroup(store.Id);
        var firstConnection = data.AddConnection(store.Id, "ozon");
        var secondConnection = data.AddConnection(store.Id, "wb");
        db.productGroupMarketplaceLinks.AddRange(
            NewLink(group.Id, firstConnection.Id, "ozon-group"),
            NewLink(group.Id, secondConnection.Id, "wb-group"));
        await data.SaveAsync();

        var links = db.productGroupMarketplaceLinks.Where(l => l.connectionId == firstConnection.Id).ToList();
        Assert.Single(links);
        Assert.Equal("ozon-group", links[0].externalGroupId);
    }

    [Fact]
    public async Task GroupMarketplaceLink_IsolatedByStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        var groupA = data.AddGroup(a.Id);
        var groupB = data.AddGroup(b.Id);
        var connectionA = data.AddConnection(a.Id);
        var connectionB = data.AddConnection(b.Id);
        db.productGroupMarketplaceLinks.AddRange(NewLink(groupA.Id, connectionA.Id, "a"), NewLink(groupB.Id, connectionB.Id, "b"));
        await data.SaveAsync();

        var storeALinks = db.productGroupMarketplaceLinks.Where(l => l.connectionId == connectionA.Id).ToList();
        Assert.Single(storeALinks);
        Assert.Equal(groupA.Id, storeALinks[0].productGroupId);
    }

    [Theory]
    [InlineData(ProductMarketplaceStatus.NotSynced)]
    [InlineData(ProductMarketplaceStatus.Synced)]
    [InlineData(ProductMarketplaceStatus.SyncError)]
    public async Task GroupMarketplaceLink_Status_Persists(ProductMarketplaceStatus status)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var link = NewLink(data.AddGroup(store.Id).Id, data.AddConnection(store.Id).Id);
        link.status = status;
        db.productGroupMarketplaceLinks.Add(link);
        await data.SaveAsync();

        Assert.Equal(status, db.productGroupMarketplaceLinks.Single().status);
    }
}
