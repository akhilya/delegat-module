using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductGroupDomainTests
{
    private static (DataContext db, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db));
    }

    [Fact]
    public void AddFill_SetsGroupFields()
    {
        var group = new ProductGroup();
        group.AddFill(new ProductGroup.AddForm { name = "Variants", description = "Same model", storeId = 6 });
        Assert.Equal("Variants", group.name);
        Assert.Equal("Same model", group.description);
        Assert.Equal(6, group.storeId);
    }

    [Fact]
    public void UpdateFill_ChangesNameAndDescription()
    {
        var group = new ProductGroup { name = "Old", description = "old" };
        group.UpdateFill(new ProductGroup.UpdateForm { name = "New", description = "new" });
        Assert.Equal("New", group.name);
        Assert.Equal("new", group.description);
        Assert.NotNull(group.updatedAt);
    }

    [Fact]
    public void UpdateFill_NullDescriptionKeepsExistingDescription()
    {
        var group = new ProductGroup { name = "Group", description = "kept" };
        group.UpdateFill(new ProductGroup.UpdateForm { name = "Renamed" });
        Assert.Equal("kept", group.description);
    }

    [Fact]
    public async Task StoreQuery_IsolatesGroups()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddGroup(a.Id, "A group");
        data.AddGroup(b.Id, "B group");
        await data.SaveAsync();
        Assert.Equal("A group", (await db.productGroups.SingleAsync(g => g.storeId == a.Id)).name);
    }

    [Fact]
    public async Task CreateAndDelete_RemovesGroup()
    {
        var (db, data) = Create();
        var group = data.AddGroup(data.AddStore().Id);
        await data.SaveAsync();
        db.productGroups.Remove(group);
        await db.SaveChangesAsync();
        Assert.Empty(db.productGroups);
    }

    [Fact]
    public void MemberAddFill_UsesGroupIdAndProductId()
    {
        var member = new ProductGroupMember();
        member.AddFill(new ProductGroupMember.AddForm { groupId = 8, productId = 9, sortOrder = 2 });
        Assert.Equal(8, member.groupId);
        Assert.Equal(9, member.productId);
        Assert.Equal(2, member.sortOrder);
    }

    [Fact]
    public void MemberUpdateFill_ChangesSortOrder()
    {
        var member = new ProductGroupMember { groupId = 1, productId = 2, sortOrder = 0 };
        member.UpdateFill(new ProductGroupMember.UpdateForm { sortOrder = 4 });
        Assert.Equal(4, member.sortOrder);
    }

    [Fact]
    public async Task AddMembers_PersistsGroupMemberships()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var p1 = data.AddProduct(store.Id);
        var p2 = data.AddProduct(store.Id);
        var group = data.AddGroup(store.Id, null, p1, p2);
        await data.SaveAsync();
        var members = await db.productGroupMembers.Where(m => m.groupId == group.Id).OrderBy(m => m.sortOrder).ToListAsync();
        Assert.Equal(new[] { p1.Id, p2.Id }, members.Select(m => m.productId));
    }

    [Fact]
    public async Task RemoveMember_LeavesOtherMembers()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var p1 = data.AddProduct(store.Id);
        var p2 = data.AddProduct(store.Id);
        var group = data.AddGroup(store.Id, null, p1, p2);
        await data.SaveAsync();
        db.productGroupMembers.Remove(await db.productGroupMembers.SingleAsync(m => m.groupId == group.Id && m.productId == p1.Id));
        await db.SaveChangesAsync();
        Assert.Equal(p2.Id, (await db.productGroupMembers.SingleAsync()).productId);
    }

    [Fact]
    public async Task GroupMemberQuery_IsolatesGroups()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var first = data.AddProduct(store.Id);
        var second = data.AddProduct(store.Id);
        var a = data.AddGroup(store.Id, "A", first);
        data.AddGroup(store.Id, "B", second);
        await data.SaveAsync();
        Assert.Equal(first.Id, (await db.productGroupMembers.SingleAsync(m => m.groupId == a.Id)).productId);
    }

    [Fact]
    public async Task ProductMembershipConflict_IsDetected()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        data.AddGroup(store.Id, "Existing", product);
        await data.SaveAsync();
        Assert.True(await db.productGroupMembers.AnyAsync(m => m.productId == product.Id));
    }
}
