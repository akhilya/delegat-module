using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public sealed class AttributeDefinitionFileRulesTests
{
    [Fact]
    public void FileSettings_RejectUnsupportedCombinations()
    {
        Assert.Equal("multiple_files_requires_file_attribute", Assert.Throws<ApiException>(() =>
            AttributeDefinitionFileRules.Validate(AttributeType.Text, true, false, null)).Code);
        Assert.Equal("file_attribute_cannot_be_unique", Assert.Throws<ApiException>(() =>
            AttributeDefinitionFileRules.Validate(AttributeType.File, false, true, null)).Code);
        Assert.Equal("file_attribute_default_unsupported", Assert.Throws<ApiException>(() =>
            AttributeDefinitionFileRules.Validate(AttributeType.File, false, false, "123")).Code);
        AttributeDefinitionFileRules.Validate(AttributeType.File, true, false, null);
    }

    [Fact]
    public async Task CategoryAttribute_CannotChangeToOrFromFileWhileValuesExist()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.Text);
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = product.Id, attributeId = attribute.Id, value = "legacy"
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            AttributeDefinitionFileRules.ValidateCategoryUpdateAsync(
                db,
                attribute,
                new CategoryAttribute.UpdateForm { type = AttributeType.File }));

        Assert.Equal("attribute_type_change_with_values", error.Code);
    }

    [Fact]
    public async Task CategoryAttribute_CannotDisableMultipleWhileProductsHaveSeveralFiles()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.File);
        attribute.allowMultipleFiles = true;
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        var first = AddFile(db, product.Id, user.Id, 1);
        var second = AddFile(db, product.Id, user.Id, 2);
        var value = new ProductAttributeValue
        {
            productId = product.Id,
            attributeId = attribute.Id,
            value = ""
        };
        value.files.Add(new ProductAttributeFile { fileId = first.Id, index = 0 });
        value.files.Add(new ProductAttributeFile { fileId = second.Id, index = 1 });
        db.productAttributeValues.Add(value);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            AttributeDefinitionFileRules.ValidateCategoryUpdateAsync(
                db,
                attribute,
                new CategoryAttribute.UpdateForm { allowMultipleFiles = false }));

        Assert.Equal("attribute_multiple_files_in_use", error.Code);
    }

    [Fact]
    public async Task StoreAttribute_CannotDisableMultipleWhileProductsHaveSeveralFiles()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var attribute = data.AddStoreAttribute(store.Id, type: AttributeType.File);
        attribute.allowMultipleFiles = true;
        var product = data.AddProduct(store.Id);
        var first = AddFile(db, product.Id, user.Id, 1);
        var second = AddFile(db, product.Id, user.Id, 2);
        var value = new StoreProductAttributeValue
        {
            productId = product.Id,
            storeAttributeId = attribute.Id,
            value = ""
        };
        value.files.Add(new StoreProductAttributeFile { fileId = first.Id, index = 0 });
        value.files.Add(new StoreProductAttributeFile { fileId = second.Id, index = 1 });
        db.storeProductAttributeValues.Add(value);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            AttributeDefinitionFileRules.ValidateStoreUpdateAsync(
                db,
                attribute,
                new StoreAttribute.UpdateForm { allowMultipleFiles = false }));

        Assert.Equal("attribute_multiple_files_in_use", error.Code);
    }

    private static Models.File AddFile(DataContext db, int productId, int userId, int id)
    {
        var file = new Models.File
        {
            Id = id,
            productId = productId,
            authorUserId = userId,
            filePath = $"products/{productId}/{id}",
            contentType = "application/pdf",
            length = 1,
            fileType = FileType.Other,
            uploadDateTime = DateTime.UtcNow
        };
        db.files.Add(file);
        return file;
    }
}
