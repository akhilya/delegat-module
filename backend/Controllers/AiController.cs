using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services.Ai;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class AiController : ControllerBase
{
    private readonly AiChatService _chatService;
    private readonly AiMappingService _mappingService;
    private readonly AiOperationService _operationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AiController> _logger;

    public AiController(
        AiChatService chatService,
        AiMappingService mappingService,
        AiOperationService operationService,
        ICurrentUserService currentUserService,
        ILogger<AiController> logger)
    {
        _chatService = chatService;
        _mappingService = mappingService;
        _operationService = operationService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    // POST /api/ai/chat — SSE streaming chat
    [HttpPost("api/ai/chat")]
    public async Task Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId();
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);

        if (userId == null || storeId == null)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var chunk in _chatService.StreamChatAsync(
            request.Messages, storeId.Value, userId.Value, request.ModelId, ct))
        {
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await Response.Body.WriteAsync(bytes, ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    // POST /api/ai/mapping-suggest/{mappingId}
    [HttpPost("api/ai/mapping-suggest/{mappingId:int}")]
    public async Task<IActionResult> MappingSuggest(int mappingId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId = _currentUserService.GetUserId();

        // Validate the mapping belongs to this store (via the service)
        // AiMappingService will handle loading
        try
        {
            var context = HttpContext.RequestServices.GetRequiredService<DataContext>();
            var mapping = await context.categoryMappings
                .Include(m => m.connection)
                .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct);

            if (mapping == null)
                return NotFound();

            var adapterRegistry = HttpContext.RequestServices
                .GetRequiredService<Delegat.Services.Marketplace.IMarketplaceAdapterRegistry>();
            var localCat = await context.categories
                .Include(c => c.attributes)
                .FirstOrDefaultAsync(c => c.Id == mapping.localCategoryId, ct);

            if (localCat == null)
                return NotFound("Local category not found");

            var adapter = adapterRegistry.GetByKey(mapping.connection.adapterKey)!;
            var adapterFields = await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);

            var result = await _mappingService.GenerateSuggestionsAsync(mapping, localCat, adapterFields, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI mapping-suggest failed for mapping {Id}", mappingId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // POST /api/ai/explain-error
    [HttpPost("api/ai/explain-error")]
    public async Task<IActionResult> ExplainError([FromBody] ExplainErrorRequest request, CancellationToken ct)
    {
        var explanation = await _chatService.ExplainErrorAsync(
            request.ErrorMessage,
            request.Context,
            request.ProductId,
            request.ConnectionId,
            ct);
        return Ok(new { explanation });
    }

    // GET /api/ai/operations — recent AI operations for undo history
    [HttpGet("api/ai/operations")]
    public async Task<IActionResult> GetOperations(CancellationToken ct)
    {
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
}

public class AiChatRequest
{
    public List<ChatMessage> Messages { get; set; } = [];
    public int? ModelId { get; set; }
}

public class ExplainErrorRequest
{
    public string ErrorMessage { get; set; } = "";
    public string Context { get; set; } = "";
    public int? ProductId { get; set; }
    public int? ConnectionId { get; set; }
}
