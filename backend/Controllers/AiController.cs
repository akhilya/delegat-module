using Microsoft.AspNetCore.Mvc;
using System.Text;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("ai")]
public class AiController : ControllerBase
{
    private readonly AiChatService _chatService;
    private readonly AiOperationService _operationService;
    private readonly ICurrentUserService _currentUserService;

    public AiController(
        AiChatService chatService,
        AiOperationService operationService,
        ICurrentUserService currentUserService)
    {
        _chatService = chatService;
        _operationService = operationService;
        _currentUserService = currentUserService;
    }

    // POST /api/ai/chat — SSE streaming chat
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);

        if (userId == null || storeId == null)
            return Unauthorized();

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var chunk in _chatService.StreamChatAsync(
            request.Messages, storeId.Value, userId.Value, request.ModelId, request.PageContext, request.SessionId, ct))
        {
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }

        return new EmptyResult();
    }

    // POST /api/ai/execute-plan — deterministic execution of a user-confirmed plan (SSE)
    [HttpPost("execute-plan")]
    public async Task<IActionResult> ExecutePlan([FromBody] ExecutePlanRequest request, CancellationToken ct)
    {
        EnsureAiEnabled();
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);

        if (userId == null || storeId == null)
            return Unauthorized();
        if (request.Steps.Count == 0)
            return BadRequest("steps are required");

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var chunk in _chatService.ExecutePlanStreamAsync(request.Steps, storeId.Value, userId.Value, ct))
        {
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }

        return new EmptyResult();
    }

    // POST /api/ai/stop — cancel server-side generation for the given session
    [HttpPost("stop")]
    public IActionResult Stop([FromBody] StopRequest req)
    {
        EnsureAiEnabled();
        if (string.IsNullOrEmpty(req.SessionId)) return BadRequest("sessionId required");
        var stopped = AiChatService.StopSession(req.SessionId);
        return Ok(new { stopped });
    }

    // POST /api/ai/explain-error
    [HttpPost("explain-error")]
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

    // GET /api/ai/operations — recent AI operations for undo history
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
