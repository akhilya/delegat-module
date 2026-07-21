using Delegat.Controllers;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Tests;

public class ApiKeysControllerTests
{
    private sealed class FakeApiRequestLogQueryService : IApiRequestLogQueryService
    {
        public Task<(List<ApiRequestLogSummaryDto> Items, int Total, int ErrorCount)> QueryAsync(
            int userId, ApiRequestLogQuery query, CancellationToken ct = default)
            => Task.FromResult((new List<ApiRequestLogSummaryDto>(), 0, 0));

        public Task<ApiRequestLogDetailDto?> GetDetailAsync(int userId, int id, CancellationToken ct = default)
            => Task.FromResult<ApiRequestLogDetailDto?>(null);
    }

    private static ApiKeysController CreateController(FakeCurrentUserService current, UserApiKeyService keys)
        => new(current, keys, new FakeApiRequestLogQueryService());

    [Fact]
    public async Task List_Unauthorized_WhenNoUser()
    {
        await using var db = TestDbFactory.CreateDb();
        var current = new FakeCurrentUserService { UserId = null };
        var controller = CreateController(current, new UserApiKeyService(db));

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.List(CancellationToken.None));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsPlaintextOnce()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("keys");
        await data.SaveAsync();

        var current = new FakeCurrentUserService { UserId = user.Id };
        var controller = CreateController(current, new UserApiKeyService(db));

        var result = await controller.Create(new ApiKeysController.CreateApiKeyRequest { name = "ERP" }, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("plaintext", json);
        Assert.Contains("dk_", json);
    }

    [Fact]
    public async Task List_ReturnsOnlyCallerKeys()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var a = data.AddUser("a");
        var b = data.AddUser("b");
        await data.SaveAsync();

        var svc = new UserApiKeyService(db);
        await svc.CreateAsync(a.Id, "a-key");
        await svc.CreateAsync(b.Id, "b-key");

        var controller = CreateController(new FakeCurrentUserService { UserId = a.Id }, svc);
        var result = await controller.List(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("a-key", json);
        Assert.DoesNotContain("b-key", json);
    }

    [Fact]
    public async Task Revoke_SucceedsForOwnKey()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("rev");
        await data.SaveAsync();

        var svc = new UserApiKeyService(db);
        await svc.CreateAsync(user.Id, "tmp");
        var keyId = db.userApiKeys.Single().Id;

        var controller = CreateController(new FakeCurrentUserService { UserId = user.Id }, svc);
        var result = await controller.Revoke(keyId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Revoke_OtherUsersKey_ThrowsNotFound()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var a = data.AddUser("a2");
        var b = data.AddUser("b2");
        await data.SaveAsync();

        var svc = new UserApiKeyService(db);
        await svc.CreateAsync(a.Id, "secret");
        var keyId = db.userApiKeys.Single().Id;

        var controller = CreateController(new FakeCurrentUserService { UserId = b.Id }, svc);
        await Assert.ThrowsAsync<ApiException>(() => controller.Revoke(keyId, CancellationToken.None));
    }
}
