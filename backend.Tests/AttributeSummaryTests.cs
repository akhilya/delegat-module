using System.Text.Json;
using Delegat.Controllers;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AttributeSummaryTests
{
    [Fact]
    public async Task CategorySummary_ExcludesDictionariesButDetailPreservesThem()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "Shoes", "Unsaved form stays separate", hasSizes: true);
        var options = JsonSerializer.Serialize(Enumerable.Range(0, 10000).Select(i => $"Option {i}"));
        var attribute = data.AddCategoryAttribute(category.Id, "Color", sortOrder: 2, options: options);
        data.AddCategoryAttribute(category.Id, "Material", sortOrder: 1);
        var otherCategory = data.AddCategory(store.Id);
        var foreignAttribute = data.AddCategoryAttribute(otherCategory.Id);
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        var current = new FakeCurrentUserService { SelectedStoreId = store.Id };
        var controller = new CategoryController(current, TestDbFactory.CreateUnitOfWork(db), null!, NullLogger<CategoryController>.Instance, db);

        var summary = Assert.IsType<Category.EditorSummaryDTO>(Assert.IsType<OkObjectResult>(
            await controller.GetCategorySummary(category.Id)).Value);
        Assert.True(summary.hasSizes);
        Assert.Equal("Shoes", summary.name);
        Assert.Equal(new[] { "Material", "Color" }, summary.attributes.Select(a => a.name));
        var json = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("options", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(json.Length < 2000);
        Assert.Empty(db.ChangeTracker.Entries<CategoryAttribute>());

        var rows = Assert.IsType<List<AttributeDefinitionSummary>>(Assert.IsType<OkObjectResult>(
            await controller.GetCategoryAttributeSummaries(category.Id)).Value);
        Assert.Equal(2, rows.Count);
        var detail = Assert.IsType<CategoryAttribute.AttributeDTO>(Assert.IsType<OkObjectResult>(
            await controller.GetCategoryAttribute(category.Id, attribute.Id)).Value);
        Assert.Equal(options, detail.options);
        var error = await Assert.ThrowsAsync<ApiException>(() => controller.GetCategoryAttribute(category.Id, foreignAttribute.Id));
        Assert.Equal("attribute_not_found", error.Code);

        current.SelectedStoreId = data.AddStore().Id;
        await data.SaveAsync();
        Assert.Equal("category_not_found", (await Assert.ThrowsAsync<ApiException>(() =>
            controller.GetCategorySummary(category.Id))).Code);
        Assert.Equal("category_not_found", (await Assert.ThrowsAsync<ApiException>(() =>
            controller.GetCategoryAttributeSummaries(category.Id))).Code);
    }

    [Fact]
    public async Task GlobalSummaries_AreStoreScopedAndExcludeDictionaries()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var otherStore = data.AddStore();
        var attribute = data.AddStoreAttribute(store.Id, options: "[\"Red\",\"Blue\"]");
        attribute.isRequired = true;
        attribute.showOnProductPage = false;
        data.AddStoreAttribute(otherStore.Id);
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        var current = new FakeCurrentUserService { SelectedStoreId = store.Id };
        var controller = new StoreAttributeController(
            current, TestDbFactory.CreateUnitOfWork(db), null!, NullLogger<StoreAttributeController>.Instance, db);

        var summaries = Assert.IsType<List<AttributeDefinitionSummary>>(Assert.IsType<OkObjectResult>(
            await controller.GetStoreAttributeSummaries()).Value);
        Assert.Equal(attribute.Id, Assert.Single(summaries).id);
        Assert.True(summaries[0].showOnProductPage);
        Assert.DoesNotContain("options", JsonSerializer.Serialize(summaries), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ChangeTracker.Entries<StoreAttribute>());
        var detail = Assert.IsType<StoreAttribute.AttributeDTO>(Assert.IsType<OkObjectResult>(
            await controller.GetStoreAttribute(attribute.Id)).Value);
        Assert.Equal(attribute.options, detail.options);
        current.SelectedStoreId = null;
        Assert.Equal("store_not_selected", (await Assert.ThrowsAsync<ApiException>(() =>
            controller.GetStoreAttributeSummaries())).Code);
    }

    [Fact]
    public async Task CategoryList_PreservesCountsWithoutMaterializingAttributes()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        data.AddCategoryAttribute(category.Id, options: "[\"Red\"]");
        data.AddCategoryAttribute(category.Id);
        data.AddProduct(store.Id, categoryId: category.Id);
        data.AddProduct(store.Id, categoryId: category.Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        var controller = new CategoryController(new FakeCurrentUserService { SelectedStoreId = store.Id },
            TestDbFactory.CreateUnitOfWork(db), null!, NullLogger<CategoryController>.Instance, db);
        var categories = Assert.IsType<List<Category.CategoryDTO>>(Assert.IsType<OkObjectResult>(await controller.GetAllCategories()).Value);
        Assert.Equal(2, Assert.Single(categories).attributesCount);
        Assert.Equal(1, categories[0].productsCount);
        Assert.Empty(db.ChangeTracker.Entries<CategoryAttribute>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SummarySql_DoesNotSelectDictionaryOrValidationColumns(bool global)
    {
        // Translation only: no connection or running MySQL instance is needed.
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseMySql("Server=localhost;Database=translation_only;User=test;Password=test",
                new MySqlServerVersion(new Version(8, 0, 36))).Options;
        using var db = new DataContext(options);
        var sql = global
            ? db.storeAttributes.Select(AttributeDefinitionSummary.FromStore).ToQueryString()
            : db.categoryAttributes.Select(AttributeDefinitionSummary.FromCategory).ToQueryString();
        foreach (var column in new[] { "options", "defaultValue", "description", "validationPattern", "maxLength" })
            Assert.DoesNotContain($"`{column}`", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`name`", sql);
    }
}
