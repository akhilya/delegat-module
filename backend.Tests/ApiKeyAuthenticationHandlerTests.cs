using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Delegat.Tests;

public class ApiKeyAuthenticationHandlerTests
{
    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static DataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DataContext(options);
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

    private static async Task<(ApiKeyAuthenticationHandler handler, DefaultHttpContext http, DataContext db)> CreateHandlerAsync(
        DataContext db,
        string? authorizationHeader = null)
    {
        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        if (authorizationHeader != null)
            http.Request.Headers.Authorization = authorizationHeader;

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationHandler.SchemeName,
            ApiKeyAuthenticationHandler.SchemeName,
            typeof(ApiKeyAuthenticationHandler));

        await handler.InitializeAsync(scheme, http);
        return (handler, http, db);
    }

    private static async Task<(string plaintext, UserApiKey entity)> SeedKeyAsync(
        DataContext db,
        int userId,
        bool revoked = false,
        bool userActive = true)
    {
        db.Users.Add(MakeUser(userId, userActive));
        var plaintext = "dk_" + Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        var entity = new UserApiKey
        {
            userId = userId,
            name = "test",
            keyPrefix = "dk_",
            keyHash = ApiKeyAuthenticationHandler.HashKey(plaintext),
            last4 = plaintext[^4..],
            createdAt = DateTime.UtcNow,
            revokedAt = revoked ? DateTime.UtcNow : null
        };
        db.userApiKeys.Add(entity);
        await db.SaveChangesAsync();
        return (plaintext, entity);
    }

    [Fact]
    public async Task Authenticate_MissingHeader_ReturnsNoResult()
    {
        await using var db = CreateDb();
        var (handler, _, _) = await CreateHandlerAsync(db);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Theory]
    [InlineData("Basic abc")]
    [InlineData("Token dk_abc")]
    [InlineData("")]
    public async Task Authenticate_NonBearer_ReturnsNoResult(string header)
    {
        await using var db = CreateDb();
        var (handler, _, _) = await CreateHandlerAsync(db, header);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("Bearer   ")]
    [InlineData("Bearer not_a_dk_key")]
    [InlineData("Bearer sk_live_123")]
    public async Task Authenticate_InvalidKeyFormat_Fails(string header)
    {
        await using var db = CreateDb();
        var (handler, _, _) = await CreateHandlerAsync(db, header);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.False(result.None);
        Assert.Equal("Invalid API key", result.Failure!.Message);
    }

    [Fact]
    public async Task Authenticate_UnknownKey_Fails()
    {
        await using var db = CreateDb();
        var (handler, _, _) = await CreateHandlerAsync(db, "Bearer dk_unknown_key_value");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid API key", result.Failure!.Message);
    }

    [Fact]
    public async Task Authenticate_RevokedKey_Fails()
    {
        await using var db = CreateDb();
        var (plaintext, _) = await SeedKeyAsync(db, userId: 1, revoked: true);
        var (handler, _, _) = await CreateHandlerAsync(db, "Bearer " + plaintext);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid API key", result.Failure!.Message);
    }

    [Fact]
    public async Task Authenticate_InactiveUser_Fails()
    {
        await using var db = CreateDb();
        var (plaintext, _) = await SeedKeyAsync(db, userId: 2, userActive: false);
        var (handler, _, _) = await CreateHandlerAsync(db, "Bearer " + plaintext);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("User is inactive", result.Failure!.Message);
    }

    [Fact]
    public async Task Authenticate_ValidKey_SucceedsWithClaims()
    {
        await using var db = CreateDb();
        var (plaintext, entity) = await SeedKeyAsync(db, userId: 7);
        var (handler, _, _) = await CreateHandlerAsync(db, "Bearer " + plaintext);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        var principal = result.Principal!;
        Assert.Equal("7", principal.FindFirstValue("userId"));
        Assert.Equal("7", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(entity.Id.ToString(), principal.FindFirstValue("apiKeyId"));
        Assert.Equal(ApiKeyAuthenticationHandler.SchemeName, principal.Identity!.AuthenticationType);
    }

    [Fact]
    public async Task Authenticate_BearerCaseInsensitive_Succeeds()
    {
        await using var db = CreateDb();
        var (plaintext, _) = await SeedKeyAsync(db, userId: 8);
        var (handler, _, _) = await CreateHandlerAsync(db, "bearer " + plaintext);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Challenge_WritesPublicApiUnauthorizedEnvelope()
    {
        await using var db = CreateDb();
        var (handler, http, _) = await CreateHandlerAsync(db);

        await handler.ChallengeAsync(new AuthenticationProperties());

        Assert.Equal(401, http.Response.StatusCode);
        Assert.Equal("application/json", http.Response.ContentType);
        http.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            "Invalid or missing API key",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Forbid_WritesPublicApiForbiddenEnvelope()
    {
        await using var db = CreateDb();
        var (handler, http, _) = await CreateHandlerAsync(db);

        await handler.ForbidAsync(new AuthenticationProperties());

        Assert.Equal(403, http.Response.StatusCode);
        Assert.Equal("application/json", http.Response.ContentType);
        http.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        Assert.Equal("forbidden", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }
}
