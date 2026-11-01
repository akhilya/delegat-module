using System.Text.Json;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Models;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Tests;

public class AgentMcpAuthorizationTests
{
    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"resources/list\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"resources/read\",\"params\":{\"uri\":\"delegat://docs/index\"}}")]
    public async Task DiscoveryAndResourceReads_RequireAgentPermission(string requestJson)
    {
        await using var db = TestDbFactory.CreateDb();
        var controller = await CreateControllerAsync(db, []);
        using var document = JsonDocument.Parse(requestJson);

        var response = await controller.Post(document.RootElement.Clone(), default);

        var forbidden = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task WorkspaceView_ToolListOmitsMemoryWrites()
    {
        await using var db = TestDbFactory.CreateDb();
        var controller = await CreateControllerAsync(db, [Permissions.WorkspaceView]);
        using var document = JsonDocument.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        var response = await controller.Post(document.RootElement.Clone(), default);

        var ok = Assert.IsType<OkObjectResult>(response);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("memory_search", json, StringComparison.Ordinal);
        Assert.DoesNotContain("memory_save", json, StringComparison.Ordinal);
        Assert.DoesNotContain("memory_delete", json, StringComparison.Ordinal);
        Assert.Contains("create_file_download_link", json, StringComparison.Ordinal);
        Assert.DoesNotContain("read_chat_attachment", json, StringComparison.Ordinal);
    }

    private static async Task<AgentMcpController> CreateControllerAsync(
        DataContext db,
        IReadOnlyList<string> permissions)
    {
        var data = new TestDataBuilder(db);
        var user = data.AddUser("agent-mcp-auth");
        var store = data.AddStore("Agent MCP auth store", "RUB", "UTC", user);
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService
        {
            UserId = user.Id,
            SelectedStoreId = store.Id,
            User = user,
        };
        var http = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = http };
        var session = new AgentSessionResolver(
            currentUser, new StaticPermissionService(permissions), db, accessor);
        var controller = new AgentMcpController(
            session, null!, null!, new CabinetActionCatalog())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return controller;
    }

    private sealed class StaticPermissionService(IReadOnlyList<string> permissions) : IPermissionService
    {
        public Task EnsureRolesSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task MigrateLegacyAccessAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult(permissions);
        public Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult(new UserAccessDto());
        public Task SetUserAccessAsync(
            int userId,
            IReadOnlyList<string> roleNames,
            IReadOnlyList<string> permissionGrants,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureOwnerAccessAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task HydrateClaimsAsync(System.Security.Claims.ClaimsIdentity identity, int userId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public void InvalidateUserAccess(int userId) { }
    }
}
