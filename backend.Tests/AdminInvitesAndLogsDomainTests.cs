using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class AdminInvitesAndLogsDomainTests
{
    [Fact]
    public async Task InvitationToken_AddFill_GeneratesUnusedToken()
    {
        await using var db = TestDbFactory.CreateDb();
        var token = new InvitationToken();

        Assert.True(token.AddFill(new InvitationToken.AddForm()));
        db.invitationTokens.Add(token);
        await db.SaveChangesAsync();

        var saved = db.invitationTokens.Single();
        Assert.False(saved.used);
        Assert.NotEmpty(saved.token);
        Assert.NotEqual(default, saved.createdAt);
    }

    [Fact]
    public async Task InvitationToken_Use_MarksTokenAndUser()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var token = new InvitationToken();
        token.AddFill(new InvitationToken.AddForm());
        db.invitationTokens.Add(token);
        await data.SaveAsync();

        Assert.True(token.UpdateFill(new InvitationToken.UseForm(user.Id)));
        await db.SaveChangesAsync();

        var saved = db.invitationTokens.Single();
        Assert.True(saved.used);
        Assert.Equal(user.Id, saved.usedByUserId);
        Assert.NotNull(saved.usedAt);
    }

    [Fact]
    public async Task InvitationToken_CannotBeUsedTwice()
    {
        await using var db = TestDbFactory.CreateDb();
        var token = new InvitationToken { token = "once", used = true, usedByUserId = 10, createdAt = DateTime.UtcNow };
        db.invitationTokens.Add(token);
        await db.SaveChangesAsync();

        Assert.False(token.UpdateFill(new InvitationToken.UseForm(11)));
        Assert.Equal(10, token.usedByUserId);
    }

    [Fact]
    public async Task InvitationToken_Delete_RemovesToken()
    {
        await using var db = TestDbFactory.CreateDb();
        var token = new InvitationToken { token = "delete-me", createdAt = DateTime.UtcNow };
        db.invitationTokens.Add(token);
        await db.SaveChangesAsync();

        db.invitationTokens.Remove(token);
        await db.SaveChangesAsync();

        Assert.Empty(db.invitationTokens);
    }

    [Fact]
    public async Task ErrorLog_Add_PersistsRequestAndExceptionDetails()
    {
        await using var db = TestDbFactory.CreateDb();
        var entry = new ErrorLog
        {
            statusCode = 500, method = "POST", path = "/api/products", queryString = "?draft=true",
            exceptionType = "InvalidOperationException", message = "failed", stackTrace = "stack",
            traceIdentifier = "trace-1", userId = 7, userName = "admin", remoteIpAddress = "127.0.0.1"
        };
        db.errorLogs.Add(entry);
        await db.SaveChangesAsync();

        var saved = db.errorLogs.Single();
        Assert.Equal(500, saved.statusCode);
        Assert.Equal("/api/products", saved.path);
        Assert.Equal("InvalidOperationException", saved.exceptionType);
        Assert.Equal("trace-1", saved.traceIdentifier);
    }

    [Fact]
    public async Task ErrorLog_Delete_RemovesOnlySelectedRecord()
    {
        await using var db = TestDbFactory.CreateDb();
        var first = new ErrorLog { statusCode = 500, path = "/first", exceptionType = "E", message = "first", traceIdentifier = "1" };
        var second = new ErrorLog { statusCode = 404, path = "/second", exceptionType = "E", message = "second", traceIdentifier = "2" };
        db.errorLogs.AddRange(first, second);
        await db.SaveChangesAsync();

        db.errorLogs.Remove(first);
        await db.SaveChangesAsync();

        Assert.Single(db.errorLogs);
        Assert.Equal("/second", db.errorLogs.Single().path);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task ErrorLog_FilterByStatusCode_ReturnsMatchingEntries(int statusCode)
    {
        await using var db = TestDbFactory.CreateDb();
        db.errorLogs.AddRange(
            new ErrorLog { statusCode = 400, path = "/bad", exceptionType = "Bad", message = "bad", traceIdentifier = "bad" },
            new ErrorLog { statusCode = 500, path = "/error", exceptionType = "Error", message = "error", traceIdentifier = "error" });
        await db.SaveChangesAsync();

        var entries = db.errorLogs.Where(e => e.statusCode == statusCode).ToList();
        Assert.Single(entries);
        Assert.Equal(statusCode, entries[0].statusCode);
    }

    [Fact]
    public async Task AdapterHttpLog_Create_PersistsOutboundRequest()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        var log = new AdapterHttpLog
        {
            storeId = store.Id, connectionId = connection.Id, adapterKey = "ozon",
            method = "POST", url = "https://api.example.test/v1/items", path = "/v1/items",
            statusCode = 201, durationMs = 250, requestHeadersJson = """{"Authorization":"[REDACTED]"}""",
            requestBody = """{"sku":"A-1"}""", responseBody = """{"result":"ok"}""", operationId = "op-1"
        };
        db.adapterHttpLogs.Add(log);
        await data.SaveAsync();

        var saved = db.adapterHttpLogs.Single();
        Assert.Equal(201, saved.statusCode);
        Assert.Equal(250, saved.durationMs);
        Assert.Equal("op-1", saved.operationId);
    }

    [Fact]
    public async Task AdapterHttpLog_FilterByConnection_IsolatesLogs()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var first = data.AddConnection(store.Id, "ozon");
        var second = data.AddConnection(store.Id, "wb");
        db.adapterHttpLogs.AddRange(
            new AdapterHttpLog { storeId = store.Id, connectionId = first.Id, adapterKey = "ozon", method = "GET", url = "https://a", path = "/a", statusCode = 200 },
            new AdapterHttpLog { storeId = store.Id, connectionId = second.Id, adapterKey = "wb", method = "GET", url = "https://b", path = "/b", statusCode = 200 });
        await data.SaveAsync();

        var logs = db.adapterHttpLogs.Where(l => l.connectionId == first.Id).ToList();
        Assert.Single(logs);
        Assert.Equal("/a", logs[0].path);
    }

    [Fact]
    public async Task AdapterHttpLog_FilterByStore_ExcludesOtherStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var firstStore = data.AddStore("A");
        var secondStore = data.AddStore("B");
        var first = data.AddConnection(firstStore.Id);
        var second = data.AddConnection(secondStore.Id);
        db.adapterHttpLogs.AddRange(
            new AdapterHttpLog { storeId = firstStore.Id, connectionId = first.Id, adapterKey = "ozon", method = "GET", url = "https://a", path = "/a", statusCode = 200 },
            new AdapterHttpLog { storeId = secondStore.Id, connectionId = second.Id, adapterKey = "ozon", method = "GET", url = "https://b", path = "/b", statusCode = 200 });
        await data.SaveAsync();

        Assert.Single(db.adapterHttpLogs.Where(l => l.storeId == firstStore.Id));
    }

    [Theory]
    [InlineData(200, 1)]
    [InlineData(500, 1)]
    [InlineData(404, 0)]
    public async Task AdapterHttpLog_FilterByStatusCode_ReturnsExpectedCount(int statusCode, int expectedCount)
    {
        await using var db = TestDbFactory.CreateDb();
        db.adapterHttpLogs.AddRange(
            new AdapterHttpLog { storeId = 1, connectionId = 1, adapterKey = "ozon", method = "GET", url = "https://ok", path = "/ok", statusCode = 200 },
            new AdapterHttpLog { storeId = 1, connectionId = 1, adapterKey = "ozon", method = "POST", url = "https://fail", path = "/fail", statusCode = 500 });
        await db.SaveChangesAsync();

        Assert.Equal(expectedCount, db.adapterHttpLogs.Count(l => l.statusCode == statusCode));
    }
}
