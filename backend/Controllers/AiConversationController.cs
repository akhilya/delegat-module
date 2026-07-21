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

    public AiConversationController(DataContext context, ICurrentUserService currentUserService, AiChatService chatService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _chatService = chatService;
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

        return Ok(new
        {
            sessionId = conv.sessionId,
            title     = conv.title,
            messages  = conv.messages.Select(m => new
            {
                dbId           = m.Id,
                role           = m.role,
                content        = m.content,
                toolCallsJson  = m.toolCallsJson,
                feedbackRating = m.feedbackRating,
            })
        });
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

        return Ok(new
        {
            sessionId = conv.sessionId,
            title     = conv.title,
            messages  = conv.messages.Select(m => new
            {
                dbId          = m.Id,
                role          = m.role,
                content       = m.content,
                toolCallsJson = m.toolCallsJson,
                feedbackRating = m.feedbackRating,
            })
        });
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

        // Upsert conversation row - always scope to current store+user to prevent cross-store bleed
        var conv = await _context.aiConversations
            .FirstOrDefaultAsync(c => c.sessionId == req.SessionId
                && c.storeId == storeId.Value && c.userId == userId.Value, ct);
        if (conv == null)
        {
            conv = new AiConversation
            {
                sessionId = req.SessionId,
                storeId   = storeId.Value,
                userId    = userId.Value,
                modelName = req.ModelName,
            };
            _context.aiConversations.Add(conv);
            await _context.SaveChangesAsync(ct);
        }

        // Only append messages beyond what's already saved (frontend sends full history)
        var savedCount = await _context.aiConversationMessages
            .CountAsync(m => m.conversationId == conv.Id, ct);

        var newMessages = req.Messages.Skip(savedCount).ToList();
        var saved = new List<object>();

        foreach (var m in newMessages)
        {
            var msg = new AiConversationMessage
            {
                conversationId = conv.Id,
                role           = m.Role,
                content        = m.Content,
                toolCallsJson  = m.ToolCallsJson,
                createdAt      = m.CreatedAt ?? DateTime.UtcNow,
            };
            _context.aiConversationMessages.Add(msg);
            await _context.SaveChangesAsync(ct);
            saved.Add(new { role = m.Role, dbId = msg.Id });
        }

        conv.lastMessageAt = DateTime.UtcNow;
        conv.messageCount  = savedCount + newMessages.Count;
        if (conv.modelName == null && req.ModelName != null)
            conv.modelName = req.ModelName;
        await _context.SaveChangesAsync(ct);

        return Ok(new { saved });
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
}

public class SaveMessagesRequest
{
    public string SessionId { get; set; } = "";
    public string? ModelName { get; set; }
    public List<SaveMessageItem> Messages { get; set; } = [];
}

public class SaveMessageItem
{
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
