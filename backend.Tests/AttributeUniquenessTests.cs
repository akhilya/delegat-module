using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class AttributeUniquenessTests
{
    private static (DataContext db, TestDataBuilder data, AttributeUniquenessService svc) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db), new AttributeUniquenessService(db));
    }

    [Fact]
    public void CategoryAttributeAddFill_PersistsIsUnique()
    {
        var attribute = new CategoryAttribute();
        attribute.AddFill(new CategoryAttribute.AddForm
        {
            categoryId = 1, name = "Article", key = "article", isUnique = true
        });
        Assert.True(attribute.isUnique);
    }

    [Fact]
    public void StoreAttributeAddFill_PersistsIsUnique()
    {
        var attribute = new StoreAttribute();
        attribute.AddFill(new StoreAttribute.AddForm
        {
            storeId = 1, name = "GTIN", key = "gtin", isUnique = true
        });
        Assert.True(attribute.isUnique);
    }

    [Fact]
    public void CategoryAttributeUpdateFill_UpdatesIsUnique()
    {
        var attribute = new CategoryAttribute { isUnique = false };
        attribute.UpdateFill(new CategoryAttribute.UpdateForm { isUnique = true });
        Assert.True(attribute.isUnique);
    }

    [Fact]
    public async Task CategoryUnique_ConflictOnSameCategory()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(category.Id, "Article", isUnique: true);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = existing.Id,
            attributeId = attr.Id,
            value = "X-100"
        });
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.EnsureUniqueAsync(
            store.Id,
            category.Id,
            new[] { new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "X-100" } },
            null));

        Assert.Equal("attribute_value_not_unique", ex.Code);
    }

    [Fact]
    public async Task CategoryUnique_EmptyValueAllowed()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(category.Id, "Article", isUnique: true);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = existing.Id,
            attributeId = attr.Id,
            value = "X-100"
        });
        await data.SaveAsync();

        await svc.EnsureUniqueAsync(
            store.Id,
            category.Id,
            new[] { new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "  " } },
            null);
    }

    [Fact]
    public async Task CategoryUnique_DifferentCategoryAllowed()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var catA = data.AddCategory(store.Id, "A");
        var catB = data.AddCategory(store.Id, "B");
        var attrA = data.AddCategoryAttribute(catA.Id, "Article", isUnique: true);
        var attrB = data.AddCategoryAttribute(catB.Id, "Article", isUnique: true);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: catA.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = existing.Id,
            attributeId = attrA.Id,
            value = "X-100"
        });
        await data.SaveAsync();

        await svc.EnsureUniqueAsync(
            store.Id,
            catB.Id,
            new[] { new ProductAttributeValue.UserValueForm { attributeId = attrB.Id, value = "X-100" } },
            null);
    }

    [Fact]
    public async Task CategoryUnique_TrimOrdinalMatchConflicts()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(category.Id, "Article", isUnique: true);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = existing.Id,
            attributeId = attr.Id,
            value = "  X-100  "
        });
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.EnsureUniqueAsync(
            store.Id,
            category.Id,
            new[] { new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "X-100" } },
            null));

        Assert.Equal("attribute_value_not_unique", ex.Code);
    }

    [Fact]
    public async Task CategoryUnique_ExcludeSelfAllowsSameValue()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(category.Id, "Article", isUnique: true);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = existing.Id,
            attributeId = attr.Id,
            value = "X-100"
        });
        await data.SaveAsync();

        await svc.EnsureUniqueAsync(
            store.Id,
            category.Id,
            new[] { new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "X-100" } },
            null,
            excludeProductId: existing.Id);
    }

    [Fact]
    public async Task StoreUnique_ConflictAcrossCategories()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var catA = data.AddCategory(store.Id, "A");
        var catB = data.AddCategory(store.Id, "B");
        var attr = data.AddStoreAttribute(store.Id, "GTIN", isUnique: true);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: catA.Id);
        db.storeProductAttributeValues.Add(new StoreProductAttributeValue
        {
            productId = existing.Id,
            storeAttributeId = attr.Id,
            value = "GTIN-1"
        });
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.EnsureUniqueAsync(
            store.Id,
            catB.Id,
            null,
            new[] { new StoreProductAttributeValue.UserValueForm { storeAttributeId = attr.Id, value = "GTIN-1" } }));

        Assert.Equal("attribute_value_not_unique", ex.Code);
        Assert.NotNull(ex.Data);
    }

    [Fact]
    public async Task NonUniqueAttribute_AllowsDuplicates()
    {
        var (db, data, svc) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(category.Id, "Color", isUnique: false);
        var existing = data.AddProduct(store.Id, sku: "A", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = existing.Id,
            attributeId = attr.Id,
            value = "red"
        });
        await data.SaveAsync();

        await svc.EnsureUniqueAsync(
            store.Id,
            category.Id,
            new[] { new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "red" } },
            null);
    }
}
