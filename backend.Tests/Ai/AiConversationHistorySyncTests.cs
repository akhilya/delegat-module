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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SaveMessages_PersistedReviewKeepsOwnProposalAndParentAcrossReload(
        bool includeDbIds, bool hasRecoveryPlan)
    {
        await using var db = TestDbFactory.CreateDb();
        var conv = await AiConversationTurnStore.EnsureConversationAsync(
            db, $"review-reload-{Guid.NewGuid():N}", 1, 2, CancellationToken.None);
        var createdAt = DateTime.UtcNow;
        var user = new AiConversationMessage
        {
            conversationId = conv.Id,
            role = "user",
            content = "Request",
            createdAt = createdAt,
        };
        const string parentJson = """
            {"_ui":"assistantTurn","plan":{"proposalId":"A"},"planConfirmed":true,"planExecuted":true,"planFailed":true,"planExecStart":0,"planConfirmationId":"A","tools":[],"timeline":[]}
            """;
        var parent = new AiConversationMessage
        {
            conversationId = conv.Id,
            role = "assistant",
            content = "",
            toolCallsJson = parentJson,
            createdAt = createdAt.AddMilliseconds(1),
        };
        // A server-persisted review links to the parent execution only; a recovery
        // variant also carries its own pending proposal B (this fixture tests
        // identity; signing/confirmation is covered separately). Two empty-content
        // assistant rows must stay distinct - the review never inherits A's flags.
        var reviewContent = hasRecoveryPlan ? "" : "Review result";
        var reviewJson = hasRecoveryPlan
            ? """{"_ui":"assistantTurn","executionProposalId":"A","plan":{"proposalId":"B"},"tools":[],"timeline":[]}"""
            : """{"_ui":"assistantTurn","executionProposalId":"A","tools":[],"timeline":[]}""";
        var review = new AiConversationMessage
        {
            conversationId = conv.Id,
            role = "assistant",
            content = reviewContent,
            toolCallsJson = reviewJson,
            createdAt = createdAt.AddMilliseconds(2),
        };
        db.aiConversationMessages.AddRange(user, parent, review);
        await db.SaveChangesAsync();

        SaveMessageItem Echo(AiConversationMessage row) => new()
        {
            DbId = includeDbIds ? row.Id : null,
            Role = row.role,
            Content = row.content,
            ToolCallsJson = row.toolCallsJson,
        };
        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 }, null!, null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conv.sessionId,
            Messages = [Echo(user), Echo(parent), Echo(review)],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        db.ChangeTracker.Clear();
        var rows = await db.aiConversationMessages
            .Where(m => m.conversationId == conv.Id)
            .OrderBy(m => m.createdAt).ThenBy(m => m.Id)
            .ToListAsync();
        Assert.Equal(new[] { user.Id, parent.Id, review.Id }, rows.Select(m => m.Id).ToArray());
        Assert.NotEqual(parent.Id, review.Id);

        var savedParent = JsonNode.Parse(rows[1].toolCallsJson!);
        Assert.Empty(rows[1].content);
        Assert.Equal("A", savedParent?["plan"]?["proposalId"]?.GetValue<string>());
        Assert.Equal(true, savedParent?["planConfirmed"]?.GetValue<bool>());
        Assert.Equal(true, savedParent?["planExecuted"]?.GetValue<bool>());
        Assert.Equal(true, savedParent?["planFailed"]?.GetValue<bool>());
        Assert.Equal(0, savedParent?["planExecStart"]?.GetValue<int>());
        Assert.Equal("A", savedParent?["planConfirmationId"]?.GetValue<string>());

        Assert.Equal(reviewContent, rows[2].content);
        var savedReview = JsonNode.Parse(rows[2].toolCallsJson!);
        Assert.Equal("A", savedReview?["executionProposalId"]?.GetValue<string>());
        if (hasRecoveryPlan)
            Assert.Equal("B", savedReview?["plan"]?["proposalId"]?.GetValue<string>());
        else
            Assert.Null(savedReview?["plan"]);
        foreach (var flag in new[]
                 { "planConfirmed", "planExecuted", "planFailed", "planExecStart", "planConfirmationId" })
        {
            Assert.Null(savedReview?[flag]);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NoPlanReview_DoesNotAcquireParentPlan(
        bool includeKnownUserId, bool reviewPersistedFirst)
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "m0-d3-probe", 1, 2, CancellationToken.None);
        var start = DateTime.UtcNow;
        var user = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "user",
            content = "Request",
            createdAt = start,
        };
        var execution = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "",
            createdAt = start.AddMilliseconds(1),
            toolCallsJson = """
                {"_ui":"assistantTurn","plan":{"proposalId":"A"},"planConfirmed":true,"planExecuted":true,"planExecStart":0,"tools":[],"timeline":[]}
                """,
        };
        db.aiConversationMessages.AddRange(user, execution);
        await db.SaveChangesAsync();
        const string reviewJson = """
            {"_ui":"assistantTurn","executionProposalId":"A","tools":[],"timeline":[]}
            """;
        if (reviewPersistedFirst)
            await AiConversationTurnStore.AppendAssistantAsync(
                db, conversation.sessionId, 1, 2,
                new AiSseTurnSnapshot("Review result", reviewJson, null),
                CancellationToken.None);

        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem
                {
                    DbId = includeKnownUserId ? user.Id : null,
                    Role = "user",
                    Content = "Request",
                },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "Review result",
                    ToolCallsJson = reviewJson,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        db.ChangeTracker.Clear();
        var rows = await db.aiConversationMessages
            .Where(m => m.conversationId == conversation.Id)
            .OrderBy(m => m.Id).ToListAsync();
        var review = Assert.Single(rows, m => m.content == "Review result");
        var metadata = JsonNode.Parse(review.toolCallsJson!)!;
        Assert.Equal("A", metadata["executionProposalId"]!.GetValue<string>());
        Assert.Null(metadata["plan"]);
        Assert.NotEqual(execution.Id, review.Id);
        // The executed parent row must not be overwritten or lose its plan A.
        var persistedExecution = Assert.Single(rows, m => m.Id == execution.Id);
        Assert.Equal("", persistedExecution.content);
        var parent = JsonNode.Parse(persistedExecution.toolCallsJson!)!;
        Assert.Equal("A", parent["plan"]?["proposalId"]?.GetValue<string>());
        Assert.Equal(true, parent["planExecuted"]?.GetValue<bool>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdlessEmptyReview_DoesNotMatchOrRebaseExecutedPlanRow(bool includeKnownUserId)
    {
        // An id-less review with empty content shares role+content with the executed
        // plan row: anonymous matching AND the final-assistant rebase must both refuse
        // to merge it, so the review becomes its own row instead of inheriting plan A.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "m0-d3-empty-review", 1, 2, CancellationToken.None);
        var start = DateTime.UtcNow;
        var user = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "user",
            content = "Request",
            createdAt = start,
        };
        var execution = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "",
            createdAt = start.AddMilliseconds(1),
            toolCallsJson = """
                {"_ui":"assistantTurn","plan":{"proposalId":"A"},"planConfirmed":true,"planExecuted":true,"planExecStart":0,"tools":[],"timeline":[]}
                """,
        };
        db.aiConversationMessages.AddRange(user, execution);
        await db.SaveChangesAsync();
        const string reviewJson = """
            {"_ui":"assistantTurn","executionProposalId":"A","tools":[],"timeline":[]}
            """;

        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem
                {
                    DbId = includeKnownUserId ? user.Id : null,
                    Role = "user",
                    Content = "Request",
                },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "",
                    ToolCallsJson = reviewJson,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        db.ChangeTracker.Clear();
        var rows = await db.aiConversationMessages
            .Where(m => m.conversationId == conversation.Id)
            .OrderBy(m => m.Id).ToListAsync();
        var review = Assert.Single(rows,
            m => m.role == "assistant" && m.Id != execution.Id);
        var metadata = JsonNode.Parse(review.toolCallsJson!)!;
        Assert.Equal("A", metadata["executionProposalId"]!.GetValue<string>());
        Assert.Null(metadata["plan"]);
        Assert.Null(metadata["planExecuted"]);
        var persistedExecution = Assert.Single(rows, m => m.Id == execution.Id);
        Assert.Equal("", persistedExecution.content);
        var parent = JsonNode.Parse(persistedExecution.toolCallsJson!)!;
        Assert.Equal("A", parent["plan"]?["proposalId"]?.GetValue<string>());
        Assert.Equal(true, parent["planExecuted"]?.GetValue<bool>());
    }

    [Theory]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":{"proposalId":"B"}}""",
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":{"proposalId":"B"}}""",
        true)]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":{"proposalId":"B"}}""",
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":{"proposalId":"C"}}""",
        false)]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":{"proposalId":"B"}}""",
        """{"_ui":"assistantTurn","executionProposalId":"A"}""",
        false)]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":"A"}""",
        """{"_ui":"assistantTurn","executionProposalId":"Z"}""",
        false)]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":123}""",
        """{"_ui":"assistantTurn","executionProposalId":123}""",
        false)]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":"oops"}""",
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":"oops"}""",
        false)]
    [InlineData(
        """{"_ui":"assistantTurn","executionProposalId":"A","plan":{}}""",
        """{"_ui":"assistantTurn","executionProposalId":"A"}""",
        false)]
    [InlineData(
        """{"_ui":"assistantTurn","plan":{"proposalId":"A"}}""",
        """{"_ui":"assistantTurn","plan":{"proposalId":"Z"}}""",
        true)]
    public void HasCompatibleReviewIdentity_RejectsIncompatibleReviewEnvelopes(
        string storedJson, string incomingJson, bool expected)
    {
        Assert.Equal(
            expected,
            AiConversationTurnStore.HasCompatibleReviewIdentity(storedJson, incomingJson));
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
