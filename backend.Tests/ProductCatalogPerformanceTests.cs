using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Interfaces;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ProductCatalogPerformanceTests
{
    internal static ProductController Controller(DataContext db, int storeId) => new(
        new FakeCurrentUserService { SelectedStoreId = storeId }, TestDbFactory.CreateUnitOfWork(db),
        new ServiceCollection().BuildServiceProvider(), NullLogger<ProductController>.Instance,
        db, null!, null!, null!, null!, null!, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public void ReviewProjection_TranslatesToMySqlWithoutHeavyColumns()
    {
        using var db = new DataContext(new DbContextOptionsBuilder<DataContext>()
            .UseMySql("Server=localhost;Database=not_used;User ID=not_used", new MySqlServerVersion(new Version(8, 4, 8)))
            .Options);
        // Single query exposes every projected collection's SQL without opening a connection.
        var sql = db.products.Where(p => p.storeId == 1 && new[] { 1, 2 }.Contains(p.Id))
            .SelectReviewData().AsSingleQuery().ToQueryString();
        Assert.DoesNotContain("graphJson", sql);
        Assert.DoesNotContain("credentials", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("`options`", sql);
        Assert.DoesNotContain("`description`", sql);
        Assert.Contains("ROW_NUMBER()", sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewProjection_PreservesImageFileAttributesAndMappingChoiceWithoutTracking(bool relational)
    {
        using var db = relational
            ? new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite("Data Source=:memory:").Options)
            : TestDbFactory.CreateDb();
        if (relational)
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
        }
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        var connection = data.AddConnection(store.Id);
        var mapping = new CategoryMapping
        {
            connectionId = connection.Id, localCategoryId = category.Id,
            externalCategoryId = "external", graphJson = new string('x', 100_000)
        };
        db.categoryMappings.Add(mapping);
        var image = new Models.File { productId = product.Id, authorUserId = user.Id,
            contentType = "image/jpeg", filePath = "image.jpg" };
        var video = new Models.File { productId = product.Id, authorUserId = user.Id,
            contentType = "video/mp4", filePath = "video.mp4" };
        db.files.AddRange(image, video);
        var attribute = new StoreAttribute { storeId = store.Id, key = "files", name = "Files", type = AttributeType.File };
        db.storeAttributes.Add(attribute);
        await db.SaveChangesAsync();
        db.images.AddRange(new ImageReference { productId = product.Id, fileId = video.Id, index = 0 },
            new ImageReference { productId = product.Id, fileId = image.Id, index = 1 });
        db.productMarketplaceLinks.Add(new ProductMarketplaceLink { productId = product.Id,
            connectionId = connection.Id, categoryMappingId = mapping.Id, externalProductId = "external" });
        var value = new StoreProductAttributeValue { productId = product.Id, storeAttributeId = attribute.Id, value = "" };
        db.storeProductAttributeValues.Add(value);
        await db.SaveChangesAsync();
        db.storeProductAttributeFiles.Add(new StoreProductAttributeFile { attributeValueId = value.Id, fileId = image.Id, index = 0 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var row = await db.products.SelectReviewData().SingleAsync();
        var dto = ((IMapToDTO<Product.ProductReviewDTO>)row).MapToDTO(new ServiceCollection().BuildServiceProvider());
        Assert.Equal($"/api/files/{image.Id}/download", dto.primaryImage);
        Assert.Equal(new[] { image.Id }, Assert.Single(dto.storeAttributeValues!).fileIds);
        Assert.Equal(category.Id, Assert.Single(row.marketplaceLinks).categoryMapping!.localCategoryId);
        Assert.Null(Assert.Single(row.marketplaceLinks).categoryMapping!.graphJson);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Lookup_IsBoundedAndDoesNotExposeForeignOrTrashedProducts()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var other = data.AddStore();
        for (var i = 0; i < 60; i++) data.AddProduct(store.Id);
        var foreign = data.AddProduct(other.Id);
        var trashed = data.AddProduct(store.Id);
        trashed.deletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = Controller(db, store.Id);
        var result = Assert.IsType<OkObjectResult>(await controller.LookupProducts(pageSize: 500));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(50, json.GetProperty("items").GetArrayLength());
        foreach (var id in new[] { foreign.Id, trashed.Id })
        {
            result = Assert.IsType<OkObjectResult>(await controller.LookupProducts(productId: id));
            json = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
            Assert.Equal(0, json.GetProperty("items").GetArrayLength());
        }
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CatalogList_PreservesPageOrderTotalAndCategoryFilter()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        for (var i = 0; i < 6; i++) data.AddProduct(store.Id, name: $"Product {i}", categoryId: category.Id);
        data.AddProduct(store.Id);
        data.AddProduct(store.Id, categoryId: category.Id, deletedAt: DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>(await Controller(db, store.Id).GetAllProducts(
            null, categoryId: category.Id, startIndex: 2, pageSize: 2, sort: "name:asc"));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(6, json.GetProperty("totalCount").GetInt32());
        Assert.False(json.GetProperty("lastPage").GetBoolean());
        Assert.Equal(new[] { "Product 2", "Product 3" }, json.GetProperty("items").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()).ToArray());
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
