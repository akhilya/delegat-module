using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class CategoryDomainTests
{
    private static (DataContext db, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db));
    }

    [Fact]
    public void AddFill_SetsCategoryFields()
    {
        var category = new Category();
        category.AddFill(new Category.AddForm { name = "Shoes", description = "Footwear", storeId = 3 });
        Assert.Equal("Shoes", category.name);
        Assert.Equal("Footwear", category.description);
        Assert.Equal(3, category.storeId);
    }

    [Fact]
    public void UpdateFill_UpdatesNameDescriptionAndSizes()
    {
        var category = new Category { name = "Old", description = "old" };
        category.UpdateFill(new Category.UpdateForm { name = "New", description = "new", hasSizes = true });
        Assert.Equal("New", category.name);
        Assert.Equal("new", category.description);
        Assert.True(category.hasSizes);
    }

    [Fact]
    public void UpdateFill_NullNameKeepsExistingName()
    {
        var category = new Category { name = "Original", description = "old" };
        category.UpdateFill(new Category.UpdateForm { description = "new" });
        Assert.Equal("Original", category.name);
    }

    [Fact]
    public async Task StoreQuery_IsolatesCategories()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddCategory(a.Id, "A category");
        data.AddCategory(b.Id, "B category");
        await data.SaveAsync();
        Assert.Equal("A category", (await db.categories.SingleAsync(c => c.storeId == a.Id)).name);
    }

    [Fact]
    public async Task CategoryDelete_WhenProductsExistSimulatesConflict()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        data.AddProduct(store.Id, categoryId: category.Id);
        await data.SaveAsync();
        var hasProducts = await db.products.AnyAsync(p => p.categoryId == category.Id);
        var ex = hasProducts ? ApiException.Conflict("category has products") : null;
        Assert.Equal(409, ex!.StatusCode);
    }

    [Fact]
    public async Task CategoryDelete_WithoutProductsCanRemoveCategory()
    {
        var (db, data) = Create();
        var category = data.AddCategory(data.AddStore().Id);
        await data.SaveAsync();
        Assert.False(await db.products.AnyAsync(p => p.categoryId == category.Id));
        db.categories.Remove(category);
        await db.SaveChangesAsync();
        Assert.Empty(db.categories);
    }

    [Fact]
    public void CategoryAttributeAddFill_SetsAllFields()
    {
        var attribute = new CategoryAttribute();
        attribute.AddFill(new CategoryAttribute.AddForm
        {
            categoryId = 2, name = "Color", key = "color", type = AttributeType.Select,
            isRequired = true, isMultiline = false, showOnProductPage = true, sortOrder = 4,
            options = "[\"red\"]", defaultValue = "red", description = "Main color",
            descriptionInTooltip = true, group = "Appearance"
        });
        Assert.Equal(2, attribute.categoryId);
        Assert.Equal(AttributeType.Select, attribute.type);
        Assert.True(attribute.isRequired);
        Assert.Equal(4, attribute.sortOrder);
        Assert.Equal("Appearance", attribute.group);
    }

    [Theory]
    [InlineData(AttributeType.Text)]
    [InlineData(AttributeType.Number)]
    [InlineData(AttributeType.Select)]
    public void CategoryAttributeAddFill_PreservesType(AttributeType type)
    {
        var attribute = new CategoryAttribute();
        attribute.AddFill(new CategoryAttribute.AddForm { categoryId = 1, name = "X", key = "x", type = type });
        Assert.Equal(type, attribute.type);
    }

    [Fact]
    public void CategoryAttributeUpdateFill_UpdatesOptionalFields()
    {
        var attribute = new CategoryAttribute { name = "Color", key = "color", type = AttributeType.Text, options = "old", group = "Old" };
        attribute.UpdateFill(new CategoryAttribute.UpdateForm
        {
            name = "Size", key = "size", type = AttributeType.Select, isRequired = true,
            sortOrder = 8, options = "[\"S\"]", defaultValue = "M", description = "Sizing",
            descriptionInTooltip = true, group = "Variants"
        });
        Assert.Equal("Size", attribute.name);
        Assert.Equal(AttributeType.Select, attribute.type);
        Assert.Equal(8, attribute.sortOrder);
        Assert.Equal("Variants", attribute.group);
    }

    [Fact]
    public async Task CategoryAttributes_OrderBySortOrder()
    {
        var (db, data) = Create();
        var category = data.AddCategory(data.AddStore().Id);
        data.AddCategoryAttribute(category.Id, "Last", sortOrder: 2);
        data.AddCategoryAttribute(category.Id, "First", sortOrder: 0);
        await data.SaveAsync();
        var names = await db.categoryAttributes.Where(a => a.categoryId == category.Id).OrderBy(a => a.sortOrder).Select(a => a.name).ToListAsync();
        Assert.Equal(new[] { "First", "Last" }, names);
    }

    [Fact]
    public async Task CategoryAttribute_ReorderChangesPersistedOrder()
    {
        var (db, data) = Create();
        var category = data.AddCategory(data.AddStore().Id);
        var first = data.AddCategoryAttribute(category.Id, "First", sortOrder: 0);
        var second = data.AddCategoryAttribute(category.Id, "Second", sortOrder: 1);
        await data.SaveAsync();
        first.sortOrder = 1;
        second.sortOrder = 0;
        await db.SaveChangesAsync();
        Assert.Equal("Second", (await db.categoryAttributes.Where(a => a.categoryId == category.Id).OrderBy(a => a.sortOrder).FirstAsync()).name);
    }

    [Fact]
    public async Task CategoryAttributes_BelongOnlyToTheirCategory()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var a = data.AddCategory(store.Id);
        var b = data.AddCategory(store.Id);
        data.AddCategoryAttribute(a.Id, "A");
        data.AddCategoryAttribute(b.Id, "B");
        await data.SaveAsync();
        Assert.Equal("A", (await db.categoryAttributes.SingleAsync(x => x.categoryId == a.Id)).name);
    }
}
