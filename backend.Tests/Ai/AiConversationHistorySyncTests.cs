using System.Text.Json.Nodes;
using Delegat.Controllers;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Delegat.Tests;

public class AiConversationHistorySyncTests
{
    [Fact]
    public async Task StaleSyncCannotEraseAStoppedReplyOrItsPartialText()
    {
        await using var db = TestDbFactory.CreateDb();
        var conv = await AiConversationTurnStore.EnsureConversationAsync(db, "stopped-sync", 1, 2, CancellationToken.None);
        var message = new AiConversationMessage { conversationId = conv.Id, role = "assistant", content = "Partial work",
            toolCallsJson = """{"_ui":"assistantTurn","turnStopped":true,"tools":[],"timeline":[]}""" };
        db.aiConversationMessages.Add(message);
        await db.SaveChangesAsync();
        var controller = new AiConversationController(db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 }, null!, null!);
        await controller.SaveMessages(new SaveMessagesRequest { SessionId = conv.sessionId, Messages = [
            new SaveMessageItem { DbId = message.Id, Role = "assistant", Content = "Older text", ToolCallsJson = """{"_ui":"assistantTurn","tools":[]}""" },
        ] }, CancellationToken.None);
        Assert.Equal("Partial work", message.content);
        Assert.True(AiConversationTurnStore.HasStoppedTurn(message.toolCallsJson));
    }

    [Fact]
    public async Task SaveMessages_UpdatesUserMetadata_WhenLiveTurnAlreadyInsertedMatchingText()
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = new AiConversation
        {
            sessionId = "form-sync-session",
            storeId = 1,
            userId = 2,
        };
        db.aiConversations.Add(conversation);
        await db.SaveChangesAsync();

        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "user",
            content = "Price for FI10",
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService
        {
            UserId = 2,
            SelectedStoreId = 1,
        };
        var controller = new AiConversationController(db, currentUser, null!, null!);
        var metadata = """
            {"_ui":"formReply","formId":"ask-1","answers":{"identifier":"FI10"}}
            """;

        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem
                {
                    Role = "user",
                    Content = "Price for FI10",
                    ToolCallsJson = metadata,
                },
            ],
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var saved = await db.aiConversationMessages.SingleAsync();
        var savedMetadata = JsonNode.Parse(saved.toolCallsJson!);
        Assert.Equal("formReply", savedMetadata?["_ui"]?.GetValue<string>());
        Assert.Equal("ask-1", savedMetadata?["formId"]?.GetValue<string>());
        Assert.Equal("FI10", savedMetadata?["answers"]?["identifier"]?.GetValue<string>());
    }

    [Fact]
    public async Task SaveMessages_ReconcilesServerInsertedTailWithoutClientIds()
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = new AiConversation
        {
            sessionId = "server-tail-sync-session",
            storeId = 1,
            userId = 2,
        };
        db.aiConversations.Add(conversation);
        await db.SaveChangesAsync();

        var createdAt = DateTime.UtcNow;
        db.aiConversationMessages.AddRange(
            new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "user",
                content = "Find archived Ozon products",
                createdAt = createdAt,
            },
            new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "assistant",
                content = "Checking the linked products.",
                toolCallsJson = """
                    {"_ui":"assistantTurn","tools":[],"timeline":[],"plan":{"proposalId":"server-plan"}}
                    """,
                createdAt = createdAt.AddMilliseconds(1),
            });
        await db.SaveChangesAsync();

        var controller = new AiConversationController(
            db,
            new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!,
            null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem
                {
                    Role = "user",
                    Content = "Find archived Ozon products",
                    ToolCallsJson = """
                        {"_ui":"operationContext","card":{"title":"Archive products"}}
                        """,
                },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "Found 3 linked products outside the archive.",
                    ToolCallsJson = """
                        {"_ui":"assistantTurn","tools":[{"name":"search_catalog"}],"timeline":[],"plan":{"proposalId":"client-plan"}}
                        """,
                },
            ],
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, await db.aiConversationMessages.CountAsync());
        var savedUser = await db.aiConversationMessages.SingleAsync(message => message.role == "user");
        var savedAssistant = await db.aiConversationMessages.SingleAsync(message => message.role == "assistant");
        Assert.Contains("Found 3 linked products", savedAssistant.content);
        Assert.Equal(
            "server-plan",
            JsonNode.Parse(savedAssistant.toolCallsJson!)?["plan"]?["proposalId"]?.GetValue<string>());
        Assert.Equal(
            "operationContext",
            JsonNode.Parse(savedUser.toolCallsJson!)?["_ui"]?.GetValue<string>());
    }

    [Fact]
    public async Task SaveMessages_MergesFormMetadataForUnpersistedTailAfterKnownHistory()
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = new AiConversation
        {
            sessionId = "form-sync-known-history-session",
            storeId = 1,
            userId = 2,
        };
        db.aiConversations.Add(conversation);
        await db.SaveChangesAsync();
        db.aiConversationMessages.AddRange(
            new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "user",
                content = "Earlier question",
                createdAt = DateTime.UtcNow,
            },
            new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "assistant",
                content = "Earlier answer",
                createdAt = DateTime.UtcNow.AddMilliseconds(1),
            },
            new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "user",
                content = "Price for FI10",
                createdAt = DateTime.UtcNow.AddMilliseconds(2),
            });
        await db.SaveChangesAsync();
        var priorMessages = await db.aiConversationMessages
            .Where(message => message.content.StartsWith("Earlier"))
            .OrderBy(message => message.createdAt)
            .ToListAsync();
        var savedUser = await db.aiConversationMessages.SingleAsync(
            message => message.content == "Price for FI10");
        var controller = new AiConversationController(
            db,
            new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!,
            null!);

        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem
                {
                    Role = "user",
                    Content = priorMessages[0].content,
                    DbId = priorMessages[0].Id,
                },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = priorMessages[1].content,
                    DbId = priorMessages[1].Id,
                },
                new SaveMessageItem
                {
                    Role = "user",
                    Content = "Price for FI10",
                    ToolCallsJson = """{"_ui":"formReply","formId":"ask-1","answers":{"identifier":"FI10"}}""",
                },
            ],
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, await db.aiConversationMessages.CountAsync());
        var refreshedUser = await db.aiConversationMessages.SingleAsync(message => message.Id == savedUser.Id);
        var metadata = JsonNode.Parse(refreshedUser.toolCallsJson!);
        Assert.Equal("ask-1", metadata?["formId"]?.GetValue<string>());
        Assert.Equal("FI10", metadata?["answers"]?["identifier"]?.GetValue<string>());
    }

    [Fact]
    public async Task SaveMessages_DoesNotDeleteConcurrentServerMessageOrLoseFormMetadata()
    {
        var databaseName = $"history-sync-race-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var baseOptions = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using (var setup = new DataContext(baseOptions))
        {
            var conversation = new AiConversation
            {
                sessionId = "form-sync-race-session",
                storeId = 1,
                userId = 2,
            };
            setup.aiConversations.Add(conversation);
            await setup.SaveChangesAsync();
            setup.aiConversationMessages.AddRange(
                new AiConversationMessage
                {
                    conversationId = conversation.Id,
                    role = "user",
                    content = "Price for FI10",
                    createdAt = DateTime.UtcNow,
                },
                new AiConversationMessage
                {
                    conversationId = conversation.Id,
                    role = "assistant",
                    content = "Checking FI10.",
                    toolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[]}""",
                    createdAt = DateTime.UtcNow.AddMilliseconds(1),
                });
            await setup.SaveChangesAsync();
        }

        var gate = new SaveChangesGate();
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .AddInterceptors(gate)
            .Options;
        await using var syncDb = new DataContext(options);
        var userMessage = await syncDb.aiConversationMessages
            .SingleAsync(message => message.role == "user");
        var controller = new AiConversationController(
            syncDb,
            new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!,
            null!);
        var syncTask = controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = "form-sync-race-session",
            Messages =
            [
                new SaveMessageItem
                {
                    Role = "user",
                    Content = "Price for FI10",
                    DbId = userMessage.Id,
                    ToolCallsJson = """{"_ui":"formReply","formId":"ask-1","answers":{"identifier":"FI10"}}""",
                },
            ],
        }, CancellationToken.None);

        await gate.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var liveDb = new DataContext(baseOptions);
            var assistant = await liveDb.aiConversationMessages
                .SingleAsync(message => message.role == "assistant");
            assistant.toolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[],"plan":{"proposalId":"server-owned"}}""";
            await liveDb.SaveChangesAsync();
        }
        finally
        {
            gate.ContinueSave.TrySetResult();
        }

        Assert.IsType<OkObjectResult>(await syncTask);

        await using var verifyDb = new DataContext(baseOptions);
        var savedUser = await verifyDb.aiConversationMessages
            .SingleAsync(message => message.role == "user");
        var savedAssistant = await verifyDb.aiConversationMessages
            .SingleAsync(message => message.role == "assistant");
        var savedConversation = await verifyDb.aiConversations.SingleAsync();
        var savedForm = JsonNode.Parse(savedUser.toolCallsJson!);
        var savedPlan = JsonNode.Parse(savedAssistant.toolCallsJson!);
        Assert.Equal("ask-1", savedForm?["formId"]?.GetValue<string>());
        Assert.Equal("FI10", savedForm?["answers"]?["identifier"]?.GetValue<string>());
        Assert.Equal("server-owned", savedPlan?["plan"]?["proposalId"]?.GetValue<string>());
        Assert.Equal(2, savedConversation.messageCount);
    }

    [Fact]
    public async Task LiveTurnRetriesConcurrentAssistantUpdateAndPreservesServerPlan()
    {
        var databaseName = $"live-turn-race-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var baseOptions = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        await using (var setup = new DataContext(baseOptions))
        {
            var conversation = new AiConversation
            {
                sessionId = "live-turn-race-session",
                storeId = 1,
                userId = 2,
            };
            setup.aiConversations.Add(conversation);
            await setup.SaveChangesAsync();
            setup.aiConversationMessages.Add(new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "assistant",
                content = "Earlier response.",
                toolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[]}""",
                createdAt = DateTime.UtcNow,
            });
            await setup.SaveChangesAsync();
        }

        var gate = new SaveChangesGate();
        var racedOptions = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .AddInterceptors(gate)
            .Options;
        await using var liveTurnDb = new DataContext(racedOptions);
        var upsertTask = AiConversationTurnStore.UpsertAssistantAsync(
            liveTurnDb,
            "live-turn-race-session",
            1,
            2,
            new AiSseTurnSnapshot(
                "Current response.",
                """{"_ui":"assistantTurn","tools":[{"name":"search_catalog"}],"timeline":[]}""",
                "test-model"),
            CancellationToken.None);

        await gate.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var concurrentSyncDb = new DataContext(baseOptions);
            var assistant = await concurrentSyncDb.aiConversationMessages
                .SingleAsync(message => message.role == "assistant");
            assistant.toolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[],"plan":{"proposalId":"server-owned"}}""";
            await concurrentSyncDb.SaveChangesAsync();
        }
        finally
        {
            gate.ContinueSave.TrySetResult();
        }

        await upsertTask;

        await using var verifyDb = new DataContext(baseOptions);
        var saved = await verifyDb.aiConversationMessages.SingleAsync(message => message.role == "assistant");
        var savedTurn = JsonNode.Parse(saved.toolCallsJson!);
        Assert.Contains("Earlier response.", saved.content);
        Assert.Contains("Current response.", saved.content);
        Assert.Equal("server-owned", savedTurn?["plan"]?["proposalId"]?.GetValue<string>());
        Assert.Equal("search_catalog", savedTurn?["tools"]?[0]?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task SaveMessagesRebasesConcurrentAssistantSyncOnLatestServerPlan()
    {
        var databaseName = $"history-plan-race-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var baseOptions = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        int assistantId;
        await using (var setup = new DataContext(baseOptions))
        {
            var conversation = new AiConversation
            {
                sessionId = "history-plan-race-session",
                storeId = 1,
                userId = 2,
            };
            setup.aiConversations.Add(conversation);
            await setup.SaveChangesAsync();
            var assistant = new AiConversationMessage
            {
                conversationId = conversation.Id,
                role = "assistant",
                content = "Earlier response.",
                toolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[],"plan":{"proposalId":"signed-plan"}}""",
                createdAt = DateTime.UtcNow,
            };
            setup.aiConversationMessages.Add(assistant);
            await setup.SaveChangesAsync();
            assistantId = assistant.Id;
        }

        var gate = new SaveChangesGate();
        var racedOptions = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .AddInterceptors(gate)
            .Options;
        await using var syncDb = new DataContext(racedOptions);
        var controller = new AiConversationController(
            syncDb,
            new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!,
            null!);
        var syncTask = controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = "history-plan-race-session",
            Messages =
            [
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "Updated response.",
                    DbId = assistantId,
                    ToolCallsJson = """{"_ui":"assistantTurn","tools":[{"name":"search_catalog"}],"timeline":[],"plan":{"proposalId":"client-plan"},"planConfirmed":false}""",
                },
            ],
        }, CancellationToken.None);

        await gate.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var liveDb = new DataContext(baseOptions);
            var assistant = await liveDb.aiConversationMessages.SingleAsync();
            assistant.toolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[],"plan":{"proposalId":"signed-plan"},"planConfirmed":true,"planConfirmedBy":2}""";
            await liveDb.SaveChangesAsync();
        }
        finally
        {
            gate.ContinueSave.TrySetResult();
        }

        Assert.IsType<OkObjectResult>(await syncTask);

        await using var verifyDb = new DataContext(baseOptions);
        var saved = await verifyDb.aiConversationMessages.SingleAsync();
        var savedTurn = JsonNode.Parse(saved.toolCallsJson!);
        Assert.Equal("Updated response.", saved.content);
        Assert.Equal("signed-plan", savedTurn?["plan"]?["proposalId"]?.GetValue<string>());
        Assert.True(savedTurn?["planConfirmed"]?.GetValue<bool>());
        Assert.Equal(2, savedTurn?["planConfirmedBy"]?.GetValue<int>());
        Assert.Equal("search_catalog", savedTurn?["tools"]?[0]?["name"]?.GetValue<string>());
    }

    private sealed class SaveChangesGate : SaveChangesInterceptor
    {
        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveStarted.TrySetResult();
            await ContinueSave.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
