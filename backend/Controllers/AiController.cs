using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("ai")]
public class AiController : ControllerBase
{
    public const string ConfirmedPlanUserText = AiConversationTurnStore.ConfirmedPlanUserText;

    private readonly AiChatService _chatService;
    private readonly AiOperationService _operationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiLiveTurnHub _liveTurns;
    private readonly AgentFileUploadService _uploads;
    private readonly IStorageService _storage;

    public AiController(
        AiChatService chatService,
        AiOperationService operationService,
        ICurrentUserService currentUserService,
        AiLiveTurnHub liveTurns,
        AgentFileUploadService uploads,
        IStorageService storage)
    {
        _chatService = chatService;
        _operationService = operationService;
        _currentUserService = currentUserService;
        _liveTurns = liveTurns;
        _uploads = uploads;
        _storage = storage;
    }

    // POST /api/ai/chat - start (or attach to) a live SSE turn
    [HttpPost("chat")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);

        if (userId == null || storeId == null)
            return Unauthorized();

        var locale = Request.Headers["X-UI-Locale"].ToString();
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            PrepareSseHeaders();
            await WriteSseAsync(
                _chatService.StreamChatAsync(
                    request.Messages, storeId.Value, userId.Value, request.ModelId, request.PageContext,
                    request.SessionId, locale, ct),
                ct);
            return new EmptyResult();
        }

        var sessionId = request.SessionId;
        var messages = request.Messages;
        var modelId = request.ModelId;
        var pageContext = request.PageContext;
        var lastUser = LastPlainUser(messages);

        _liveTurns.TryStart(
            sessionId, storeId.Value, userId.Value, lastUser,
            (sp, token) => sp.GetRequiredService<AiChatService>().StreamChatAsync(
                messages, storeId.Value, userId.Value, modelId, pageContext, sessionId, locale, token));

        var turn = _liveTurns.Find(sessionId, storeId.Value, userId.Value);
        if (turn == null)
            return NotFound();

        PrepareSseHeaders();
        await WriteSseAsync(turn.Subscribe(ct), ct);
        return new EmptyResult();
    }

    // POST /api/ai/chat/attachments?sessionId= — stage files for this chat (bytes never go to the model)
    [HttpPost("chat/attachments")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    [RequiresPermission(Permissions.FilesManage, requireStoreMembership: true)]
    public async Task<IActionResult> UploadChatAttachments(
        [FromQuery] string sessionId,
        [FromForm] IFormFile[] files,
        [FromForm] string[]? relativePaths,
        CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (userId == null || storeId == null)
            return Unauthorized();

        try
        {
            var staged = await _uploads.StageChatAttachmentsAsync(
                storeId.Value, userId.Value, sessionId, files ?? [], relativePaths, ct);
            return Ok(new { count = staged.Count, items = staged });
        }
        catch (InvalidOperationException ex)
        {
            throw ApiException.BadRequest(ex.Message);
        }
    }

    // GET /api/ai/chat/attachments/{id} — download a staged file while it is still live (24h)
    [HttpGet("chat/attachments/{id:int}")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task DownloadChatAttachment(int id, CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (userId == null || storeId == null)
            throw ApiException.Unauthorized();

        var row = await _uploads.GetLiveOwnedAsync(storeId.Value, userId.Value, id, ct);
        if (row == null)
            throw ApiException.NotFound("Attachment expired or not found", code: "attachment_expired");

        if (!await _storage.ExistsAsync(row.storageKey, ct))
            throw ApiException.NotFound("Attachment expired or not found", code: "attachment_expired");

        StorageDownloadHttp.Prepare(Response, row.contentType, row.fileName, inline: false, row.length);
        await _storage.StreamToAsync(row.storageKey, Response.Body, ct);
    }

    // GET /api/ai/chat/stream?sessionId= — replay + tail a live turn after reload
    [HttpGet("chat/stream")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> ChatStream([FromQuery] string sessionId, CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (userId == null || storeId == null)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest("sessionId required");

        var turn = _liveTurns.Find(sessionId, storeId.Value, userId.Value);
        if (turn == null)
            return NotFound();

        PrepareSseHeaders();
        await WriteSseAsync(turn.Subscribe(ct), ct);
        return new EmptyResult();
    }

    // POST /api/ai/execute-plan - deterministic execution of a user-confirmed plan (SSE)
    [HttpPost("execute-plan")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> ExecutePlan([FromBody] ExecutePlanRequest request, CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);

        if (userId == null || storeId == null)
            return Unauthorized();
        if (request.Steps.Count == 0)
            return BadRequest("steps are required");

        var steps = request.Steps;
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            PrepareSseHeaders();
            await WriteSseAsync(
                _chatService.ExecutePlanStreamAsync(steps, storeId.Value, userId.Value, ct),
                ct);
            return new EmptyResult();
        }

        var sessionId = request.SessionId;
        _liveTurns.TryStart(
            sessionId, storeId.Value, userId.Value, userMessage: null,
            (sp, token) => sp.GetRequiredService<AiChatService>()
                .ExecutePlanStreamAsync(steps, storeId.Value, userId.Value, token));

        var turn = _liveTurns.Find(sessionId, storeId.Value, userId.Value);
        if (turn == null)
            return NotFound();

        PrepareSseHeaders();
        await WriteSseAsync(turn.Subscribe(ct), ct);
        return new EmptyResult();
    }

    // POST /api/ai/stop - cancel server-side generation for the given session
    [HttpPost("stop")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> Stop([FromBody] StopRequest req, CancellationToken ct)
    {
        EnsureAiEnabled();
        if (string.IsNullOrEmpty(req.SessionId)) return BadRequest("sessionId required");
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (userId == null || storeId == null)
            return Unauthorized();
        var stopped = _liveTurns.Stop(req.SessionId, storeId.Value, userId.Value);
        return Ok(new { stopped });
    }

    // POST /api/ai/explain-error
    [HttpPost("explain-error")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> ExplainError([FromBody] ExplainErrorRequest request, CancellationToken ct)
    {
        EnsureAiEnabled();
        var explanation = await _chatService.ExplainErrorAsync(
            request.ErrorMessage,
            request.Context,
            request.ProductId,
            request.ConnectionId,
            ct);
        return Ok(new { explanation });
    }

    [HttpPost("generate-product-description")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public async Task<IActionResult> GenerateProductDescription(
        [FromBody] GenerateProductDescriptionRequest request,
        CancellationToken ct)
    {
        EnsureAiEnabled();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");

        try
        {
            var description = await _chatService.GenerateProductDescriptionAsync(
                storeId.Value,
                request.Name.Trim(),
                request.Sku,
                request.CategoryName,
                request.AttributesSummary,
                request.ExistingDescription,
                request.ModelId,
                ct);

            return Ok(new { description });
        }
        catch (InvalidOperationException ex)
        {
            throw ApiException.BadRequest(ex.Message);
        }
    }

    // GET /api/ai/operations - recent AI operations for undo history
    [HttpGet("operations")]
    public async Task<IActionResult> GetOperations(CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (userId == null || storeId == null) return Unauthorized();
        var ops = await _operationService.GetRecentAsync(userId.Value, storeId.Value, 10, ct);
        return Ok(ops.Select(o => new
        {
            o.Id,
            o.operationType,
            o.description,
            o.canRollback,
            o.status,
            o.createdAt
        }));
    }

    private void PrepareSseHeaders()
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";
    }

    private async Task WriteSseAsync(IAsyncEnumerable<string> chunks, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in chunks.WithCancellation(ct))
            {
                var bytes = Encoding.UTF8.GetBytes(chunk);
                await Response.Body.WriteAsync(bytes, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Subscriber left; the hub turn keeps running.
        }
    }

    private static ChatMessage? LastPlainUser(List<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (AiConversationTurnStore.UserText(messages[i]) != null)
                return messages[i];
        }
        return null;
    }

    private static void EnsureAiEnabled()
    {
        if (!FeatureFlags.IsAiEnabled())
            throw ApiException.Forbidden("AI assistant is disabled for this instance", code: "ai_disabled");
    }
}

public class StopRequest
{
    public string SessionId { get; set; } = "";
}

public class AiChatRequest
{
    public List<ChatMessage> Messages { get; set; } = [];
    public int? ModelId { get; set; }
    public AiPageContext? PageContext { get; set; }
    public string? SessionId { get; set; }
}

public class ExecutePlanRequest
{
    public List<PlanStepInput> Steps { get; set; } = [];
    public string? SessionId { get; set; }
}

public class ExplainErrorRequest
{
    public string ErrorMessage { get; set; } = "";
    public string Context { get; set; } = "";
    public int? ProductId { get; set; }
    public int? ConnectionId { get; set; }
}

public class GenerateProductDescriptionRequest
{
    public string Name { get; set; } = "";
    public string? Sku { get; set; }
    public string? CategoryName { get; set; }
    public string? AttributesSummary { get; set; }
    public string? ExistingDescription { get; set; }
    public int? ModelId { get; set; }
}
