using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using ProductFile = Delegat.Models.File;

namespace Delegat.Tests;

public class ProductFilesDomainTests
{
    private static ProductFile NewFile(int productId, int userId, int? folderId = null) => new()
    {
        productId = productId, authorUserId = userId, folderId = folderId,
        filePath = $"products/{productId}/asset.jpg", contentType = "image/jpeg",
        length = 128, fileType = FileType.Image, uploadDateTime = DateTime.UtcNow
    };

    [Fact]
    public async Task FileAddFill_PersistsRequiredStorageFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();
        var file = new ProductFile();

        Assert.True(file.AddFill(new ProductFile.AddForm("products/1/manual.pdf", user.Id, product.Id, "application/pdf", 44)));
        file.fileType = FileType.Other;
        db.files.Add(file);
        await db.SaveChangesAsync();

        var saved = db.files.Single();
        Assert.Equal("products/1/manual.pdf", saved.filePath);
        Assert.Equal(user.Id, saved.authorUserId);
        Assert.Equal(product.Id, saved.productId);
        Assert.Equal(44, saved.length);
        Assert.Equal("application/pdf", saved.contentType);
        Assert.NotEqual(default, saved.uploadDateTime);
    }

    [Fact]
    public async Task File_PublicFlag_Persists()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();
        var file = NewFile(product.Id, user.Id);
        file.isPublic = true;
        db.files.Add(file);
        await db.SaveChangesAsync();

        Assert.True(db.files.Single().isPublic);
    }

    [Fact]
    public async Task File_Rename_ChangesDisplayNameWithoutStoragePath()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();
        var file = NewFile(product.Id, user.Id);
        db.files.Add(file);
        await db.SaveChangesAsync();

        file.displayName = "catalog-cover";
        await db.SaveChangesAsync();

        Assert.Equal("catalog-cover", db.files.Single().displayName);
        Assert.Equal($"products/{product.Id}/asset.jpg", db.files.Single().filePath);
    }

    [Fact]
    public async Task File_Move_UpdatesFolderId()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        var folder = new ProductFolder { productId = product.Id, name = "Images" };
        db.productFolders.Add(folder);
        await data.SaveAsync();
        var file = NewFile(product.Id, user.Id);
        db.files.Add(file);
        await db.SaveChangesAsync();

        file.folderId = folder.Id;
        await db.SaveChangesAsync();

        Assert.Equal(folder.Id, db.files.Single().folderId);
    }

    [Fact]
    public async Task File_MoveToRoot_ClearsFolderId()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        var folder = new ProductFolder { productId = product.Id, name = "Images" };
        db.productFolders.Add(folder);
        await data.SaveAsync();
        var file = NewFile(product.Id, user.Id, folder.Id);
        db.files.Add(file);
        await db.SaveChangesAsync();

        file.folderId = null;
        await db.SaveChangesAsync();

        Assert.Null(db.files.Single().folderId);
    }

    [Fact]
    public async Task ProductFolder_CreateAndRename_PersistsParentRelationship()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        var root = new ProductFolder { productId = product.Id, name = "Root" };
        db.productFolders.Add(root);
        await db.SaveChangesAsync();
        var child = new ProductFolder { productId = product.Id, name = "Drafts", parentFolderId = root.Id };
        db.productFolders.Add(child);
        await db.SaveChangesAsync();

        child.name = "Published";
        await db.SaveChangesAsync();

        Assert.Equal(root.Id, db.productFolders.Single(f => f.Id == child.Id).parentFolderId);
        Assert.Equal("Published", db.productFolders.Single(f => f.Id == child.Id).name);
    }

    [Fact]
    public async Task ProductFolder_Delete_RemovesOnlySelectedFolder()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        var first = new ProductFolder { productId = product.Id, name = "First" };
        var second = new ProductFolder { productId = product.Id, name = "Second" };
        db.productFolders.AddRange(first, second);
        await data.SaveAsync();

        db.productFolders.Remove(first);
        await db.SaveChangesAsync();

        Assert.DoesNotContain(db.productFolders, f => f.Id == first.Id);
        Assert.Contains(db.productFolders, f => f.Id == second.Id);
    }

    [Fact]
    public async Task File_Delete_RemovesOnlySelectedFile()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();
        var first = NewFile(product.Id, user.Id);
        var second = NewFile(product.Id, user.Id);
        second.filePath = "products/1/second.jpg";
        db.files.AddRange(first, second);
        await db.SaveChangesAsync();

        db.files.Remove(first);
        await db.SaveChangesAsync();

        Assert.Single(db.files);
        Assert.Equal(second.Id, db.files.Single().Id);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void FileShareToken_ReportsExpiration(int offsetMinutes, bool expected)
    {
        var token = new FileShareToken { expiresAt = DateTime.UtcNow.AddMinutes(offsetMinutes) };
        Assert.Equal(expected, token.IsExpired);
    }

    [Fact]
    public async Task FileShareToken_PersistsFileAndCreator()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();
        var file = NewFile(product.Id, user.Id);
        db.files.Add(file);
        await db.SaveChangesAsync();

        db.fileShareTokens.Add(new FileShareToken { fileId = file.Id, createdByUserId = user.Id, token = "file-token", expiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        Assert.Equal(file.Id, db.fileShareTokens.Single().fileId);
    }

    [Fact]
    public async Task FileTag_AttachAndDetach_Persists()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var file = NewFile(product.Id, user.Id);
        var tag = new TagDefinition { storeId = store.Id, name = "Catalog", color = FileTagColor.Green };
        db.files.Add(file);
        db.tagDefinitions.Add(tag);
        await data.SaveAsync();
        var assignment = new FileTag { fileId = file.Id, tagDefinitionId = tag.Id };
        db.tags.Add(assignment);
        await db.SaveChangesAsync();
        Assert.Equal(tag.Id, db.tags.Single().tagDefinitionId);

        db.tags.Remove(assignment);
        await db.SaveChangesAsync();
        Assert.Empty(db.tags);
    }

    [Fact]
    public async Task FolderShareToken_PersistsRootFolderScope()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var product = data.AddProduct(data.AddStore().Id);
        await data.SaveAsync();

        db.folderShareTokens.Add(new FolderShareToken { productId = product.Id, folderId = null, createdByUserId = user.Id, token = "root-token", expiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        var token = db.folderShareTokens.Single();
        Assert.Equal(product.Id, token.productId);
        Assert.Null(token.folderId);
        Assert.False(token.IsExpired);
    }

    [Fact]
    public async Task FileQuota_AtLimitRejectsAnotherProductFile()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        data.SetAppSetting(AppSettingKeys.MaxFiles, "1");
        await data.SaveAsync();
        db.files.Add(NewFile(product.Id, user.Id));
        await db.SaveChangesAsync();
        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), new AppSettingsService(db));

        var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureFileQuotaAsync(store.Id));

        Assert.Equal(409, ex.StatusCode);
    }
}
