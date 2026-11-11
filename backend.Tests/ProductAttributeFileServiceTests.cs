using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public sealed class ProductAttributeFileServiceTests
{
    [Fact]
    public async Task RequiredFileAttribute_RejectsMissingFiles()
    {
        var (db, _, category, _) = await CreateCategoryProductAsync(required: true);
        var service = new ProductAttributeFileService(db);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.EnsureRequiredCategoryFilesAsync(category.Id, []));

        Assert.Equal("required_attribute_file", error.Code);
    }

    [Fact]
    public async Task MultipleFileAttribute_PersistsOrderedProductOwnedLinks()
    {
        var (db, product, category, attribute) = await CreateCategoryProductAsync(
            allowMultiple: true);
        var first = AddFile(db, product.Id, "application/pdf");
        var second = AddFile(db, product.Id, "video/mp4");
        await db.SaveChangesAsync();
        var value = new ProductAttributeValue { productId = product.Id, attributeId = attribute.Id, value = "" };
        var form = new ProductAttributeValue.UserValueForm
        {
            attributeId = attribute.Id,
            value = "[" + second.Id + "," + first.Id + "]",
            fileIds = [second.Id, first.Id]
        };

        await new ProductAttributeFileService(db).ApplyCategoryAsync(value, form, category.Id);
        db.productAttributeValues.Add(value);
        await db.SaveChangesAsync();

        var links = await db.productAttributeFiles
            .Where(link => link.attributeValueId == value.Id)
            .OrderBy(link => link.index)
            .Select(link => link.fileId)
            .ToArrayAsync();
        Assert.Equal([second.Id, first.Id], links);
        Assert.Equal("", value.value);
    }

    [Fact]
    public async Task SingleFileAttribute_RejectsMultipleFiles()
    {
        var (db, product, category, attribute) = await CreateCategoryProductAsync();
        var first = AddFile(db, product.Id, "application/pdf");
        var second = AddFile(db, product.Id, "application/pdf");
        await db.SaveChangesAsync();
        var value = new ProductAttributeValue { productId = product.Id, attributeId = attribute.Id, value = "" };
        var form = new ProductAttributeValue.UserValueForm
        {
            attributeId = attribute.Id,
            value = $"[{first.Id},{second.Id}]",
            fileIds = [first.Id, second.Id]
        };

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new ProductAttributeFileService(db).ApplyCategoryAsync(value, form, category.Id));

        Assert.Equal("attribute_multiple_files_disabled", error.Code);
    }

    [Fact]
    public async Task FileAttribute_RejectsFileOwnedByAnotherProduct()
    {
        var (db, product, category, attribute) = await CreateCategoryProductAsync();
        var foreignProduct = new Product
        {
            Id = product.Id + 1,
            storeId = product.storeId,
            sku = "SKU-FOREIGN",
            name = "Other",
            description = "",
            createdAt = DateTime.UtcNow,
            updatedAt = DateTime.UtcNow
        };
        db.products.Add(foreignProduct);
        await db.SaveChangesAsync();
        var file = AddFile(db, foreignProduct.Id, "application/pdf");
        await db.SaveChangesAsync();
        var value = new ProductAttributeValue { productId = product.Id, attributeId = attribute.Id, value = "" };
        var form = new ProductAttributeValue.UserValueForm
        {
            attributeId = attribute.Id,
            value = $"[{file.Id}]",
            fileIds = [file.Id]
        };

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new ProductAttributeFileService(db).ApplyCategoryAsync(value, form, category.Id));

        Assert.Equal("file_not_found", error.Code);
    }

    [Fact]
    public void FileIdBridge_RejectsConflictingTypedAndLegacyValues()
    {
        var error = Assert.Throws<ApiException>(() =>
            ProductAttributeFileService.ParseFileIds([1], "[2]"));

        Assert.Equal("conflicting_attribute_file_values", error.Code);
    }

    private static async Task<(DataContext Db, Product Product, Category Category, CategoryAttribute Attribute)>
        CreateCategoryProductAsync(bool required = false, bool allowMultiple = false)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.File, isRequired: required);
        attribute.allowMultipleFiles = allowMultiple;
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        await db.SaveChangesAsync();
        return (db, product, category, attribute);
    }

    private static Models.File AddFile(DataContext db, int productId, string contentType)
    {
        var file = new Models.File
        {
            productId = productId,
            authorUserId = 1,
            filePath = $"stores/1/products/{productId}/{Guid.NewGuid():N}",
            contentType = contentType,
            length = 10,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        return file;
    }
}
