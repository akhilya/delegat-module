using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ProductGroupBulkDeleteTests
{
    private static ProductGroupController Controller(DataContext db, int? storeId) => new(
        new FakeCurrentUserService { SelectedStoreId = storeId },
        TestDbFactory.CreateUnitOfWork(db), db, null!, null!,
        NullLogger<ProductGroupController>.Instance);

    [Fact]
    public async Task DeletesSelectionInOneSave_WithoutDeletingProductsOrOtherGroups()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var otherStore = data.AddStore();
        var firstProduct = data.AddProduct(store.Id);
        var secondProduct = data.AddProduct(store.Id);
        var retainedProduct = data.AddProduct(store.Id);
        var first = data.AddGroup(store.Id, "First", firstProduct);
        var second = data.AddGroup(store.Id, "Second", secondProduct);
        var retained = data.AddGroup(store.Id, "Retained", retainedProduct);
        var foreign = data.AddGroup(otherStore.Id, "Foreign");
        var connection = data.AddConnection(store.Id);
        db.productGroupMarketplaceLinks.AddRange(
            Link(first.Id, connection.Id, "external-first"),
            Link(second.Id, connection.Id, "external-second"),
            Link(retained.Id, connection.Id, "external-retained"));
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        var saveCount = 0;
        db.SavingChanges += (_, _) => saveCount++;
        var result = Assert.IsType<OkObjectResult>(await Controller(db, store.Id).BulkDeleteGroups(
            new ProductGroupController.BulkDeleteGroupsRequest
            {
                groupIds = [first.Id, second.Id, first.Id]
            }));

        Assert.Equal(1, saveCount);
        Assert.Equal(2, (int)result.Value!.GetType().GetProperty("deletedCount")!.GetValue(result.Value)!);
        db.ChangeTracker.Clear();
        Assert.Equal(new[] { retained.Id, foreign.Id }, await db.productGroups
            .Select(group => group.Id).OrderBy(id => id).ToArrayAsync());
        Assert.Equal(retained.Id, (await db.productGroupMembers.SingleAsync()).groupId);
        Assert.Equal(retained.Id, (await db.productGroupMarketplaceLinks.SingleAsync()).productGroupId);
        Assert.Equal(3, await db.products.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrMissingId_DoesNotPartiallyDelete(bool foreign)
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var otherStore = data.AddStore();
        var product = data.AddProduct(store.Id);
        var ownGroup = data.AddGroup(store.Id, "Own", product);
        var otherGroup = data.AddGroup(otherStore.Id, "Other");
        var connection = data.AddConnection(store.Id);
        db.productGroupMarketplaceLinks.Add(Link(ownGroup.Id, connection.Id, "external-own"));
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<ApiException>(() => Controller(db, store.Id).BulkDeleteGroups(
            new ProductGroupController.BulkDeleteGroupsRequest
            {
                groupIds = [ownGroup.Id, foreign ? otherGroup.Id : 999999]
            }));

        Assert.Equal("group_not_found", error.Code);
        Assert.Equal(2, await db.productGroups.CountAsync());
        Assert.Single(await db.productGroupMembers.ToListAsync());
        Assert.Single(await db.productGroupMarketplaceLinks.ToListAsync());
    }

    [Fact]
    public async Task RejectsInvalidOversizedAndMissingStoreSelections()
    {
        using var db = TestDbFactory.CreateDb();
        foreach (var ids in new int[]?[] { [], [0], [-1], Enumerable.Range(1, 1001).ToArray(), null })
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => Controller(db, 1).BulkDeleteGroups(
                new ProductGroupController.BulkDeleteGroupsRequest { groupIds = ids }));
            Assert.Equal("groups_selection_invalid", error.Code);
        }

        Assert.Equal("store_not_selected", (await Assert.ThrowsAsync<ApiException>(() =>
            Controller(db, null).BulkDeleteGroups(new ProductGroupController.BulkDeleteGroupsRequest
            {
                groupIds = [1]
            }))).Code);
    }

    [Fact]
    public void RequiresCatalogWritePermissionAndStoreMembership()
    {
        var method = typeof(ProductGroupController).GetMethod(nameof(ProductGroupController.BulkDeleteGroups))!;
        var permission = Assert.Single(method
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), true)
            .Cast<RequiresPermissionAttribute>());
        Assert.Equal(PermissionPolicyProvider.StorePolicyName(Permissions.CatalogManage), permission.Policy);
        Assert.Contains(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>(),
            attribute => attribute.Template == "/productgroups/bulk-delete");
    }

    private static ProductGroupMarketplaceLink Link(int groupId, int connectionId, string externalId) => new()
    {
        productGroupId = groupId,
        connectionId = connectionId,
        externalGroupId = externalId
    };
}
