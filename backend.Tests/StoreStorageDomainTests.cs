using Delegat.Enums;
using Delegat.Models;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class StoreStorageDomainTests
{
    private static StoreFile NewFile(int storeId, int userId, int? folderId = null) => new()
    {
        storeId = storeId, authorUserId = userId, folderId = folderId,
        filePath = $"stores/{storeId}/asset.png", contentType = "image/png",
        length = 256, fileType = FileType.Image, uploadDateTime = DateTime.UtcNow
    };

    [Fact]
    public async Task StoreFile_FillPersistsStorageFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();
        var file = new StoreFile { fileType = FileType.Other };
        file.Fill("stores/a/price.pdf", user.Id, store.Id, "application/pdf", 512);
        db.storeFiles.Add(file);
        await db.SaveChangesAsync();

        var saved = db.storeFiles.Single();
        Assert.Equal("stores/a/price.pdf", saved.filePath);
        Assert.Equal(user.Id, saved.authorUserId);
        Assert.Equal(store.Id, saved.storeId);
        Assert.Equal(512, saved.length);
    }

    [Fact]
    public async Task StoreFile_CreateRenameAndDelete_ArePersisted()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();
        var file = NewFile(store.Id, user.Id);
        db.storeFiles.Add(file);
        await db.SaveChangesAsync();

        file.displayName = "logo";
        await db.SaveChangesAsync();
        Assert.Equal("logo", db.storeFiles.Single().displayName);

        db.storeFiles.Remove(file);
        await db.SaveChangesAsync();
        Assert.Empty(db.storeFiles);
    }

    [Fact]
    public async Task StoreFile_PublicFlag_Persists()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();
        var file = NewFile(store.Id, user.Id);
        file.isPublic = true;
        db.storeFiles.Add(file);
        await db.SaveChangesAsync();

        Assert.True(db.storeFiles.Single().isPublic);
    }

    [Fact]
    public async Task StoreFolder_CreateRenameAndDelete_Persists()
    {
        await using var db = TestDbFactory.CreateDb();
        var store = new TestDataBuilder(db).AddStore();
        await db.SaveChangesAsync();
        var folder = new StoreFolder { storeId = store.Id, name = "Incoming" };
        db.storeFolders.Add(folder);
        await db.SaveChangesAsync();

        folder.name = "Approved";
        await db.SaveChangesAsync();
        Assert.Equal("Approved", db.storeFolders.Single().name);

        db.storeFolders.Remove(folder);
        await db.SaveChangesAsync();
        Assert.Empty(db.storeFolders);
    }

    [Fact]
    public async Task StoreFolder_MoveUnderParent_PersistsHierarchy()
    {
        await using var db = TestDbFactory.CreateDb();
        var store = new TestDataBuilder(db).AddStore();
        var root = new StoreFolder { storeId = store.Id, name = "Root" };
        db.storeFolders.Add(root);
        await db.SaveChangesAsync();
        var child = new StoreFolder { storeId = store.Id, name = "Child", parentFolderId = root.Id };
        db.storeFolders.Add(child);
        await db.SaveChangesAsync();

        Assert.Equal(root.Id, db.storeFolders.Single(f => f.Id == child.Id).parentFolderId);
    }

    [Fact]
    public async Task StoreFile_MoveBetweenFolderAndRoot_Persists()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        var folder = new StoreFolder { storeId = store.Id, name = "Media" };
        db.storeFolders.Add(folder);
        await data.SaveAsync();
        var file = NewFile(store.Id, user.Id);
        db.storeFiles.Add(file);
        await db.SaveChangesAsync();

        file.folderId = folder.Id;
        await db.SaveChangesAsync();
        Assert.Equal(folder.Id, db.storeFiles.Single().folderId);

        file.folderId = null;
        await db.SaveChangesAsync();
        Assert.Null(db.storeFiles.Single().folderId);
    }

    [Theory]
    [InlineData(-5, true)]
    [InlineData(5, false)]
    public void StoreFileShareToken_ReportsExpiration(int offsetMinutes, bool expected)
    {
        Assert.Equal(expected, new StoreFileShareToken { expiresAt = DateTime.UtcNow.AddMinutes(offsetMinutes) }.IsExpired);
    }

    [Fact]
    public async Task StoreFileShareToken_PersistsFileScope()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();
        var file = NewFile(store.Id, user.Id);
        db.storeFiles.Add(file);
        await db.SaveChangesAsync();
        db.storeFileShareTokens.Add(new StoreFileShareToken { fileId = file.Id, createdByUserId = user.Id, token = "store-file", expiresAt = DateTime.UtcNow.AddHours(2) });
        await db.SaveChangesAsync();

        Assert.Equal(file.Id, db.storeFileShareTokens.Single().fileId);
    }

    [Fact]
    public async Task StoreFileTag_AttachAndDetach_Persists()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        await data.SaveAsync();
        var file = NewFile(store.Id, user.Id);
        var tag = new TagDefinition { storeId = store.Id, name = "Hero", color = FileTagColor.Blue };
        db.storeFiles.Add(file);
        db.tagDefinitions.Add(tag);
        await db.SaveChangesAsync();
        var assignment = new StoreFileTag { fileId = file.Id, tagDefinitionId = tag.Id };
        db.storeFileTags.Add(assignment);
        await db.SaveChangesAsync();
        Assert.Equal(tag.Id, db.storeFileTags.Single().tagDefinitionId);

        db.storeFileTags.Remove(assignment);
        await db.SaveChangesAsync();
        Assert.Empty(db.storeFileTags);
    }

    [Fact]
    public async Task TagDefinition_UpdatesSharedName()
    {
        await using var db = TestDbFactory.CreateDb();
        var store = new TestDataBuilder(db).AddStore();
        await db.SaveChangesAsync();
        var tag = new TagDefinition { storeId = store.Id, name = "Old", color = FileTagColor.Red };
        db.tagDefinitions.Add(tag);
        await db.SaveChangesAsync();

        tag.name = "New";
        await db.SaveChangesAsync();

        Assert.Equal("New", db.tagDefinitions.Single().name);
    }

    [Fact]
    public async Task StoreStorage_IsolatedByStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        await data.SaveAsync();
        var firstFile = NewFile(a.Id, user.Id);
        var secondFile = NewFile(b.Id, user.Id);
        secondFile.filePath = "stores/b/asset.png";
        db.storeFiles.AddRange(firstFile, secondFile);
        await db.SaveChangesAsync();

        var visibleToA = db.storeFiles.Where(f => f.storeId == a.Id).ToList();
        Assert.Single(visibleToA);
        Assert.All(visibleToA, f => Assert.Equal(a.Id, f.storeId));
    }

    [Fact]
    public async Task StoreTags_AreIsolatedByStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        db.tagDefinitions.AddRange(
            new TagDefinition { storeId = a.Id, name = "A", color = FileTagColor.Green },
            new TagDefinition { storeId = b.Id, name = "B", color = FileTagColor.Green });
        await data.SaveAsync();

        var tagsForA = db.tagDefinitions.Where(t => t.storeId == a.Id).ToList();
        Assert.Single(tagsForA);
        Assert.Equal("A", tagsForA[0].name);
    }
}
