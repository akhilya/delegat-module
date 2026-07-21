using System.Security.Claims;
using Delegat.Infrastructure.Auth;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class PermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task PlatformPermission_SucceedsWithoutStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var handler = CreateHandler(db, new DefaultHttpContext());
        var user = PrincipalWith(Permissions.UsersManage, userId: 1);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.UsersManage, requireStoreMembership: false)],
            user,
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task MissingPermission_DoesNotSucceed()
    {
        await using var db = TestDbFactory.CreateDb();
        var handler = CreateHandler(db, new DefaultHttpContext());
        var user = PrincipalWith(Permissions.ReferenceRead, userId: 1);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.UsersManage, requireStoreMembership: false)],
            user,
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task StoreScoped_DeniesNonMember()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var alice = data.AddUser("alice");
        var bob = data.AddUser("bob");
        var store = data.AddStore("S", members: bob);
        await data.SaveAsync();

        var http = new DefaultHttpContext();
        http.Request.Path = $"/stores/{store.Id}";
        http.Request.RouteValues["id"] = store.Id.ToString();

        var handler = CreateHandler(db, http);
        var user = PrincipalWith(Permissions.CatalogManage, userId: alice.Id);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.CatalogManage, requireStoreMembership: true)],
            user,
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task StoreScoped_AllowsStoresCreateWithoutMembership()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var alice = data.AddUser("alice");
        var store = data.AddStore("S"); // alice is not a member
        await data.SaveAsync();

        var http = new DefaultHttpContext();
        http.Request.Path = $"/stores/{store.Id}/detail";
        http.Request.RouteValues["id"] = store.Id.ToString();

        var handler = CreateHandler(db, http);
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("userId", alice.Id.ToString()));
        identity.AddClaim(new Claim(Permissions.ClaimType, Permissions.StoreSettingsManage));
        identity.AddClaim(new Claim(Permissions.ClaimType, Permissions.StoresCreate));
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.StoreSettingsManage, requireStoreMembership: true)],
            user,
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task StoreScoped_AllowsMemberViaRoute()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var alice = data.AddUser("alice");
        var store = data.AddStore("S", members: alice);
        await data.SaveAsync();

        var http = new DefaultHttpContext();
        http.Request.Path = $"/stores/{store.Id}/members";
        http.Request.RouteValues["id"] = store.Id.ToString();

        var handler = CreateHandler(db, http);
        var user = PrincipalWith(Permissions.StoreMembersManage, userId: alice.Id);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.StoreMembersManage, requireStoreMembership: true)],
            user,
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task StoreScoped_AllowsMemberViaSelectedStoreCookie()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var alice = data.AddUser("alice");
        var store = data.AddStore("S", members: alice);
        await data.SaveAsync();

        var http = new DefaultHttpContext();
        http.Request.Path = "/products";
        http.Request.Headers.Cookie = $"selected_store_id={store.Id}";

        var handler = CreateHandler(db, http);
        var user = PrincipalWith(Permissions.CatalogManage, userId: alice.Id);
        var context = new AuthorizationHandlerContext(
            [new PermissionRequirement(Permissions.CatalogManage, requireStoreMembership: true)],
            user,
            resource: null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static PermissionAuthorizationHandler CreateHandler(DataContext db, HttpContext http)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var accessor = new HttpContextAccessor { HttpContext = http };
        return new PermissionAuthorizationHandler(scopeFactory, accessor);
    }

    private static ClaimsPrincipal PrincipalWith(string permission, int userId)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("userId", userId.ToString()));
        identity.AddClaim(new Claim(Permissions.ClaimType, permission));
        return new ClaimsPrincipal(identity);
    }
}

public class PermissionPolicyProviderTests
{
    [Fact]
    public async Task BuildsPlatformAndStorePolicies()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions());
        var provider = new PermissionPolicyProvider(options);

        var platform = await provider.GetPolicyAsync(PermissionPolicyProvider.PolicyName(Permissions.UsersManage));
        var store = await provider.GetPolicyAsync(PermissionPolicyProvider.StorePolicyName(Permissions.CatalogManage));

        Assert.NotNull(platform);
        Assert.NotNull(store);
        Assert.Contains(platform!.Requirements, r => r is PermissionRequirement pr
            && pr.Permission == Permissions.UsersManage && !pr.RequireStoreMembership);
        Assert.Contains(store!.Requirements, r => r is PermissionRequirement pr
            && pr.Permission == Permissions.CatalogManage && pr.RequireStoreMembership);
    }
}

public class EffectivePermissionsUnionTests
{
    [Fact]
    public void Union_RolePlusUserGrant_IncludesBoth()
    {
        var fromRoles = AppRoles.RolePermissions[AppRoles.Viewer].ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grants = new[] { Permissions.CatalogManage };

        var effective = new HashSet<string>(fromRoles, StringComparer.OrdinalIgnoreCase);
        foreach (var g in grants)
            effective.Add(g);

        Assert.Contains(Permissions.ReferenceRead, effective);
        Assert.Contains(Permissions.CatalogManage, effective);
        Assert.DoesNotContain(Permissions.UsersManage, effective);
    }

    [Fact]
    public void Operator_DoesNotIncludePlatformManage()
    {
        var ops = AppRoles.RolePermissions[AppRoles.Operator];
        Assert.DoesNotContain(Permissions.UsersManage, ops);
        Assert.DoesNotContain(Permissions.RolesManage, ops);
        Assert.Contains(Permissions.CatalogManage, ops);
    }
}
