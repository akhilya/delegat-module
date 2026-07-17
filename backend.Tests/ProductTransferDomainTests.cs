using Delegat.Models;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class ProductTransferDomainTests
{
    private static PendingProductTransfer NewTransfer(int sourceProductId, int sourceStoreId, int targetStoreId, int authorId) => new()
    {
        sourceProductId = sourceProductId, sourceStoreId = sourceStoreId, targetStoreId = targetStoreId,
        authorUserId = authorId, batchId = Guid.NewGuid().ToString("N")
    };

    [Fact]
    public async Task PendingTransfer_Create_DefaultsToPendingClone()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var source = data.AddStore("Source");
        var target = data.AddStore("Target");
        var product = data.AddProduct(source.Id);
        var transfer = NewTransfer(product.Id, source.Id, target.Id, author.Id);
        db.pendingProductTransfers.Add(transfer);
        await data.SaveAsync();

        var saved = db.pendingProductTransfers.Single();
        Assert.Equal(ProductTransferStatus.Pending, saved.status);
        Assert.Equal("clone", saved.mode);
        Assert.Equal("match", saved.categoryAction);
        Assert.Equal("none", saved.groupAction);
    }

    [Fact]
    public async Task PendingTransfer_PersistsRequestFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var source = data.AddStore();
        var target = data.AddStore();
        var product = data.AddProduct(source.Id);
        var transfer = NewTransfer(product.Id, source.Id, target.Id, author.Id);
        transfer.mode = "move";
        transfer.newSku = "NEW-001";
        transfer.categoryAction = "use";
        transfer.categoryId = 4;
        transfer.groupAction = "create";
        transfer.groupName = "New group";
        transfer.confirmBreakLinks = true;
        transfer.syncMissingCategoryAttributes = true;
        db.pendingProductTransfers.Add(transfer);
        await data.SaveAsync();

        var saved = db.pendingProductTransfers.Single();
        Assert.Equal("move", saved.mode);
        Assert.Equal("NEW-001", saved.newSku);
        Assert.Equal(4, saved.categoryId);
        Assert.Equal("create", saved.groupAction);
        Assert.Equal("New group", saved.groupName);
        Assert.True(saved.confirmBreakLinks);
        Assert.True(saved.syncMissingCategoryAttributes);
    }

    [Fact]
    public async Task PendingTransfer_Start_TransitionsToInProgress()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var source = data.AddStore();
        var target = data.AddStore();
        var transfer = NewTransfer(data.AddProduct(source.Id).Id, source.Id, target.Id, author.Id);
        db.pendingProductTransfers.Add(transfer);
        await data.SaveAsync();

        transfer.status = ProductTransferStatus.InProgress;
        transfer.startedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Equal(ProductTransferStatus.InProgress, db.pendingProductTransfers.Single().status);
        Assert.NotNull(db.pendingProductTransfers.Single().startedAt);
    }

    [Fact]
    public async Task PendingTransfer_Complete_SavesCreatedProductAndFinishTime()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var source = data.AddStore();
        var target = data.AddStore();
        var original = data.AddProduct(source.Id);
        var created = data.AddProduct(target.Id);
        var transfer = NewTransfer(original.Id, source.Id, target.Id, author.Id);
        db.pendingProductTransfers.Add(transfer);
        await data.SaveAsync();

        transfer.status = ProductTransferStatus.Completed;
        transfer.newProductId = created.Id;
        transfer.finishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var saved = db.pendingProductTransfers.Single();
        Assert.Equal(ProductTransferStatus.Completed, saved.status);
        Assert.Equal(created.Id, saved.newProductId);
        Assert.NotNull(saved.finishedAt);
    }

    [Theory]
    [InlineData(ProductTransferStatus.Failed)]
    [InlineData(ProductTransferStatus.Partial)]
    [InlineData(ProductTransferStatus.Cancelled)]
    public async Task PendingTransfer_TerminalStatuses_Persist(ProductTransferStatus status)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var source = data.AddStore();
        var target = data.AddStore();
        var transfer = NewTransfer(data.AddProduct(source.Id).Id, source.Id, target.Id, author.Id);
        db.pendingProductTransfers.Add(transfer);
        await data.SaveAsync();

        transfer.status = status;
        transfer.lastError = status is ProductTransferStatus.Failed or ProductTransferStatus.Partial ? "adapter failure" : null;
        transfer.finishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Equal(status, db.pendingProductTransfers.Single().status);
        Assert.NotNull(db.pendingProductTransfers.Single().finishedAt);
    }

    [Fact]
    public async Task PendingTransfer_QueryBySourceStore_ExcludesOtherStores()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        var c = data.AddStore("C");
        db.pendingProductTransfers.AddRange(
            NewTransfer(data.AddProduct(a.Id).Id, a.Id, b.Id, author.Id),
            NewTransfer(data.AddProduct(c.Id).Id, c.Id, b.Id, author.Id));
        await data.SaveAsync();

        var fromA = db.pendingProductTransfers.Where(t => t.sourceStoreId == a.Id).ToList();
        Assert.Single(fromA);
        Assert.Equal(a.Id, fromA[0].sourceStoreId);
    }

    [Fact]
    public async Task PendingTransfer_QueryByBatch_ReturnsOnlyMatchingBatch()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var author = data.AddUser();
        var source = data.AddStore();
        var target = data.AddStore();
        var first = NewTransfer(data.AddProduct(source.Id).Id, source.Id, target.Id, author.Id);
        first.batchId = "batch-a";
        var second = NewTransfer(data.AddProduct(source.Id).Id, source.Id, target.Id, author.Id);
        second.batchId = "batch-b";
        db.pendingProductTransfers.AddRange(first, second);
        await data.SaveAsync();

        Assert.Single(db.pendingProductTransfers.Where(t => t.batchId == "batch-a"));
    }
}
