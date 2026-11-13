using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductPermanentDeleteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletesNestedFoldersAndFileAttributeReferences(bool bulk)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.File);
        var storeAttribute = data.AddStoreAttribute(store.Id, type: AttributeType.File);
        var product = data.AddProduct(store.Id, categoryId: category.Id, deletedAt: DateTime.UtcNow);
        var active = data.AddProduct(store.Id);
        var otherStore = data.AddStore();
        var foreign = data.AddProduct(otherStore.Id, deletedAt: DateTime.UtcNow);
        await db.SaveChangesAsync();
        var parent = new ProductFolder { productId = product.Id, name = "root" };
        var child = new ProductFolder { productId = product.Id, name = "child", parentFolder = parent };
        var leaf = new ProductFolder { productId = product.Id, name = "leaf", parentFolder = child };
        db.productFolders.AddRange(parent, child, leaf);
        var file = new Models.File { productId = product.Id, authorUserId = user.Id,
            contentType = "application/pdf", filePath = "manual.pdf", folder = leaf };
        db.files.Add(file);
        var value = new ProductAttributeValue { productId = product.Id, attributeId = attribute.Id, value = "" };
        var storeValue = new StoreProductAttributeValue { productId = product.Id, storeAttributeId = storeAttribute.Id, value = "" };
        db.productAttributeValues.Add(value);
        db.storeProductAttributeValues.Add(storeValue);
        await db.SaveChangesAsync();
        db.productAttributeFiles.Add(new ProductAttributeFile { attributeValueId = value.Id, fileId = file.Id });
        db.storeProductAttributeFiles.Add(new StoreProductAttributeFile { attributeValueId = storeValue.Id, fileId = file.Id });
        db.folderShareTokens.Add(new FolderShareToken { productId = product.Id, createdByUserId = user.Id, token = "root-share" });
        db.folderShareTokens.Add(new FolderShareToken { productId = product.Id, folderId = leaf.Id,
            createdByUserId = user.Id, token = "leaf-share" });
        db.fileShareTokens.Add(new FileShareToken { fileId = file.Id, createdByUserId = user.Id, token = "file-share" });
        db.pendingImageDownloads.Add(new PendingImageDownload { productId = product.Id, storeId = store.Id, authorUserId = user.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.SavingChanges += (_, _) => Assert.Empty(db.ChangeTracker.Entries<Models.File>());

        var controller = ProductCatalogPerformanceTests.Controller(db, store.Id);
        if (bulk)
            Assert.IsType<OkObjectResult>(await controller.BulkPermanentDelete(new ProductController.BulkDeleteRequest
            {
                productIds = [product.Id, active.Id, foreign.Id, product.Id]
            }));
        else
            Assert.IsType<OkObjectResult>(await controller.PermanentDeleteProduct(product.Id));

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Empty(await db.files.ToListAsync());
        Assert.Empty(await db.productFolders.ToListAsync());
        Assert.Empty(await db.productAttributeFiles.ToListAsync());
        Assert.Empty(await db.storeProductAttributeFiles.ToListAsync());
        Assert.Empty(await db.folderShareTokens.ToListAsync());
        Assert.Empty(await db.fileShareTokens.ToListAsync());
        Assert.Empty(await db.pendingImageDownloads.ToListAsync());
    }

    [Fact]
    public async Task BulkDelete_ProcessesMoreThanOneChunk()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var ids = Enumerable.Range(0, 205).Select(_ => data.AddProduct(store.Id, deletedAt: DateTime.UtcNow).Id).ToList();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>(await ProductCatalogPerformanceTests.Controller(db, store.Id)
            .BulkPermanentDelete(new ProductController.BulkDeleteRequest { productIds = ids }));
        Assert.Equal(205, (int)result.Value!.GetType().GetProperty("deletedCount")!.GetValue(result.Value)!);
        Assert.Equal(0, await db.products.IgnoreQueryFilters().CountAsync());
        Assert.Empty(db.ChangeTracker.Entries<Models.Products.Product>());
    }

    [Fact]
    public async Task BulkDelete_FailureInSecondChunkRollsBackFirstChunk()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var ids = Enumerable.Range(0, 105).Select(_ => data.AddProduct(store.Id, deletedAt: DateTime.UtcNow).Id).ToList();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saves = 0;
        db.SavingChanges += (_, _) =>
        {
            if (++saves == 2) throw new InvalidOperationException("Injected second-chunk failure");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProductCatalogPerformanceTests.Controller(db, store.Id)
            .BulkPermanentDelete(new ProductController.BulkDeleteRequest { productIds = ids }));

        db.ChangeTracker.Clear();
        Assert.Equal(105, await db.products.IgnoreQueryFilters().CountAsync());
    }
}
