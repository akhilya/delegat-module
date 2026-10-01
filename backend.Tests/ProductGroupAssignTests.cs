using Delegat.Controllers;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ProductGroupAssignTests
{
    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null) =>
            Task.CompletedTask;

        public Task LogAsync(string operationId, int storeId, string level, string message) =>
            Task.CompletedTask;

        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) =>
            Task.CompletedTask;

        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) =>
            Task.CompletedTask;

        public Task UpdateStatusAsync(string operationId, int storeId, string status) =>
            Task.CompletedTask;
    }

    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService user, ProductController controller)
        CreateSut()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = new FakeCurrentUserService();
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uniqueness = new AttributeUniquenessService(db);
        var controller = new ProductController(
            user,
            uow,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<ProductController>.Instance,
            db,
            new ExcelProductService(),
            new QuotaService(uow, db),
            new NoopOperationLogger(),
            uniqueness,
            new ProductBulkUpdateService(db, uow, uniqueness));
        return (db, data, user, controller);
    }

    private static async Task<(Store store, Product product, ProductGroup group)> SeedAsync(
        TestDataBuilder data,
        FakeCurrentUserService user)
    {
        var member = data.AddUser();
        var store = data.AddStore(members: member);
        var product = data.AddProduct(store.Id, name: "Card");
        var group = data.AddGroup(store.Id, "Target");
        await data.SaveAsync();
        user.UserId = member.Id;
        user.SelectedStoreId = store.Id;
        user.User = member;
        return (store, product, group);
    }

    [Fact]
    public async Task PatchGroup_MovesProductIntoGroup()
    {
        var (db, data, user, controller) = CreateSut();
        var (_, product, group) = await SeedAsync(data, user);

        var result = await controller.SetProductGroup(
            product.Id,
            new ProductController.SetProductGroupInput { groupId = group.Id });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            group.Id,
            (await db.productGroupMembers.SingleAsync(m => m.productId == product.Id)).groupId);
    }

    [Fact]
    public async Task PatchGroup_MovesProductOutOfPreviousGroup()
    {
        var (db, data, user, controller) = CreateSut();
        var (store, product, target) = await SeedAsync(data, user);
        var source = data.AddGroup(store.Id, "Source", product);
        await data.SaveAsync();

        await controller.SetProductGroup(
            product.Id,
            new ProductController.SetProductGroupInput { groupId = target.Id });

        Assert.False(await db.productGroupMembers.AnyAsync(m => m.groupId == source.Id));
        Assert.Equal(
            target.Id,
            (await db.productGroupMembers.SingleAsync(m => m.productId == product.Id)).groupId);
    }

    [Fact]
    public async Task PatchGroup_NullGroupId_RemovesMembership()
    {
        var (db, data, user, controller) = CreateSut();
        var (store, product, _) = await SeedAsync(data, user);
        data.AddGroup(store.Id, "Has it", product);
        await data.SaveAsync();

        var result = await controller.SetProductGroup(
            product.Id,
            new ProductController.SetProductGroupInput { groupId = null });

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(await db.productGroupMembers.ToListAsync());
    }

    [Fact]
    public async Task PatchGroup_MissingGroup_IsNotFound()
    {
        var (_, data, user, controller) = CreateSut();
        var (_, product, _) = await SeedAsync(data, user);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.SetProductGroup(
                product.Id,
                new ProductController.SetProductGroupInput { groupId = 999 }));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("group_not_found", ex.Code);
    }
}
