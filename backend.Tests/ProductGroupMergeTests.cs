using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductGroupMergeTests
{
    [Fact]
    public async Task Merge_MovesAllMembersIncludingTrashAndKeepsTargetMetadataAndOrder()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var a = data.AddProduct(store.Id);
        var b = data.AddProduct(store.Id);
        var trashed = data.AddProduct(store.Id, deletedAt: DateTime.UtcNow);
        var target = data.AddGroup(store.Id, "Keep", a);
        target.description = "Keep description";
        var source = data.AddGroup(store.Id, "Remove", b, trashed);
        var untouched = data.AddGroup(store.Id, "Other");
        await db.SaveChangesAsync();
        var sourceMember = await db.productGroupMembers.SingleAsync(m => m.productId == b.Id);
        sourceMember.isVariantLeader = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await ProductGroupMerge.MergeAsync(db, store.Id, target.Id, [source.Id]);

        Assert.Equal(2, result.movedProductCount);
        Assert.Equal(1, result.mergedGroupCount);
        Assert.False(await db.productGroups.AnyAsync(g => g.Id == source.Id));
        Assert.True(await db.productGroups.AnyAsync(g => g.Id == untouched.Id));
        target = await db.productGroups.SingleAsync(g => g.Id == target.Id);
        Assert.Equal("Keep", target.name);
        Assert.Equal("Keep description", target.description);
        Assert.NotNull(target.updatedAt);
        var members = await db.productGroupMembers.OrderBy(m => m.sortOrder).ToListAsync();
        Assert.Equal(new[] { a.Id, b.Id, trashed.Id }, members.Select(m => m.productId));
        Assert.All(members, m => Assert.Equal(target.Id, m.groupId));
        Assert.Single(members, m => m.isVariantLeader);
        Assert.True(members.Single(m => m.productId == b.Id).isVariantLeader);
        Assert.Equal(3, await db.products.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Merge_DeduplicatesMembersAndPreservesTargetLeader()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var a = data.AddProduct(store.Id);
        var b = data.AddProduct(store.Id);
        var target = data.AddGroup(store.Id, "Target", a);
        var source = data.AddGroup(store.Id, "Source", a, b);
        await db.SaveChangesAsync();
        foreach (var member in await db.productGroupMembers.ToListAsync()) member.isVariantLeader = true;
        await db.SaveChangesAsync();

        var result = await ProductGroupMerge.MergeAsync(db, store.Id, target.Id, [source.Id, source.Id]);

        Assert.Equal(1, result.movedProductCount);
        Assert.Equal(2, await db.productGroupMembers.CountAsync());
        var leader = await db.productGroupMembers.SingleAsync(m => m.isVariantLeader);
        Assert.Equal(a.Id, leader.productId);
        Assert.Equal(target.Id, leader.groupId);
    }

    [Fact]
    public async Task Merge_PreservesCompatibleLinksWithoutChangingExternalIds()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var target = data.AddGroup(store.Id, "Target");
        var source = data.AddGroup(store.Id, "Source");
        var first = data.AddConnection(store.Id);
        var second = data.AddConnection(store.Id, "wildberries");
        db.productGroupMarketplaceLinks.AddRange(Link(target.Id, first.Id, "external-a"),
            Link(source.Id, second.Id, "external-b"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await ProductGroupMerge.MergeAsync(db, store.Id, target.Id, [source.Id]);

        var links = await db.productGroupMarketplaceLinks.OrderBy(l => l.connectionId).ToListAsync();
        Assert.Equal(new[] { "external-a", "external-b" }, links.Select(l => l.externalGroupId));
        Assert.All(links, l => Assert.Equal(target.Id, l.productGroupId));
        Assert.All(links, l => Assert.Equal(ProductMarketplaceStatus.Modified, l.status));
    }

    [Fact]
    public async Task Merge_ConflictingMarketplaceLinksDoesNotMutateAnything()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var target = data.AddGroup(store.Id, "Target", data.AddProduct(store.Id));
        var source = data.AddGroup(store.Id, "Source", data.AddProduct(store.Id));
        var connection = data.AddConnection(store.Id);
        db.productGroupMarketplaceLinks.AddRange(Link(target.Id, connection.Id, "a"), Link(source.Id, connection.Id, "b"));
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => ProductGroupMerge.MergeAsync(db, store.Id, target.Id, [source.Id]));

        Assert.Equal("group_merge_marketplace_conflict", error.Code);
        Assert.Equal(409, error.StatusCode);
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal(2, await db.productGroups.CountAsync());
        Assert.Equal(1, await db.productGroupMembers.CountAsync(m => m.groupId == source.Id));
        Assert.Equal(2, await db.productGroupMarketplaceLinks.CountAsync());
    }

    [Fact]
    public async Task Merge_RejectsCrossStoreSourcesAsAnAllOrNothingOperation()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var target = data.AddGroup(store.Id);
        var source = data.AddGroup(store.Id, "Source", data.AddProduct(store.Id));
        var foreign = data.AddGroup(data.AddStore().Id);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() => ProductGroupMerge.MergeAsync(db, store.Id, target.Id, [source.Id, foreign.Id]));

        Assert.Equal("group_not_found", error.Code);
        Assert.Equal(3, await db.productGroups.CountAsync());
        Assert.True(await db.productGroupMembers.AnyAsync(m => m.groupId == source.Id));
    }

    [Fact]
    public async Task Merge_RejectsForeignProductMembershipEvenInLegacyData()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var target = data.AddGroup(store.Id);
        var source = data.AddGroup(store.Id, "Corrupt", data.AddProduct(data.AddStore().Id));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ApiException>(() => ProductGroupMerge.MergeAsync(db, store.Id, target.Id, [source.Id]));
        Assert.Equal("group_merge_selection_invalid", error.Code);
        Assert.Equal(2, await db.productGroups.CountAsync());
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task Merge_RejectsInvalidSelection(int target, int source)
    {
        using var db = TestDbFactory.CreateDb();
        var error = await Assert.ThrowsAsync<ApiException>(() => ProductGroupMerge.MergeAsync(db, 1, target, [source]));
        Assert.Equal("group_merge_selection_invalid", error.Code);
    }

    [Fact]
    public async Task Merge_RejectsEmptyOrOversizedSelection()
    {
        using var db = TestDbFactory.CreateDb();
        foreach (var sources in new List<int>?[] { null, [], Enumerable.Range(2, 101).ToList() })
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => ProductGroupMerge.MergeAsync(db, 1, 1, sources));
            Assert.Equal("group_merge_selection_invalid", error.Code);
        }
    }

    [Fact]
    public void MergeEndpoint_RequiresCatalogManagementAndStoreMembership()
    {
        var attribute = Assert.Single(typeof(ProductGroupController).GetMethod(nameof(ProductGroupController.MergeGroups))!
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), false).Cast<RequiresPermissionAttribute>());
        Assert.Equal(PermissionPolicyProvider.StorePolicyName(Permissions.CatalogManage), attribute.Policy);
    }

    private static ProductGroupMarketplaceLink Link(int groupId, int connectionId, string externalId) => new()
    {
        productGroupId = groupId,
        connectionId = connectionId,
        externalGroupId = externalId,
        status = ProductMarketplaceStatus.Synced
    };
}
