using System.Text.Json;
using Delegat.Controllers;
using Delegat.Enums;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class CategoryAttributeOptionsTests
{
    [Fact]
    public async Task GetOptionsSearchesDictionaryAndResolvesSelectedLabelsOutsideCurrentPage()
    {
        var (db, data, current, controller) = CreateController();
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.Select);
        attribute.options = JsonSerializer.Serialize(new[]
        {
            new { value = "ru", label = "Russia" },
            new { value = "de", label = "Germany" },
            new { value = "us", label = "United States" }
        });
        await data.SaveAsync();
        current.SelectedStoreId = store.Id;

        var result = await controller.GetCategoryAttributeOptions(
            category.Id,
            attribute.Id,
            query: "rus",
            page: 1,
            pageSize: 20,
            selectedValues: "[\"de\",\"ru\"]");

        var page = Assert.IsType<CategoryController.AttributeOptionPage>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, page.Total);
        Assert.False(page.HasMore);
        Assert.Equal(new CategoryController.AttributeOption("ru", "Russia"), Assert.Single(page.Items));
        Assert.Equal(
            [new CategoryController.AttributeOption("de", "Germany"), new("ru", "Russia")],
            page.SelectedOptions);
    }

    [Fact]
    public async Task GetOptionsCapsPageSizeAndDoesNotReturnTheWholeDictionary()
    {
        var (db, data, current, controller) = CreateController();
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.MultiSelect);
        attribute.options = JsonSerializer.Serialize(Enumerable.Range(0, 125)
            .Select(index => new { value = $"v-{index:D3}", label = $"Option {index:D3}" }));
        await data.SaveAsync();
        current.SelectedStoreId = store.Id;

        var result = await controller.GetCategoryAttributeOptions(
            category.Id,
            attribute.Id,
            page: 1,
            pageSize: 1000);

        var page = Assert.IsType<CategoryController.AttributeOptionPage>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(125, page.Total);
        Assert.Equal(100, page.Items.Count);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task GetOptionsRejectsCategoriesOutsideTheSelectedStore()
    {
        var (db, data, current, controller) = CreateController();
        var user = data.AddUser();
        var selectedStore = data.AddStore(members: [user]);
        var foreignStore = data.AddStore();
        var category = data.AddCategory(foreignStore.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.Select);
        attribute.options = "[\"one\"]";
        await data.SaveAsync();
        current.SelectedStoreId = selectedStore.Id;

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            controller.GetCategoryAttributeOptions(category.Id, attribute.Id));

        Assert.Equal("category_not_found", error.Code);
    }

    private static (DataContext Db, TestDataBuilder Data, FakeCurrentUserService Current, CategoryController Controller)
        CreateController()
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
}
