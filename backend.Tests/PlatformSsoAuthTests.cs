using Delegat.Controllers;
using Delegat.Infrastructure.Data;
using Delegat.Models;
using Delegat.Services.PlatformLink;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Delegat.Tests;

public class PlatformSsoAuthTests
{
    [Fact]
    public async Task GetPlatformSso_WhenPlatformEmailHasNoWorkspaceUser_DoesNotSignInAdmin()
    {
        await using var db = TestDbFactory.CreateDb();
        var users = CreateUserManager(db);
        var admin = new User
        {
            UserName = "workspace-admin@example.test",
            Email = "workspace-admin@example.test",
            EmailConfirmed = true,
            isAdmin = true,
            isActive = true,
        };
        Assert.True((await users.CreateAsync(admin)).Succeeded);

        var services = new ServiceCollection();
        services.AddSingleton<IPlatformLinkClient>(new FakePlatformLinkClient());
        using var provider = services.BuildServiceProvider();
        var controller = new JwtAuthController(
            users,
            signInManager: null!,
            jwtService: null!,
            unitOfWork: null!,
            serviceProvider: provider,
            permissionService: null!,
            loginTickets: null!,
            cache: null!,
            db: db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.GetPlatformSso("one-time-code", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/signIn?ssoError=user_not_found", redirect.Url);
        Assert.False(controller.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private static UserManager<User> CreateUserManager(DataContext db) =>
        new(
            new UserStore<User, UserRole, DataContext, int>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            NullLogger<UserManager<User>>.Instance);

    private sealed class FakePlatformLinkClient : IPlatformLinkClient
    {
        public bool IsEnabled => true;
        public string? InstanceId => "inst_test";

        public Task<IReadOnlyList<PlatformCurrencyDto>> GetCurrenciesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlatformCurrencyDto>>([]);
        public Task<IReadOnlyList<PlatformTimezoneDto>> GetTimezonesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PlatformTimezoneDto>>([]);
        public Task ReportErrorAsync(PlatformErrorReportDto report, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendHeartbeatAsync(PlatformHeartbeatDto heartbeat, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PlatformEntitlementsDto?> GetEntitlementsAsync(CancellationToken ct = default) =>
            Task.FromResult<PlatformEntitlementsDto?>(null);
        public Task<PlatformMarketplaceDocDto?> GetMarketplaceDocAsync(string adapter, string? query, CancellationToken ct = default) =>
            Task.FromResult<PlatformMarketplaceDocDto?>(null);
        public Task<PlatformOzonGrantClaimDto?> ClaimOzonGrantAsync(string ticket, CancellationToken ct = default) =>
            Task.FromResult<PlatformOzonGrantClaimDto?>(null);
        public Task<PlatformOzonRefreshResult> RefreshOzonTokenAsync(string refreshToken, CancellationToken ct = default) =>
            Task.FromResult(new PlatformOzonRefreshResult(false, "not_configured", null, null, 0));
        public Task<PlatformInstanceSsoDto?> ExchangeInstanceSsoAsync(string code, CancellationToken ct = default) =>
            Task.FromResult<PlatformInstanceSsoDto?>(new PlatformInstanceSsoDto { Email = "tech@example.test" });
    }
}
