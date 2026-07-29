using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

/// <summary>
/// Covers product bulk update: enqueue via PATCH /products/bulk, apply via ProductBulkUpdateService
/// for every AttributeType the UI can send, plus unique-attribute multi-product rejection.
/// </summary>
public class ProductBulkUpdateTests
{
    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null) =>
            Task.CompletedTask;

        public Task LogAsync(string operationId, int storeId, string level, string message) =>
            Task.CompletedTask;

        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) =>
            Task.CompletedTask;

        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) =>
            Task.CompletedTask;

        public Task UpdateStatusAsync(string operationId, int storeId, string status) =>
            Task.CompletedTask;
    }

    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService user,
        ProductController controller, ProductBulkUpdateService service)
        CreateSut()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = new FakeCurrentUserService();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uniqueness = new AttributeUniquenessService(db);
        var service = new ProductBulkUpdateService(db, uow, uniqueness);
        var controller = new ProductController(
            user,
            uow,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<ProductController>.Instance,
            db,
            new ExcelProductService(),
            new QuotaService(uow),
            new NoopOperationLogger(),
            uniqueness,
            service);
        return (db, data, user, controller, service);
    }

    private static async Task<(Store store, Category category, Product a, Product b)> SeedTwoProductsAsync(
        TestDataBuilder data,
        FakeCurrentUserService user)
    {
        var member = data.AddUser();
        var store = data.AddStore(members: member);
        var category = data.AddCategory(store.Id);
        var a = data.AddProduct(store.Id, name: "A", categoryId: category.Id);
        var b = data.AddProduct(store.Id, name: "B", categoryId: category.Id);
        await data.SaveAsync();
        user.UserId = member.Id;
        user.SelectedStoreId = store.Id;
        user.User = member;
        return (store, category, a, b);
    }

    private static ProductController.BulkUpdateRequest Request(
        IEnumerable<int> productIds,
        ProductController.BulkUpdateFields updates) =>
        new()
        {
            productIds = productIds.ToList(),
            updates = updates
        };

    private static async Task RunAndAssertQueued(
        ProductController controller,
        DataContext db,
        ProductController.BulkUpdateRequest request)
    {
        var result = await controller.BulkUpdateProducts(request);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("queued").GetBoolean());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("operationId").GetString()));
        Assert.Equal(1, await db.pendingProductBulkUpdates.CountAsync());
    }

    [Fact]
    public async Task BulkUpdate_Enqueue_CreatesPendingJob()
    {
        var (db, data, user, controller, _) = CreateSut();
        var (_, _, a, b) = await SeedTwoProductsAsync(data, user);

        await RunAndAssertQueued(controller, db, Request(
            [a.Id, b.Id],
            new ProductController.BulkUpdateFields { name = "Shared name" }));

        var pending = Assert.Single(await db.pendingProductBulkUpdates.ToListAsync());
        Assert.Equal(ProductBulkUpdateStatus.Pending, pending.status);
        Assert.Contains(a.Id.ToString(), pending.productIdsJson);
        Assert.Contains(b.Id.ToString(), pending.productIdsJson);
    }

    [Fact]
    public async Task BulkUpdate_SetsBasicFieldsOnAllProducts()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, _, a, b) = await SeedTwoProductsAsync(data, user);

        var result = await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            name = "Shared name",
            description = "Shared desc",
            price = 199.5m,
            storageQuantity = 3
        });

        Assert.Equal(2, result.updatedCount);
        Assert.Equal(0, result.failedCount);
        var reloaded = await db.products.Where(p => p.Id == a.Id || p.Id == b.Id).ToListAsync();
        Assert.All(reloaded, p =>
        {
            Assert.Equal("Shared name", p.name);
            Assert.Equal("Shared desc", p.description);
            Assert.Equal(199.5m, p.price);
            Assert.Equal(3, p.storageQuantity);
        });
    }

    [Fact]
    public async Task BulkUpdate_TextAttribute_CreatesAndOverwritesValues()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, category, a, b) = await SeedTwoProductsAsync(data, user);
        var attr = data.AddCategoryAttribute(category.Id, "Material", AttributeType.Text);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = a.Id,
            attributeId = attr.Id,
            value = "old"
        });
        await data.SaveAsync();

        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "cotton" }
            ]
        });

        var values = await db.productAttributeValues
            .Where(v => v.attributeId == attr.Id)
            .OrderBy(v => v.productId)
            .ToListAsync();
        Assert.Equal(2, values.Count);
        Assert.All(values, v => Assert.Equal("cotton", v.value));
    }

    [Fact]
    public async Task BulkUpdate_NumberAttribute_PersistsNumericString()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, category, a, b) = await SeedTwoProductsAsync(data, user);
        var attr = data.AddCategoryAttribute(category.Id, "Weight", AttributeType.Number);

        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "12.5" }
            ]
        });

        var values = await db.productAttributeValues.Where(v => v.attributeId == attr.Id).ToListAsync();
        Assert.Equal(2, values.Count);
        Assert.All(values, v => Assert.Equal("12.5", v.value));
    }

    [Fact]
    public async Task BulkUpdate_BooleanAttribute_PersistsTrueFalseStrings()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, category, a, b) = await SeedTwoProductsAsync(data, user);
        var attr = data.AddCategoryAttribute(category.Id, "In stock flag", AttributeType.Boolean);

        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "true" }
            ]
        });

        var values = await db.productAttributeValues.Where(v => v.attributeId == attr.Id).ToListAsync();
        Assert.Equal(2, values.Count);
        Assert.All(values, v => Assert.Equal("true", v.value));
    }

    [Fact]
    public async Task BulkUpdate_SelectAttribute_PersistsOptionValue()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, category, a, b) = await SeedTwoProductsAsync(data, user);
        var options = """[{"value":"red","label":"Красный"},{"value":"blue","label":"Синий"}]""";
        var attr = data.AddCategoryAttribute(
            category.Id, "Color", AttributeType.Select, options: options);

        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "red" }
            ]
        });

        var values = await db.productAttributeValues.Where(v => v.attributeId == attr.Id).ToListAsync();
        Assert.Equal(2, values.Count);
        Assert.All(values, v => Assert.Equal("red", v.value));
    }

    [Fact]
    public async Task BulkUpdate_MultiSelectAttribute_PersistsJsonArrayOfValues()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, category, a, b) = await SeedTwoProductsAsync(data, user);
        var options = """[{"value":"red","label":"Красный"},{"value":"green","label":"Зелёный"},{"value":"blue","label":"Синий"}]""";
        var attr = data.AddCategoryAttribute(
            category.Id, "Colors", AttributeType.MultiSelect, options: options);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = a.Id,
            attributeId = attr.Id,
            value = """["blue"]"""
        });
        await data.SaveAsync();

        const string bulkValue = """["red","green"]""";
        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = bulkValue }
            ]
        });

        var values = await db.productAttributeValues
            .Where(v => v.attributeId == attr.Id)
            .OrderBy(v => v.productId)
            .ToListAsync();
        Assert.Equal(2, values.Count);
        Assert.All(values, v => Assert.Equal(bulkValue, v.value));
    }

    [Fact]
    public async Task BulkUpdate_StoreMultiSelectAttribute_PersistsJsonArray()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, _, a, b) = await SeedTwoProductsAsync(data, user);
        var options = """[{"value":"s","label":"S"},{"value":"m","label":"M"},{"value":"l","label":"L"}]""";
        var attr = data.AddStoreAttribute(
            store.Id, "Sizes", AttributeType.MultiSelect, options: options);
        await data.SaveAsync();

        const string bulkValue = """["s","l"]""";
        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            storeAttributeValues =
            [
                new StoreProductAttributeValue.UserValueForm
                {
                    storeAttributeId = attr.Id,
                    value = bulkValue
                }
            ]
        });

        var values = await db.storeProductAttributeValues
            .Where(v => v.storeAttributeId == attr.Id)
            .ToListAsync();
        Assert.Equal(2, values.Count);
        Assert.All(values, v => Assert.Equal(bulkValue, v.value));
    }

    [Fact]
    public async Task BulkUpdate_UniqueAttribute_OnMultipleProducts_IsRejectedAtEnqueue()
    {
        var (db, data, user, controller, _) = CreateSut();
        var (_, category, a, b) = await SeedTwoProductsAsync(data, user);
        var attr = data.AddCategoryAttribute(
            category.Id, "Article", AttributeType.Text, isUnique: true);
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.BulkUpdateProducts(Request(
            [a.Id, b.Id],
            new ProductController.BulkUpdateFields
            {
                attributeValues =
                [
                    new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "ART-1" }
                ]
            })));

        Assert.Equal("attribute_value_unique_bulk_forbidden", ex.Code);
        Assert.Empty(await db.pendingProductBulkUpdates.ToListAsync());
        Assert.Empty(await db.productAttributeValues.Where(v => v.attributeId == attr.Id).ToListAsync());
    }

    [Fact]
    public async Task BulkUpdate_UniqueAttribute_OnSingleProduct_SucceedsWhenFree()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, category, a, _) = await SeedTwoProductsAsync(data, user);
        var attr = data.AddCategoryAttribute(
            category.Id, "Article", AttributeType.Text, isUnique: true);
        await data.SaveAsync();

        var result = await service.ExecuteAsync(store.Id, [a.Id], new ProductBulkUpdateFields
        {
            attributeValues =
            [
                new ProductAttributeValue.UserValueForm { attributeId = attr.Id, value = "ART-1" }
            ]
        });

        Assert.Equal(1, result.updatedCount);
        var value = Assert.Single(await db.productAttributeValues.Where(v => v.attributeId == attr.Id).ToListAsync());
        Assert.Equal("ART-1", value.value);
        Assert.Equal(a.Id, value.productId);
    }

    [Fact]
    public async Task BulkUpdate_GenerateBarcodes_FillsOnlyEmpty()
    {
        var (db, data, user, _, service) = CreateSut();
        var (store, _, a, b) = await SeedTwoProductsAsync(data, user);
        a.barcode = "4600000000002";
        await db.SaveChangesAsync();

        await service.ExecuteAsync(store.Id, [a.Id, b.Id], new ProductBulkUpdateFields
        {
            generateBarcodes = true
        });

        await db.Entry(a).ReloadAsync();
        await db.Entry(b).ReloadAsync();
        Assert.Equal("4600000000002", a.barcode);
        Assert.False(string.IsNullOrEmpty(b.barcode));
        Assert.Equal(13, b.barcode!.Length);
    }
}
