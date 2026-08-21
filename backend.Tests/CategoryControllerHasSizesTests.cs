using Delegat.Controllers;
using Delegat.Infrastructure.Data;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class CategoryControllerHasSizesTests
{
    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService current, CategoryController controller)
        Create()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var current = new FakeCurrentUserService();
        var controller = new CategoryController(
            current,
            TestDbFactory.CreateUnitOfWork(db),
            null!,
            NullLogger<CategoryController>.Instance,
            db);
        return (db, data, current, controller);
    }

    [Fact]
    public async Task GetCategory_IncludesHasSizes()
    {
        var (db, data, current, controller) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var category = data.AddCategory(store.Id, "Shoes");
        category.hasSizes = true;
        await data.SaveAsync();
        current.UserId = user.Id;
        current.SelectedStoreId = store.Id;

        var result = await controller.GetCategory(category.Id);

        var dto = Assert.IsType<Category.CategoryDetailDTO>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(dto.hasSizes);
    }

    [Fact]
    public async Task GetAllCategories_IncludesHasSizes()
    {
        var (db, data, current, controller) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var category = data.AddCategory(store.Id, "Shoes");
        category.hasSizes = true;
        await data.SaveAsync();
        current.UserId = user.Id;
        current.SelectedStoreId = store.Id;

        var result = await controller.GetAllCategories();

        var list = Assert.IsType<List<Category.CategoryDTO>>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(Assert.Single(list).hasSizes);
    }

    [Fact]
    public async Task UpdateCategory_PersistsHasSizesForGet()
    {
        var (db, data, current, controller) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var category = data.AddCategory(store.Id, "Shoes");
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        current.UserId = user.Id;
        current.SelectedStoreId = store.Id;

        await controller.UpdateCategory(
            category.Id,
            new Category.UpdateForm { name = "Shoes", hasSizes = true });

        var result = await controller.GetCategory(category.Id);
        var dto = Assert.IsType<Category.CategoryDetailDTO>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(dto.hasSizes);
    }
}
