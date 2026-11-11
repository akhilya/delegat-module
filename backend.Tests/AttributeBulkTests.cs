using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AttributeBulkTests
{
    private sealed class Fixture : IDisposable
    {
        public DataContext Db { get; } = TestDbFactory.CreateDb();
        public FakeCurrentUserService Current { get; } = new();
        public CategoryController Categories { get; }
        public StoreAttributeController Global { get; }
        public int CategoryId { get; }
        public int OtherCategoryId { get; }
        public CategoryAttribute CategoryAttribute { get; }
        public StoreAttribute GlobalAttribute { get; }
        public int OtherCategoryAttributeId { get; }
        public int OtherStoreAttributeId { get; }

        public Fixture()
        {
            var data = new TestDataBuilder(Db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            var otherStore = data.AddStore();
            var category = data.AddCategory(store.Id);
            CategoryId = category.Id;
            OtherCategoryId = data.AddCategory(store.Id).Id;
            CategoryAttribute = data.AddCategoryAttribute(category.Id, options: "[\"Red\"]");
            CategoryAttribute.description = "Keep category description";
            CategoryAttribute.descriptionInTooltip = true;
            CategoryAttribute.group = "Color";
            CategoryAttribute.groupIcon = "Palette";
            CategoryAttribute.maxLength = 42;
            CategoryAttribute.defaultValue = "Red";
            GlobalAttribute = data.AddStoreAttribute(store.Id, options: "[\"Red\"]");
            GlobalAttribute.description = "Keep global description";
            GlobalAttribute.descriptionInTooltip = true;
            GlobalAttribute.maxLength = 42;
            GlobalAttribute.defaultValue = "Red";
            OtherCategoryAttributeId = data.AddCategoryAttribute(OtherCategoryId).Id;
            OtherStoreAttributeId = data.AddStoreAttribute(otherStore.Id).Id;
            var product = data.AddProduct(store.Id, categoryId: category.Id);
            Db.productAttributeValues.Add(new ProductAttributeValue
            {
                productId = product.Id, attributeId = CategoryAttribute.Id, value = "Red",
            });
            Db.storeProductAttributeValues.Add(new StoreProductAttributeValue
            {
                productId = product.Id, storeAttributeId = GlobalAttribute.Id, value = "Red",
            });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            Current.UserId = user.Id;
            Current.SelectedStoreId = store.Id;
            var unit = TestDbFactory.CreateUnitOfWork(Db);
            Categories = new CategoryController(Current, unit, null!, NullLogger<CategoryController>.Instance, Db);
            Global = new StoreAttributeController(Current, unit, null!, NullLogger<StoreAttributeController>.Instance, Db);
        }

        public int Id(bool global) => global ? GlobalAttribute.Id : CategoryAttribute.Id;
        public Task<IActionResult> Update(bool global, AttributeBulkUpdateForm form) => global
            ? Global.BulkUpdateAttributes(form) : Categories.BulkUpdateAttributes(CategoryId, form);
        public Task<IActionResult> Delete(bool global, AttributeBulkSelectionForm form) => global
            ? Global.BulkDeleteAttributes(form) : Categories.BulkDeleteAttributes(CategoryId, form);
        public async Task<IAttributeDefinitionSettings> Read(bool global)
        {
            Db.ChangeTracker.Clear();
            return global ? await Db.storeAttributes.AsNoTracking().SingleAsync(a => a.Id == GlobalAttribute.Id)
                : await Db.categoryAttributes.AsNoTracking().SingleAsync(a => a.Id == CategoryAttribute.Id);
        }
        public void Dispose() => Db.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_PreservesUnspecifiedSettingsAndValues(bool global)
    {
        using var fixture = new Fixture();
        Assert.IsType<OkObjectResult>(await fixture.Update(global, new AttributeBulkUpdateForm
        {
            attributeIds = [fixture.Id(global), fixture.Id(global)], isUnique = true,
        }));
        var attribute = await fixture.Read(global);
        Assert.True(attribute.isUnique);
        Assert.False(attribute.isRequired);
        Assert.True(attribute.showOnProductPage);
        Assert.True(attribute.descriptionInTooltip);
        if (global)
        {
            var stored = Assert.IsType<StoreAttribute>(attribute);
            Assert.Equal("[\"Red\"]", stored.options);
            Assert.Equal("Keep global description", stored.description);
            Assert.Equal(42, stored.maxLength);
            Assert.Equal("Red", stored.defaultValue);
        }
        else
        {
            var stored = Assert.IsType<CategoryAttribute>(attribute);
            Assert.Equal("[\"Red\"]", stored.options);
            Assert.Equal("Keep category description", stored.description);
            Assert.Equal("Color", stored.group);
            Assert.Equal("Palette", stored.groupIcon);
            Assert.Equal(42, stored.maxLength);
            Assert.Equal("Red", stored.defaultValue);
        }
        Assert.Equal("Red", (await fixture.Db.productAttributeValues.SingleAsync()).value);
        Assert.Equal("Red", (await fixture.Db.storeProductAttributeValues.SingleAsync()).value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_RequiredRemainsVisible_AndExplicitFalseApplies(bool global)
    {
        using var fixture = new Fixture();
        var ids = new[] { fixture.Id(global) };
        await fixture.Update(global, new AttributeBulkUpdateForm { attributeIds = ids, isRequired = true, showOnProductPage = false });
        Assert.True((await fixture.Read(global)).showOnProductPage);
        await fixture.Update(global, new AttributeBulkUpdateForm { attributeIds = ids, isRequired = false, showOnProductPage = false });
        var attribute = await fixture.Read(global);
        Assert.False(attribute.isRequired);
        Assert.False(attribute.showOnProductPage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_DescriptionInTooltip_AppliesTrueAndFalse(bool global)
    {
        using var fixture = new Fixture();
        int[] ids = [fixture.Id(global)];

        await fixture.Update(global, new AttributeBulkUpdateForm
        {
            attributeIds = ids,
            descriptionInTooltip = false,
        });
        Assert.False((await fixture.Read(global)).descriptionInTooltip);

        await fixture.Update(global, new AttributeBulkUpdateForm
        {
            attributeIds = ids,
            descriptionInTooltip = true,
        });
        Assert.True((await fixture.Read(global)).descriptionInTooltip);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ForeignSelection_RejectsEntireRequest(bool global, bool delete)
    {
        using var fixture = new Fixture();
        int[] ids = [fixture.Id(global), global ? fixture.OtherStoreAttributeId : fixture.OtherCategoryAttributeId];
        var error = await Assert.ThrowsAsync<ApiException>(() => delete
            ? fixture.Delete(global, new AttributeBulkSelectionForm { attributeIds = ids })
            : fixture.Update(global, new AttributeBulkUpdateForm { attributeIds = ids, isRequired = true }));
        Assert.Equal(404, error.StatusCode);
        Assert.False((await fixture.Read(global)).isRequired);
        Assert.Single(await fixture.Db.productAttributeValues.ToListAsync());
        Assert.Single(await fixture.Db.storeProductAttributeValues.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_RemovesOnlySelectedDefinitionsAndValues(bool global)
    {
        using var fixture = new Fixture();
        await fixture.Delete(global, new AttributeBulkSelectionForm { attributeIds = [fixture.Id(global)] });
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(global ? 0 : 1, await fixture.Db.storeProductAttributeValues.CountAsync());
        Assert.Equal(global ? 1 : 0, await fixture.Db.productAttributeValues.CountAsync());
        Assert.False(global
            ? await fixture.Db.storeAttributes.AnyAsync(a => a.Id == fixture.Id(true))
            : await fixture.Db.categoryAttributes.AnyAsync(a => a.Id == fixture.Id(false)));
        Assert.True(await fixture.Db.storeAttributes.AnyAsync(a => a.Id == fixture.OtherStoreAttributeId));
        Assert.True(await fixture.Db.categoryAttributes.AnyAsync(a => a.Id == fixture.OtherCategoryAttributeId));
        Assert.Single(await fixture.Db.products.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleDefinitions_UpdateAndDeleteTogether(bool global)
    {
        using var fixture = new Fixture();
        if (global)
            fixture.Db.storeAttributes.Add(new StoreAttribute { Id = 100, storeId = fixture.Current.SelectedStoreId!.Value, name = "Extra", key = "extra" });
        else
            fixture.Db.categoryAttributes.Add(new CategoryAttribute { Id = 100, categoryId = fixture.CategoryId, name = "Extra", key = "extra" });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        int[] ids = [fixture.Id(global), 100];
        await fixture.Update(global, new AttributeBulkUpdateForm { attributeIds = ids, isRequired = true });
        Assert.Equal(2, global
            ? await fixture.Db.storeAttributes.CountAsync(a => ids.Contains(a.Id) && a.isRequired)
            : await fixture.Db.categoryAttributes.CountAsync(a => ids.Contains(a.Id) && a.isRequired));
        fixture.Db.ChangeTracker.Clear();
        await fixture.Delete(global, new AttributeBulkSelectionForm { attributeIds = ids });
        Assert.Equal(0, global
            ? await fixture.Db.storeAttributes.CountAsync(a => ids.Contains(a.Id))
            : await fixture.Db.categoryAttributes.CountAsync(a => ids.Contains(a.Id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingId_DoesNotPartiallyDelete(bool global)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ApiException>(() => fixture.Delete(global,
            new AttributeBulkSelectionForm { attributeIds = [fixture.Id(global), 999999] }));
        Assert.NotNull(await fixture.Read(global));
    }

    [Fact]
    public async Task CategoryFromAnotherStore_IsRejected()
    {
        using var fixture = new Fixture();
        fixture.Current.SelectedStoreId = await fixture.Db.storeAttributes
            .Where(a => a.Id == fixture.OtherStoreAttributeId).Select(a => a.storeId).SingleAsync();
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Update(false,
            new AttributeBulkUpdateForm { attributeIds = [fixture.Id(false)], isRequired = true }));
        Assert.Equal("category_not_found", error.Code);
        Assert.False((await fixture.Read(false)).isRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingStore_RejectsMutation(bool global)
    {
        using var fixture = new Fixture();
        fixture.Current.SelectedStoreId = null;
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Delete(global,
            new AttributeBulkSelectionForm { attributeIds = [fixture.Id(global)] }));
        Assert.Equal("store_not_selected", error.Code);
    }

    [Fact]
    public void SelectionAndChanges_AreBoundedAndExplicit()
    {
        Assert.Equal("attributes_selection_invalid", Assert.Throws<ApiException>(() =>
            new AttributeBulkSelectionForm { attributeIds = null! }.GetValidatedIds()).Code);
        foreach (var ids in new[] { Array.Empty<int>(), new[] { 0 }, new[] { -1 }, Enumerable.Range(1, 1001).ToArray() })
            Assert.Equal("attributes_selection_invalid", Assert.Throws<ApiException>(() =>
                new AttributeBulkSelectionForm { attributeIds = ids }.GetValidatedIds()).Code);
        Assert.Equal("attributes_changes_required", Assert.Throws<ApiException>(() => new AttributeBulkUpdateForm().ValidateChanges()).Code);
    }

    [Theory]
    [InlineData(typeof(CategoryController))]
    [InlineData(typeof(StoreAttributeController))]
    public void BulkEndpoints_RequireCatalogPermissionAndMembership(Type controller)
    {
        foreach (var method in new[] { "BulkUpdateAttributes", "BulkDeleteAttributes" })
        {
            var permission = Assert.Single(controller.GetMethod(method)!
                .GetCustomAttributes(typeof(RequiresPermissionAttribute), true).Cast<RequiresPermissionAttribute>());
            Assert.Equal(PermissionPolicyProvider.StorePolicyName(Permissions.CatalogManage), permission.Policy);
        }
    }
}
