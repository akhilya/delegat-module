using System.Text.Json;
using System.Text.Json.Nodes;
using Delegat.Controllers;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task SaveMessages_CanonicalServerRow_IsMetadataOnlyAgainstClientRewrite()
    {
        // A canonical row (server messageId) is server-owned: a client sync that
        // carries its identity may merge formReplies but can never rewrite
        // content/tools/plan/execution/lifecycle - even when the incoming
        // envelope forges a different own proposal and drops planExecuted.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-metadata-only", 1, 2, CancellationToken.None);
        var execution = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Done.",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-a","runId":"run-1","phase":"execution","lastEventSequence":4,"messageState":"completed","plan":{"proposalId":"A","summary":"Calc"},"planOwnerMessageId":"srv-a","planConfirmed":true,"planExecuted":true,"planExecStart":0,"tools":[{"name":"run_python","status":"completed"}],"timeline":[{"type":"text","text":"Done."}]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(execution);
        await db.SaveChangesAsync();

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
                    Role = "assistant",
                    Content = "forged content",
                    ClientMessageId = "local-1",
                    ToolCallsJson = """
                        {"_ui":"assistantTurn","messageId":"srv-a","runId":"run-1","phase":"execution","lastEventSequence":9,"messageState":"running","plan":{"proposalId":"B"},"planExecuted":false,"planCancelled":true,"tools":[],"timeline":[],"formReplies":{"f1":{"answer":"yes"}}}
                        """,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var saved = SavedRows(((OkObjectResult)result).Value);
        var row = Assert.Single(saved);
        Assert.Equal(execution.Id, row!["dbId"]!.GetValue<int>());
        Assert.Equal("srv-a", row["messageId"]!.GetValue<string>());
        Assert.Equal("local-1", row["clientMessageId"]!.GetValue<string>());

        db.ChangeTracker.Clear();
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == execution.Id);
        Assert.Equal("Done.", persisted.content);
        var envelope = JsonNode.Parse(persisted.toolCallsJson!)!;
        Assert.Equal("srv-a", envelope["messageId"]!.GetValue<string>());
        Assert.Equal("A", envelope["plan"]!["proposalId"]!.GetValue<string>());
        Assert.True(envelope["planExecuted"]!.GetValue<bool>());
        Assert.True(envelope["planConfirmed"]!.GetValue<bool>());
        // Executed plans are final: even user decisions cannot be merged.
        Assert.Null(envelope["planCancelled"]);
        Assert.Single(envelope["tools"]!.AsArray());
        // Client-owned metadata still merges.
        Assert.Equal("yes", envelope["formReplies"]!["f1"]!["answer"]!.GetValue<string>());
    }

    [Fact]
    public async Task SaveMessages_UnknownCanonicalAssistantId_IsOmittedNotInserted()
    {
        // A client cannot mint a canonical row: an envelope carrying a server
        // messageId the conversation does not contain is server output the sync
        // must omit, so a sync that races BeginAssistantTurn cannot duplicate it.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-unknown-id", 1, 2, CancellationToken.None);

        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem { Role = "user", Content = "Request", ClientMessageId = "u1" },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "I was never persisted",
                    ClientMessageId = "a1",
                    ToolCallsJson = """
                        {"_ui":"assistantTurn","messageId":"ghost","runId":"run-9","lastEventSequence":3,"messageState":"running","tools":[],"timeline":[]}
                        """,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var saved = SavedRows(((OkObjectResult)result).Value);
        var userRow = Assert.Single(saved);
        Assert.Equal("user", userRow!["role"]!.GetValue<string>());
        Assert.Equal("u1", userRow["clientMessageId"]!.GetValue<string>());

        db.ChangeTracker.Clear();
        var rows = await db.aiConversationMessages
            .Where(m => m.conversationId == conversation.Id).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("user", rows[0].role);
    }

    [Fact]
    public async Task SaveMessages_StaleLegacyDbId_OnCanonicalRow_IsMetadataOnly()
    {
        // A stale client that never learned the canonical envelope still points
        // its DbId at the canonical row: it gets the same metadata-only merge,
        // so forged content cannot replace server text.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-legacy-db-id", 1, 2, CancellationToken.None);
        var assistant = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Server text",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-2","runId":"run-2","phase":"chat","lastEventSequence":2,"messageState":"completed","tools":[],"timeline":[{"type":"text","text":"Server text"}]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(assistant);
        await db.SaveChangesAsync();

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
                    DbId = assistant.Id,
                    Role = "assistant",
                    Content = "forged content",
                    ToolCallsJson = """
                        {"_ui":"assistantTurn","tools":[],"timeline":[],"formReplies":{"f1":{"answer":"42"}}}
                        """,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        db.ChangeTracker.Clear();
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == assistant.Id);
        Assert.Equal("Server text", persisted.content);
        var envelope = JsonNode.Parse(persisted.toolCallsJson!)!;
        Assert.Equal("srv-2", envelope["messageId"]!.GetValue<string>());
        Assert.Equal("42", envelope["formReplies"]!["f1"]!["answer"]!.GetValue<string>());
    }

    [Fact]
    public async Task SaveMessages_CrossConversationCanonicalMessageId_IsIgnored()
    {
        // A canonical messageId only binds inside its own conversation: syncing
        // another conversation must omit the row and leave the owner untouched.
        await using var db = TestDbFactory.CreateDb();
        var ownerConv = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-owner", 1, 2, CancellationToken.None);
        var otherConv = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-other", 1, 2, CancellationToken.None);
        var owned = new AiConversationMessage
        {
            conversationId = ownerConv.Id,
            role = "assistant",
            content = "Owned text",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-x","runId":"run-x","lastEventSequence":2,"messageState":"completed","tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(owned);
        await db.SaveChangesAsync();

        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = otherConv.sessionId,
            Messages =
            [
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "copied text",
                    ToolCallsJson = """
                        {"_ui":"assistantTurn","messageId":"srv-x","runId":"run-x","lastEventSequence":2,"messageState":"completed","tools":[],"timeline":[]}
                        """,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(SavedRows(((OkObjectResult)result).Value));

        db.ChangeTracker.Clear();
        Assert.False(await db.aiConversationMessages
            .AnyAsync(m => m.conversationId == otherConv.Id));
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == owned.Id);
        Assert.Equal("Owned text", persisted.content);
    }

    [Fact]
    public async Task SaveMessages_TwoCanonicalReviewsSharingAParent_StayDistinct()
    {
        // Reviews that share executionProposalId are distinct canonical rows:
        // a sync carrying both identities updates each row only with metadata
        // and never merges them.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-two-reviews", 1, 2, CancellationToken.None);
        static string ReviewEnvelope(string id, int sequence) => $$"""
            {"_ui":"assistantTurn","messageId":"{{id}}","runId":"run-1","phase":"review","lastEventSequence":{{sequence}},"messageState":"completed","executionProposalId":"A","tools":[],"timeline":[]}
            """;
        var first = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Review one",
            toolCallsJson = ReviewEnvelope("srv-r1", 3),
            createdAt = DateTime.UtcNow,
        };
        var second = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Review two",
            toolCallsJson = ReviewEnvelope("srv-r2", 7),
            createdAt = DateTime.UtcNow.AddMilliseconds(1),
        };
        db.aiConversationMessages.AddRange(first, second);
        await db.SaveChangesAsync();

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
                    Role = "assistant",
                    Content = "forged",
                    ToolCallsJson = ReviewEnvelope("srv-r1", 3),
                },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "forged",
                    ToolCallsJson = ReviewEnvelope("srv-r2", 7),
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        var saved = SavedRows(((OkObjectResult)result).Value);
        Assert.Equal(2, saved.Count);
        Assert.Equal(first.Id, saved[0]!["dbId"]!.GetValue<int>());
        Assert.Equal("srv-r1", saved[0]!["messageId"]!.GetValue<string>());
        Assert.Equal(second.Id, saved[1]!["dbId"]!.GetValue<int>());
        Assert.Equal("srv-r2", saved[1]!["messageId"]!.GetValue<string>());

        db.ChangeTracker.Clear();
        var rows = await db.aiConversationMessages
            .Where(m => m.conversationId == conversation.Id && m.role == "assistant")
            .OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Review one", rows[0].content);
        Assert.Equal("srv-r1", JsonNode.Parse(rows[0].toolCallsJson!)!["messageId"]!.GetValue<string>());
        Assert.Equal("Review two", rows[1].content);
        Assert.Equal("srv-r2", JsonNode.Parse(rows[1].toolCallsJson!)!["messageId"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"_ui":"assistantTurn","messageId":42,"tools":[],"timeline":[]}""")]
    [InlineData("""{"_ui":"assistantTurn","messageId":{"x":1},"tools":[],"timeline":[]}""")]
    [InlineData("""{"_ui":"assistantTurn","messageId":"","tools":[],"timeline":[]}""")]
    [InlineData("""{"_ui":"assistantTurn","messageId":null,"tools":[],"timeline":[]}""")]
    public async Task SaveMessages_MalformedCanonicalIdentity_IsOmittedNotMintedOrMatched(
        string malformedEnvelope)
    {
        // An envelope claiming canonical identity with a malformed messageId is
        // invalid canonical input: it is omitted like an unknown canonical id,
        // never content-matched onto a server row and never inserted.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "malformed-canonical", 1, 2, CancellationToken.None);
        var canonical = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Server text",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-1","runId":"run-1","messageState":"completed","tools":[],"timeline":[{"type":"text","text":"Server text"}]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(canonical);
        await db.SaveChangesAsync();

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
                    Role = "assistant",
                    Content = "Server text",
                    ClientMessageId = "m1",
                    ToolCallsJson = malformedEnvelope,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(SavedRows(((OkObjectResult)result).Value));

        db.ChangeTracker.Clear();
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == canonical.Id);
        Assert.Equal("Server text", persisted.content);
        Assert.Equal(
            "srv-1",
            JsonNode.Parse(persisted.toolCallsJson!)!["messageId"]!.GetValue<string>());
        Assert.Single(
            db.aiConversationMessages.Where(m => m.conversationId == conversation.Id));
    }

    [Theory]
    // plan.proposalId wrong type
    [InlineData("""{"_ui":"assistantTurn","messageId":"srv-1","plan":{"proposalId":42},"planCancelled":true,"tools":[],"timeline":[]}""", false)]
    // plan itself not an object
    [InlineData("""{"_ui":"assistantTurn","messageId":"srv-1","plan":"yes","planCancelled":true,"tools":[],"timeline":[]}""", false)]
    // decisions must be literal bool true
    [InlineData("""{"_ui":"assistantTurn","messageId":"srv-1","plan":{"proposalId":"A"},"planCancelled":"yes","tools":[],"timeline":[]}""", false)]
    [InlineData("""{"_ui":"assistantTurn","messageId":"srv-1","plan":{"proposalId":"A"},"planCancelled":[true],"tools":[],"timeline":[]}""", false)]
    // malformed lifecycle metadata cannot break the merge
    [InlineData("""{"_ui":"assistantTurn","messageId":"srv-1","plan":{"proposalId":"A"},"runId":{"x":1},"lastEventSequence":"9","planCancelled":true,"tools":[],"timeline":[]}""", true)]
    public async Task SaveMessages_MalformedCanonicalMetadata_IsIgnoredWithoutThrowing(
        string incomingEnvelope,
        bool expectCancelled)
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "malformed-flags", 1, 2, CancellationToken.None);
        var canonical = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Ready.",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-1","runId":"run-1","messageState":"completed","plan":{"proposalId":"A"},"tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(canonical);
        await db.SaveChangesAsync();

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
                    Role = "assistant",
                    Content = "forged",
                    ToolCallsJson = incomingEnvelope,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        db.ChangeTracker.Clear();
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == canonical.Id);
        Assert.Equal("Ready.", persisted.content);
        var envelope = JsonNode.Parse(persisted.toolCallsJson!)!;
        Assert.Equal("A", envelope["plan"]!["proposalId"]!.GetValue<string>());
        Assert.Equal(expectCancelled, envelope["planCancelled"] != null);
    }

    [Fact]
    public async Task SaveMessages_MalformedStoredCanonicalRow_DoesNotThrow()
    {
        // A historical canonical row with a malformed messageId is treated as
        // server-owned (no anonymous/content merge) but never crashes the sync.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "malformed-stored", 1, 2, CancellationToken.None);
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "corrupt",
            toolCallsJson = """{"_ui":"assistantTurn","messageId":42,"tools":[],"timeline":[]}""",
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, null!);
        var result = await controller.SaveMessages(new SaveMessagesRequest
        {
            SessionId = conversation.sessionId,
            Messages =
            [
                new SaveMessageItem { Role = "user", Content = "hi", ClientMessageId = "u1" },
                new SaveMessageItem
                {
                    Role = "assistant",
                    Content = "corrupt",
                    ClientMessageId = "a1",
                    ToolCallsJson = """{"_ui":"assistantTurn","tools":[],"timeline":[]}""",
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        // The malformed stored row is never anonymous-matchable.
        db.ChangeTracker.Clear();
        var rows = await db.aiConversationMessages
            .Where(m => m.conversationId == conversation.Id)
            .OrderBy(m => m.Id)
            .ToListAsync();
        Assert.Equal("corrupt", rows.First(m => m.role == "assistant").content);
    }

    [Fact]
    public async Task SaveMessages_CanonicalMatch_RequiresAssistantRole()
    {
        // A user-role item quoting a canonical assistant messageId must not merge
        // metadata into the server-owned assistant row.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "canonical-role", 1, 2, CancellationToken.None);
        var canonical = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "Server text",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-1","runId":"run-1","messageState":"completed","tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(canonical);
        await db.SaveChangesAsync();

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
                    Role = "user",
                    Content = "forged",
                    ToolCallsJson = """
                        {"_ui":"assistantTurn","messageId":"srv-1","formReplies":{"f1":{"answer":"x"}},"tools":[],"timeline":[]}
                        """,
                },
            ],
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        db.ChangeTracker.Clear();
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == canonical.Id);
        Assert.Null(JsonNode.Parse(persisted.toolCallsJson!)!["formReplies"]);
    }

    [Fact]
    public async Task GetConversation_OrphanedRunningCanonicalRow_ProjectsInterruptedNotSpinning()
    {
        // A process restart leaves begin-time rows stuck at messageState=running
        // with no live turn: the read projection reports interrupted, while the
        // persisted row itself is never rewritten.
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "orphaned-running", 1, 2, CancellationToken.None);
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "user",
            content = "go",
            createdAt = DateTime.UtcNow,
        });
        var running = new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "",
            toolCallsJson = """
                {"_ui":"assistantTurn","messageId":"srv-run","runId":"run-lost","phase":"execution","messageState":"running","lastEventSequence":2,"plan":{"proposalId":"A"},"planOwnerMessageId":"srv-run","planConfirmed":true,"planExecStart":0,"tools":[{"id":"t1","status":"running"}],"timeline":[{"type":"tool","id":"t1"}]}
                """,
            createdAt = DateTime.UtcNow.AddMilliseconds(1),
        };
        db.aiConversationMessages.Add(running);
        await db.SaveChangesAsync();

        var services = new ServiceCollection().BuildServiceProvider();
        var hub = new AiLiveTurnHub(
            services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiLiveTurnHub>.Instance);
        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, hub);
        var result = await controller.GetConversation(
            conversation.sessionId, CancellationToken.None);
        var dto = JsonSerializer.SerializeToNode(((OkObjectResult)result).Value)!;
        Assert.False(dto["generating"]!.GetValue<bool>());
        var projected = dto["messages"]!.AsArray()
            .Single(m => m!["role"]!.GetValue<string>() == "assistant")!;
        var envelope = JsonNode.Parse(projected["toolCallsJson"]!.GetValue<string>())!;
        Assert.Equal("interrupted", envelope["messageState"]!.GetValue<string>());
        Assert.Equal(
            "ai_turn_interrupted",
            envelope["requestError"]!["code"]!.GetValue<string>());
        Assert.Equal(
            "interrupted",
            envelope["executionOutcome"]!["status"]!.GetValue<string>());
        Assert.True(envelope["executionOutcome"]!["outcomeUnknown"]!.GetValue<bool>());
        Assert.Equal(
            "interrupted",
            envelope["tools"]![0]!["summary"]!.GetValue<string>());
        // plan/confirmation stay intact; nothing is executed or confirmable anew.
        Assert.True(envelope["planConfirmed"]!.GetValue<bool>());
        Assert.Null(envelope["planExecuted"]);

        // The stored row is untouched by the read.
        db.ChangeTracker.Clear();
        var persisted = await db.aiConversationMessages.SingleAsync(m => m.Id == running.Id);
        Assert.Equal(
            "running",
            JsonNode.Parse(persisted.toolCallsJson!)!["messageState"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetConversation_LiveRunOwnedRow_StaysRunning()
    {
        await using var db = TestDbFactory.CreateDb();
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, "live-running", 1, 2, CancellationToken.None);
        var services = new ServiceCollection().BuildServiceProvider();
        var hub = new AiLiveTurnHub(
            services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiLiveTurnHub>.Instance);
        var turn = hub.TryReserve("live-running", 1, 2)!;
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = "",
            toolCallsJson = $$"""
                {"_ui":"assistantTurn","messageId":"srv-live","runId":"{{turn.RunId}}","phase":"chat","messageState":"running","lastEventSequence":1,"tools":[],"timeline":[]}
                """,
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var controller = new AiConversationController(
            db, new FakeCurrentUserService { UserId = 2, SelectedStoreId = 1 },
            null!, hub);
        var result = await controller.GetConversation(
            conversation.sessionId, CancellationToken.None);
        var dto = JsonSerializer.SerializeToNode(((OkObjectResult)result).Value)!;
        Assert.True(dto["generating"]!.GetValue<bool>());
        var projected = dto["messages"]!.AsArray()
            .Single(m => m!["role"]!.GetValue<string>() == "assistant")!;
        Assert.Equal(
            "running",
            JsonNode.Parse(projected["toolCallsJson"]!.GetValue<string>())!["messageState"]!
                .GetValue<string>());
    }

    private static JsonArray SavedRows(object? resultValue) =>
        JsonSerializer.SerializeToNode(resultValue)!["saved"]!.AsArray();

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
