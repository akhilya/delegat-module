using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class CategoryBulkDeleteTests
{
    private static CategoryController Controller(DataContext db, int? storeId) => new(
        new FakeCurrentUserService { SelectedStoreId = storeId }, TestDbFactory.CreateUnitOfWork(db),
        null!, NullLogger<CategoryController>.Instance, db);

    [Fact]
    public async Task DeletesSelectedDefinitionsAndValues_WithoutDeletingTrashedProducts()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var first = data.AddCategory(store.Id);
        var second = data.AddCategory(store.Id);
        var retained = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(first.Id, options: new string('x', 100000));
        var secondAttr = data.AddCategoryAttribute(second.Id);
        var retainedAttr = data.AddCategoryAttribute(retained.Id);
        var trashed = data.AddProduct(store.Id, categoryId: first.Id, deletedAt: DateTime.UtcNow);
        var active = data.AddProduct(store.Id, categoryId: retained.Id);
        var savedValue = new ProductAttributeValue { productId = trashed.Id, attributeId = attr.Id, value = "Saved value" };
        db.productAttributeValues.Add(savedValue);
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        var deletedValueAttributeIds = new Dictionary<int, int>();
        var deletedAttributeCategoryIds = new Dictionary<int, int>();
        db.SavingChanges += (_, _) =>
        {
            foreach (var entry in db.ChangeTracker.Entries<ProductAttributeValue>()
                .Where(entry => entry.State == EntityState.Deleted))
                deletedValueAttributeIds[entry.Entity.Id] = entry.Property(value => value.attributeId).CurrentValue;
            foreach (var entry in db.ChangeTracker.Entries<CategoryAttribute>()
                .Where(entry => entry.State == EntityState.Deleted))
                deletedAttributeCategoryIds[entry.Entity.Id] = entry.Property(attribute => attribute.categoryId).CurrentValue;
        };

        Assert.IsType<OkObjectResult>(await Controller(db, store.Id).BulkDeleteCategories(
            new Category.BulkDeleteForm { categoryIds = [first.Id, second.Id, first.Id] }));
        Assert.Equal(attr.Id, deletedValueAttributeIds[savedValue.Id]);
        Assert.Equal(first.Id, deletedAttributeCategoryIds[attr.Id]);
        Assert.Equal(second.Id, deletedAttributeCategoryIds[secondAttr.Id]);
        db.ChangeTracker.Clear();
        Assert.Equal(retained.Id, (await db.categories.SingleAsync()).Id);
        Assert.Equal(retainedAttr.Id, (await db.categoryAttributes.SingleAsync()).Id);
        Assert.Empty(await db.productAttributeValues.ToListAsync());
        var preservedTrash = await db.products.IgnoreQueryFilters().SingleAsync(p => p.Id == trashed.Id);
        Assert.NotNull(preservedTrash.deletedAt);
        Assert.Null(preservedTrash.categoryId);
        Assert.Equal(retained.Id, (await db.products.SingleAsync(p => p.Id == active.Id)).categoryId);
    }

    [Fact]
    public async Task ActiveProducts_BlockEntireSelection()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var empty = data.AddCategory(store.Id);
        var occupied = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(empty.Id, options: "[\"Keep\"]");
        data.AddProduct(store.Id, categoryId: occupied.Id);
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<ApiException>(() => Controller(db, store.Id).BulkDeleteCategories(
            new Category.BulkDeleteForm { categoryIds = [empty.Id, occupied.Id] }));
        Assert.Equal("category_has_products", error.Code);
        Assert.Equal(2, await db.categories.CountAsync());
        Assert.Equal("[\"Keep\"]", (await db.categoryAttributes.SingleAsync(a => a.Id == attr.Id)).options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrMissingId_DoesNotPartiallyDelete(bool foreign)
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var other = data.AddStore();
        var ownCategory = data.AddCategory(store.Id);
        var otherCategory = data.AddCategory(other.Id);
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<ApiException>(() => Controller(db, store.Id).BulkDeleteCategories(
            new Category.BulkDeleteForm { categoryIds = [ownCategory.Id, foreign ? otherCategory.Id : 999999] }));
        Assert.Equal("category_not_found", error.Code);
        Assert.Equal(2, await db.categories.CountAsync());
    }

    [Fact]
    public async Task RejectsEmptyInvalidOversizedAndMissingStoreSelections()
    {
        using var db = TestDbFactory.CreateDb();
        foreach (var ids in new int[][] { [], [0], [-1], Enumerable.Range(1, 1001).ToArray(), null! })
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => Controller(db, 1).BulkDeleteCategories(
                new Category.BulkDeleteForm { categoryIds = ids }));
            Assert.Equal("categories_selection_invalid", error.Code);
        }
        Assert.Equal("store_not_selected", (await Assert.ThrowsAsync<ApiException>(() =>
            Controller(db, null).BulkDeleteCategories(new Category.BulkDeleteForm { categoryIds = [1] }))).Code);
    }

    [Fact]
    public void RequiresCatalogWritePermissionAndMembership()
    {
        var permission = Assert.Single(typeof(CategoryController).GetMethod(nameof(CategoryController.BulkDeleteCategories))!
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), true).Cast<RequiresPermissionAttribute>());
        Assert.Equal(PermissionPolicyProvider.StorePolicyName(Permissions.CatalogManage), permission.Policy);
    }
}
