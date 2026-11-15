using System.Security.Claims;
using Delegat.Hubs;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;

namespace Delegat.Tests.Hubs;

public class OperationHubTests
{
    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string Group)> Added { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly FeatureCollection _features = new();

        public FakeHubCallerContext(ClaimsPrincipal user, HttpContext httpContext)
        {
            User = user;
            _features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
        }

        public override string ConnectionId => "conn-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private static User MakeUser(int id, bool active = true) => new()
    {
        Id = id,
        UserName = $"user{id}",
        Email = $"user{id}@example.com",
        NormalizedUserName = $"USER{id}",
        NormalizedEmail = $"USER{id}@EXAMPLE.COM",
        isActive = active,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static Store MakeStore(int id, params User[] members) => new()
    {
        Id = id,
        name = $"Store {id}",
        baseCurrency = "RUB",
        members = members.ToList()
    };

    private static async Task<RecordingGroupManager> ConnectAsync(DataContext db, int? userId, string? storeCookie)
    {
        var claims = userId != null ? new[] { new Claim("userId", userId.Value.ToString()) } : Array.Empty<Claim>();
        var httpContext = new DefaultHttpContext();
        if (storeCookie != null)
            httpContext.Request.Headers.Cookie = $"selected_store_id={storeCookie}";

        var groups = new RecordingGroupManager();
        var hub = new OperationHub(db)
        {
            Context = new FakeHubCallerContext(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), httpContext),
            Groups = groups
        };

        await hub.OnConnectedAsync();
        return groups;
    }

    private static DataContext SeedTwoStores()
    {
        var db = TestDbFactory.CreateDb();
        var alice = MakeUser(1);
        var mallory = MakeUser(2);
        var inactive = MakeUser(3, active: false);
        db.Users.AddRange(alice, mallory, inactive);
        db.stores.AddRange(MakeStore(10, alice, inactive), MakeStore(20, mallory));
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Member_JoinsSelectedStoreGroup()
    {
        using var db = SeedTwoStores();

        var groups = await ConnectAsync(db, userId: 1, storeCookie: "10");

        Assert.Equal(new[] { ("conn-1", "store-10") }, groups.Added);
    }

    [Fact]
    public async Task NonMember_WithForeignStoreCookie_JoinsNoGroup()
    {
        using var db = SeedTwoStores();

        var groups = await ConnectAsync(db, userId: 2, storeCookie: "10");

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task InactiveMember_JoinsNoGroup()
    {
        using var db = SeedTwoStores();

        var groups = await ConnectAsync(db, userId: 3, storeCookie: "10");

        Assert.Empty(groups.Added);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("999")]
    public async Task MissingInvalidOrUnknownStoreCookie_JoinsNoGroup(string? storeCookie)
    {
        using var db = SeedTwoStores();

        var groups = await ConnectAsync(db, userId: 1, storeCookie: storeCookie);

        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task MissingUserClaim_JoinsNoGroup()
    {
        using var db = SeedTwoStores();

        var groups = await ConnectAsync(db, userId: null, storeCookie: "10");

        Assert.Empty(groups.Added);
    }
}
