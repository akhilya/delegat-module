using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class StoreAttributeDomainTests
{
    private static (DataContext db, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db));
    }

    [Fact]
    public void AddFill_SetsStoreAttributeFields()
    {
        var attribute = new StoreAttribute();
        attribute.AddFill(new StoreAttribute.AddForm
        {
            storeId = 5, name = "Brand", key = "brand", type = AttributeType.Text,
            isRequired = true, isMultiline = true, showOnProductPage = false, sortOrder = 3,
            options = "[]", defaultValue = "Acme", description = "Manufacturer", descriptionInTooltip = true
        });
        Assert.Equal(5, attribute.storeId);
        Assert.Equal("brand", attribute.key);
        Assert.True(attribute.isRequired);
        Assert.True(attribute.isMultiline);
        Assert.True(attribute.showOnProductPage);
        Assert.Equal(3, attribute.sortOrder);
    }

    [Fact]
    public void AddFill_CopiesValueConstraints()
    {
        var attribute = new StoreAttribute();
        attribute.AddFill(new StoreAttribute.AddForm
        {
            storeId = 1, name = "Color", key = "color", type = AttributeType.Select,
            allowCustomValues = true, maxValues = 3, maxLength = 40,
            minValue = 1.5m, maxValue = 10m, validationPattern = "^[A-Z]+$"
        });
        Assert.True(attribute.allowCustomValues);
        Assert.Equal(3, attribute.maxValues);
        Assert.Equal(40, attribute.maxLength);
        Assert.Equal(1.5m, attribute.minValue);
        Assert.Equal(10m, attribute.maxValue);
        Assert.Equal("^[A-Z]+$", attribute.validationPattern);

        var dto = ((IMapToDTO<StoreAttribute.AttributeDTO>)attribute).MapToDTO(null!);
        Assert.True(dto.allowCustomValues);
        Assert.Equal(3, dto.maxValues);
        Assert.Equal(40, dto.maxLength);
        Assert.Equal("^[A-Z]+$", dto.validationPattern);
    }

    [Fact]
    public void UpdateFill_CopiesValueConstraints()
    {
        var attribute = new StoreAttribute
        {
            name = "Color", key = "color", type = AttributeType.Select,
            allowCustomValues = false, maxValues = 1
        };
        attribute.UpdateFill(new StoreAttribute.UpdateForm
        {
            allowCustomValues = true, maxValues = 4, maxLength = 12,
            minValue = 0m, maxValue = 9m, validationPattern = "x"
        });
        Assert.True(attribute.allowCustomValues);
        Assert.Equal(4, attribute.maxValues);
        Assert.Equal(12, attribute.maxLength);
        Assert.Equal(0m, attribute.minValue);
        Assert.Equal(9m, attribute.maxValue);
        Assert.Equal("x", attribute.validationPattern);

        attribute.UpdateFill(new StoreAttribute.UpdateForm
        {
            allowCustomValues = false, maxValues = null, maxLength = null,
            minValue = null, maxValue = null, validationPattern = null
        });
        Assert.False(attribute.allowCustomValues);
        Assert.Null(attribute.maxValues);
        Assert.Null(attribute.maxLength);
        Assert.Null(attribute.validationPattern);
    }

    [Fact]
    public void UpdateFill_UpdatesDefinedFields()
    {
        var attribute = new StoreAttribute { name = "Old", key = "old", type = AttributeType.Text, options = "old" };
        attribute.UpdateFill(new StoreAttribute.UpdateForm
        {
            name = "New", key = "new", type = AttributeType.Select, isRequired = true,
            showOnProductPage = false, sortOrder = 8, options = "[\"A\"]", defaultValue = "A",
            description = "new description", descriptionInTooltip = true
        });
        Assert.Equal("New", attribute.name);
        Assert.Equal("new", attribute.key);
        Assert.Equal(AttributeType.Select, attribute.type);
        Assert.True(attribute.isRequired);
        Assert.True(attribute.showOnProductPage);
        Assert.Equal(8, attribute.sortOrder);
        Assert.True(attribute.descriptionInTooltip);
    }

    [Fact]
    public void UpdateFill_EmptyNameAndKeyKeepExistingValues()
    {
        var attribute = new StoreAttribute { name = "Name", key = "key" };
        attribute.UpdateFill(new StoreAttribute.UpdateForm { name = "", key = "" });
        Assert.Equal("Name", attribute.name);
        Assert.Equal("key", attribute.key);
    }

    [Theory]
    [InlineData(AttributeType.Text)]
    [InlineData(AttributeType.Boolean)]
    [InlineData(AttributeType.MultiSelect)]
    public void AddFill_PreservesAttributeType(AttributeType type)
    {
        var attribute = new StoreAttribute();
        attribute.AddFill(new StoreAttribute.AddForm { storeId = 1, name = "X", key = "x", type = type });
        Assert.Equal(type, attribute.type);
    }

    [Fact]
    public async Task StoreQuery_IsolatesAttributes()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddStoreAttribute(a.Id, "A");
        data.AddStoreAttribute(b.Id, "B");
        await data.SaveAsync();
        Assert.Equal("A", (await db.storeAttributes.SingleAsync(attribute => attribute.storeId == a.Id)).name);
    }

    [Fact]
    public async Task StoreAttributes_OrderBySortOrder()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        data.AddStoreAttribute(store.Id, "Third", sortOrder: 2);
        data.AddStoreAttribute(store.Id, "First", sortOrder: 0);
        data.AddStoreAttribute(store.Id, "Second", sortOrder: 1);
        await data.SaveAsync();
        var names = await db.storeAttributes.Where(a => a.storeId == store.Id).OrderBy(a => a.sortOrder).Select(a => a.name).ToListAsync();
        Assert.Equal(new[] { "First", "Second", "Third" }, names);
    }

    [Fact]
    public async Task Reorder_ChangesPersistedSortOrders()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var first = data.AddStoreAttribute(store.Id, "First", sortOrder: 0);
        var second = data.AddStoreAttribute(store.Id, "Second", sortOrder: 1);
        await data.SaveAsync();
        first.sortOrder = 1;
        second.sortOrder = 0;
        await db.SaveChangesAsync();
        Assert.Equal(second.Id, (await db.storeAttributes.Where(a => a.storeId == store.Id).OrderBy(a => a.sortOrder).FirstAsync()).Id);
    }

    [Fact]
    public async Task Delete_RemovesAttribute()
    {
        var (db, data) = Create();
        var attribute = data.AddStoreAttribute(data.AddStore().Id);
        await data.SaveAsync();
        db.storeAttributes.Remove(attribute);
        await db.SaveChangesAsync();
        Assert.Empty(db.storeAttributes);
    }

    [Fact]
    public async Task DuplicateKey_IsDetectedWithinStore()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        data.AddStoreAttribute(store.Id, "Brand");
        await data.SaveAsync();
        Assert.True(await db.storeAttributes.AnyAsync(a => a.storeId == store.Id && a.key == "store_attr_1"));
    }

    [Fact]
    public async Task SameKey_InAnotherStoreIsNotConflict()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddStoreAttribute(a.Id, "Brand");
        await data.SaveAsync();
        Assert.False(await db.storeAttributes.AnyAsync(x => x.storeId == b.Id && x.key == "store_attr_1"));
    }
}
