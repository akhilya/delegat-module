using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("ai")]
public class AiConversationController : ControllerBase
{
    private readonly DataContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiChatService _chatService;
    private readonly AiLiveTurnHub _liveTurns;

    public AiConversationController(
        DataContext context,
        ICurrentUserService currentUserService,
        AiChatService chatService,
        AiLiveTurnHub liveTurns)
    {
        _context = context;
        _currentUserService = currentUserService;
        _chatService = chatService;
        _liveTurns = liveTurns;
    }

    // GET /api/ai/conversations - list recent conversations
    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations(CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var convs = await _context.aiConversations
            .Where(c => c.storeId == storeId.Value && c.userId == userId.Value)
            .OrderByDescending(c => c.lastMessageAt)
            .Take(30)
            .Select(c => new { c.sessionId, c.title, c.messageCount, c.lastMessageAt })
            .ToListAsync(ct);

        return Ok(convs);
    }

    // GET /api/ai/conversations/{sessionId} - load specific conversation
    [HttpGet("conversations/{sessionId}")]
    public async Task<IActionResult> GetConversation(string sessionId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var conv = await _context.aiConversations
            .Include(c => c.messages.OrderBy(m => m.createdAt))
            .FirstOrDefaultAsync(c => c.sessionId == sessionId
                && c.storeId == storeId.Value && c.userId == userId.Value, ct);

        if (conv == null) return NotFound();

        return Ok(ConversationDto(conv, storeId.Value, userId.Value));
    }

    // PATCH /api/ai/conversations/{sessionId} - rename conversation
    [HttpPatch("conversations/{sessionId}")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> RenameConversation(string sessionId, [FromBody] RenameRequest req, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var conv = await _context.aiConversations
            .FirstOrDefaultAsync(c => c.sessionId == sessionId
                && c.storeId == storeId.Value && c.userId == userId.Value, ct);
        if (conv == null) return NotFound();

        conv.title = req.Title?.Trim();
        await _context.SaveChangesAsync(ct);
        return Ok(new { title = conv.title });
    }

    // GET /api/ai/conversations/latest - load latest conversation for this user/store
    [HttpGet("conversations/latest")]
    public async Task<IActionResult> GetLatestConversation(CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var conv = await _context.aiConversations
            .Include(c => c.messages.OrderBy(m => m.createdAt))
            .Where(c => c.storeId == storeId.Value && c.userId == userId.Value)
            .OrderByDescending(c => c.lastMessageAt)
            .FirstOrDefaultAsync(ct);

        if (conv == null) return Ok(null);

        return Ok(ConversationDto(conv, storeId.Value, userId.Value));
    }

    // POST /api/ai/conversations/{sessionId}/generate-title
    [HttpPost("conversations/{sessionId}/generate-title")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> GenerateTitle(string sessionId, [FromBody] GenerateTitleRequest req, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var conv = await _context.aiConversations
            .FirstOrDefaultAsync(c => c.sessionId == sessionId
                && c.storeId == storeId.Value && c.userId == userId.Value, ct);
        if (conv == null) return NotFound();

        var title = await _chatService.GenerateTitleAsync(req.FirstUserMessage, ct);
        conv.title = title;
        await _context.SaveChangesAsync(ct);

        return Ok(new { title });
    }

    // POST /api/ai/conversations/messages - append a conversation turn to DB
    [HttpPost("conversations/messages")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> SaveMessages(
        [FromBody] SaveMessagesRequest req, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        if (string.IsNullOrEmpty(req.SessionId)) return BadRequest("sessionId required");

        var conv = await AiConversationTurnStore.EnsureConversationAsync(
            _context, req.SessionId, storeId.Value, userId.Value, ct);
        if (conv.modelName == null && req.ModelName != null)
            conv.modelName = req.ModelName;

        var existing = await _context.aiConversationMessages
            .Where(m => m.conversationId == conv.Id)
            .OrderBy(m => m.createdAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        var saved = new List<object>();
        var historyUpdates = new Dictionary<int, SaveMessageItem>();
        var hasIds = req.Messages.Any(m => m.DbId is > 0);

        if (hasIds)
        {
            // A client snapshot can race the live-turn persistence path. Treat it as an
            // upsert, not an authoritative replacement: omitted rows may have been added
            // by the server after the client built this request.
            var byId = existing.ToDictionary(m => m.Id);
            var indexById = existing
                .Select((message, index) => (message.Id, index))
                .ToDictionary(item => item.Id, item => item.index);
            var lastKnownIndex = req.Messages
                .Where(message => message.DbId is > 0)
                .Select(message => indexById.GetValueOrDefault(message.DbId!.Value, -1))
                .DefaultIfEmpty(-1)
                .Max();
            var nextUnpersistedIndex = lastKnownIndex + 1;
            foreach (var m in req.Messages)
            {
                if (m.DbId is > 0 && byId.TryGetValue(m.DbId.Value, out var row))
                {
                    row.role = m.Role;
                    row.content = m.Content;
                    row.toolCallsJson = AiConversationTurnStore.PreserveServerPlan(row.toolCallsJson, m.ToolCallsJson);
                    historyUpdates[row.Id] = m;
                    continue;
                }

                if (m.DbId is > 0) continue;

                var matchingIndex = existing.FindIndex(
                    Math.Max(0, nextUnpersistedIndex),
                    candidate => string.Equals(candidate.role, m.Role, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(candidate.content, m.Content, StringComparison.Ordinal));
                if (matchingIndex >= 0)
                {
                    var matched = existing[matchingIndex];
                    nextUnpersistedIndex = matchingIndex + 1;
                    matched.toolCallsJson = AiConversationTurnStore.PreserveServerPlan(
                        matched.toolCallsJson, m.ToolCallsJson);
                    historyUpdates[matched.Id] = m;
                    saved.Add(new { role = m.Role, dbId = matched.Id });
                    continue;
                }

                var msg = new AiConversationMessage
                {
                    conversationId = conv.Id,
                    role = m.Role,
                    content = m.Content,
                    toolCallsJson = AiConversationTurnStore.PreserveServerPlan(null, m.ToolCallsJson),
                    createdAt = m.CreatedAt ?? DateTime.UtcNow,
                };
                _context.aiConversationMessages.Add(msg);
                await SaveHistoryChangesAsync(historyUpdates, ct);
                saved.Add(new { role = m.Role, dbId = msg.Id });
            }

            conv.lastMessageAt = DateTime.UtcNow;
            conv.messageCount = await _context.aiConversationMessages
                .CountAsync(message => message.conversationId == conv.Id, ct);
            if (conv.modelName == null && req.ModelName != null)
                conv.modelName = req.ModelName;
            await SaveHistoryChangesAsync(historyUpdates, ct);
            return Ok(new { saved });
        }

        var nextExistingIndex = 0;
        for (var requestIndex = 0; requestIndex < req.Messages.Count; requestIndex++)
        {
            var incoming = req.Messages[requestIndex];
            var matchingIndex = existing.FindIndex(
                nextExistingIndex,
                candidate => string.Equals(candidate.role, incoming.Role, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.content, incoming.Content, StringComparison.Ordinal));
            if (matchingIndex >= 0)
            {
                var matched = existing[matchingIndex];
                nextExistingIndex = matchingIndex + 1;
                matched.toolCallsJson = AiConversationTurnStore.PreserveServerPlan(
                    matched.toolCallsJson, incoming.ToolCallsJson);
                historyUpdates[matched.Id] = incoming;
                saved.Add(new { role = incoming.Role, dbId = matched.Id });
                continue;
            }

            // The live turn can persist its final assistant row before a client sync
            // arrives with a slightly different snapshot. Rebase that final row instead
            // of appending a duplicate assistant message.
            if (requestIndex == req.Messages.Count - 1
                && string.Equals(incoming.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                && nextExistingIndex == existing.Count - 1
                && nextExistingIndex >= 0
                && existing[nextExistingIndex].role == "assistant")
            {
                var last = existing[nextExistingIndex];
                last.content = incoming.Content;
                last.toolCallsJson = AiConversationTurnStore.PreserveServerPlan(
                    last.toolCallsJson, incoming.ToolCallsJson);
                historyUpdates[last.Id] = incoming;
                saved.Add(new { role = incoming.Role, dbId = last.Id });
                nextExistingIndex++;
                continue;
            }

            var msg = new AiConversationMessage
            {
                conversationId = conv.Id,
                role = incoming.Role,
                content = incoming.Content,
                toolCallsJson = AiConversationTurnStore.PreserveServerPlan(null, incoming.ToolCallsJson),
                createdAt = incoming.CreatedAt ?? DateTime.UtcNow,
            };
            _context.aiConversationMessages.Add(msg);
            await SaveHistoryChangesAsync(historyUpdates, ct);
            saved.Add(new { role = incoming.Role, dbId = msg.Id });
            existing.Add(msg);
            nextExistingIndex = existing.Count;
        }

        conv.lastMessageAt = DateTime.UtcNow;
        conv.messageCount = await _context.aiConversationMessages
            .CountAsync(message => message.conversationId == conv.Id, ct);
        if (conv.modelName == null && req.ModelName != null)
            conv.modelName = req.ModelName;
        await SaveHistoryChangesAsync(historyUpdates, ct);

        return Ok(new { saved });
    }

    private async Task SaveHistoryChangesAsync(
        IReadOnlyDictionary<int, SaveMessageItem> requestedUpdates,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _context.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < 2)
            {
                foreach (var entry in exception.Entries)
                {
                    if (entry.Entity is not AiConversationMessage message)
                        throw;

                    await entry.ReloadAsync(ct);
                    if (entry.State == EntityState.Detached
                        || !requestedUpdates.TryGetValue(message.Id, out var incoming))
                        continue;

                    // Rebase client display/form metadata on the latest row. PreserveServerPlan
                    // makes the server's current plan and execution flags authoritative.
                    message.role = incoming.Role;
                    message.content = incoming.Content;
                    message.toolCallsJson = AiConversationTurnStore.PreserveServerPlan(
                        message.toolCallsJson, incoming.ToolCallsJson);
                }
            }
        }
    }

    // DELETE /api/ai/conversations/{sessionId} - delete a conversation and its messages
    [HttpDelete("conversations/{sessionId}")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteConversation(string sessionId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var conv = await _context.aiConversations
            .FirstOrDefaultAsync(c => c.sessionId == sessionId
                && c.storeId == storeId.Value && c.userId == userId.Value, ct);
        if (conv == null) return NotFound();

        _context.aiConversations.Remove(conv);
        await _context.SaveChangesAsync(ct);
        return Ok(new { deleted = true });
    }

    // POST /api/ai/messages/{id}/feedback - set 👍/👎 on an assistant message
    [HttpPost("messages/{id:int}/feedback")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> SetFeedback(int id, [FromBody] FeedbackRequest req, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId  = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var msg = await _context.aiConversationMessages
            .Include(m => m.conversation)
            .FirstOrDefaultAsync(
                m => m.Id == id && m.conversation.storeId == storeId.Value && m.conversation.userId == userId.Value, ct);

        if (msg == null) return NotFound();

        msg.feedbackRating = req.Rating;
        await _context.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    private object ConversationDto(AiConversation conv, int storeId, int userId) => new
    {
        sessionId = conv.sessionId,
        title = conv.title,
        generating = _liveTurns.IsGenerating(conv.sessionId, storeId, userId),
        messages = conv.messages.Select(m => new
        {
            dbId = m.Id,
            role = m.role,
            content = m.content,
            toolCallsJson = m.toolCallsJson,
            feedbackRating = m.feedbackRating,
        })
    };
}

public class SaveMessagesRequest
{
    public string SessionId { get; set; } = "";
    public string? ModelName { get; set; }
    public List<SaveMessageItem> Messages { get; set; } = [];
}

public class SaveMessageItem
{
    public int? DbId { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? ToolCallsJson { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public class FeedbackRequest
{
    public int Rating { get; set; }
}

public class GenerateTitleRequest
{
    public string FirstUserMessage { get; set; } = "";
}

public class RenameRequest
{
    public string? Title { get; set; }
}
