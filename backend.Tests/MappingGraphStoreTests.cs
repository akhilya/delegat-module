using Delegat.Services.Marketplace.Graph;
using Delegat.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class MappingGraphStoreTests : IAsyncLifetime
{
    private const string G0 = "{\"nodes\":[{\"id\":\"g0\"}],\"edges\":[]}";
    private const string G1 = "{\"nodes\":[{\"id\":\"g1\"}],\"edges\":[]}";
    private const string G2 = "{\"nodes\":[{\"id\":\"g2\"}],\"edges\":[]}";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private int _connectionId;
    private int _mappingId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var mapping = data.AddMapping(connection.Id, category.Id);
        mapping.graphJson = G0;
        await db.SaveChangesAsync();
        _connectionId = connection.Id;
        _mappingId = mapping.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // Each request in the API gets its own DataContext; share one in-memory database between them.
    private DataContext NewContext() =>
        new(new DbContextOptionsBuilder<DataContext>().UseSqlite(_connection).Options);

    private async Task<string?> StoredGraphAsync()
    {
        await using var db = NewContext();
        return (await MappingGraphStore.ReadAsync(db, _connectionId, _mappingId))?.GraphJson;
    }

    [Fact]
    public async Task Save_WithCurrentVersion_WritesGraphAndStampsUpdate()
    {
        await using var db = NewContext();
        var result = await MappingGraphStore.SaveAsync(
            db, _connectionId, _mappingId, G1, MappingGraphVersion.Compute(G0));

        Assert.Equal(MappingGraphSaveStatus.Saved, result.Status);
        Assert.Equal(MappingGraphVersion.Compute(G1), result.GraphVersion);
        await using var check = NewContext();
        var stored = await MappingGraphStore.ReadAsync(check, _connectionId, _mappingId);
        Assert.Equal(G1, stored?.GraphJson);
        Assert.NotNull(stored?.GraphUpdatedAt);
    }

    [Fact]
    public async Task Save_WithStaleVersion_ReturnsConflictAndKeepsGraph()
    {
        await using var db = NewContext();
        var result = await MappingGraphStore.SaveAsync(
            db, _connectionId, _mappingId, G2, MappingGraphVersion.Compute(G1));

        Assert.Equal(MappingGraphSaveStatus.Conflict, result.Status);
        Assert.Equal(MappingGraphVersion.Compute(G0), result.GraphVersion);
        Assert.Equal(G0, await StoredGraphAsync());
    }

    [Fact]
    public async Task OverlappingSaves_OfSameVersion_OnlyFirstWriteWins()
    {
        // Alice reads G0, then Bob saves G1 before Alice writes.
        await using var alice = NewContext();
        var aliceSnapshot = await MappingGraphStore.ReadAsync(alice, _connectionId, _mappingId);
        Assert.Equal(G0, aliceSnapshot?.GraphJson);

        await using (var bob = NewContext())
        {
            var bobResult = await MappingGraphStore.SaveAsync(
                bob, _connectionId, _mappingId, G1, MappingGraphVersion.Compute(G0));
            Assert.Equal(MappingGraphSaveStatus.Saved, bobResult.Status);
        }

        var aliceWritten = await MappingGraphStore.TryWriteAsync(
            alice, _connectionId, _mappingId, G2, aliceSnapshot);

        Assert.False(aliceWritten);
        Assert.Equal(G1, await StoredGraphAsync());
    }

    [Fact]
    public async Task OverlappingSaves_DetectChangeEvenWhenGraphTextIsRestored()
    {
        await using var alice = NewContext();
        var aliceSnapshot = await MappingGraphStore.ReadAsync(alice, _connectionId, _mappingId);

        await using (var bob = NewContext())
        {
            // Same text, new save timestamp.
            await MappingGraphStore.TryWriteAsync(bob, _connectionId, _mappingId, G0, original: null);
        }

        Assert.False(await MappingGraphStore.TryWriteAsync(alice, _connectionId, _mappingId, G2, aliceSnapshot));
        Assert.Equal(G0, await StoredGraphAsync());
    }

    [Fact]
    public async Task Save_WithoutExpectedVersion_Overwrites()
    {
        await using var db = NewContext();
        var result = await MappingGraphStore.SaveAsync(db, _connectionId, _mappingId, G2, expectedGraphVersion: null);

        Assert.Equal(MappingGraphSaveStatus.Saved, result.Status);
        Assert.Equal(G2, await StoredGraphAsync());
    }

    [Fact]
    public async Task Save_UnknownMapping_ReturnsNotFound()
    {
        await using var db = NewContext();
        var result = await MappingGraphStore.SaveAsync(db, _connectionId, _mappingId + 1000, G1, null);

        Assert.Equal(MappingGraphSaveStatus.NotFound, result.Status);
    }
}
