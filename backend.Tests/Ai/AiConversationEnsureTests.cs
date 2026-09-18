using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class AiConversationEnsureTests
{
    [Fact]
    public async Task Ensure_ReusesRow_WhenStoreDiffersForSameUser()
    {
        using var db = TestDbFactory.CreateDb();
        db.aiConversations.Add(new AiConversation
        {
            sessionId = "61f7273a-36af-4b33-a3fc-0b076dfa23de",
            storeId = 1,
            userId = 9,
        });
        await db.SaveChangesAsync();

        var conv = await AiConversationTurnStore.EnsureConversationAsync(
            db, "61f7273a-36af-4b33-a3fc-0b076dfa23de", storeId: 2, userId: 9, CancellationToken.None);

        Assert.Equal(1, conv.storeId);
        Assert.Equal(1, db.aiConversations.Count());
    }

    [Fact]
    public async Task Ensure_Rejects_WhenSessionBelongsToAnotherUser()
    {
        using var db = TestDbFactory.CreateDb();
        db.aiConversations.Add(new AiConversation
        {
            sessionId = "sess-a",
            storeId = 1,
            userId = 1,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            AiConversationTurnStore.EnsureConversationAsync(
                db, "sess-a", storeId: 1, userId: 2, CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("access_denied", ex.Code);
    }

    [Fact]
    public async Task Ensure_Creates_WhenMissing()
    {
        using var db = TestDbFactory.CreateDb();
        var conv = await AiConversationTurnStore.EnsureConversationAsync(
            db, "new-session", storeId: 3, userId: 4, CancellationToken.None);
        Assert.Equal("new-session", conv.sessionId);
        Assert.Equal(3, conv.storeId);
        Assert.Equal(4, conv.userId);
        Assert.Equal(1, db.aiConversations.Count());
    }
}
