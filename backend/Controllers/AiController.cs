using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Delegat.Infrastructure.Auth;
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
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequest request, CancellationToken ct)
    {
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

    // POST /api/ai/mapping-suggest/{mappingId}
    // With operationId: validates and returns 202 immediately; the AI mapping runs in the
    // background (survives page reloads), progress streams to the operations panel via SignalR.
    // Without operationId: legacy synchronous mode (returns the result).
    [HttpPost("mapping-suggest/{mappingId:int}")]
    public async Task<IActionResult> MappingSuggest(
        int mappingId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] MappingSuggestRequest? request,
        [FromServices] IServiceScopeFactory scopeFactory,
        CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        var userId = _currentUserService.GetUserId();
        if (storeId == null || userId == null) return Unauthorized();

        var context = HttpContext.RequestServices.GetRequiredService<DataContext>();
        var mapping = await context.categoryMappings
            .AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct);
        if (mapping == null)
            return NotFound();

        var localCatName = await context.categories
            .Where(c => c.Id == mapping.localCategoryId)
            .Select(c => c.name)
            .FirstOrDefaultAsync(ct);
        if (localCatName == null)
            return NotFound("Локальная категория не найдена");

        var operationId = request?.OperationId;
        if (string.IsNullOrEmpty(operationId))
        {
            // Legacy synchronous path (AI chat and older clients)
            try
            {
                var result = await RunMappingSuggestAsync(
                    HttpContext.RequestServices, mappingId, storeId.Value, null, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI mapping-suggest failed for mapping {Id}", mappingId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        var operationLogger = HttpContext.RequestServices.GetRequiredService<Delegat.Services.IOperationLogger>();
        await operationLogger.StartAsync(operationId, storeId.Value, userId.Value,
            "ai-mapping", $"AI-маппинг: {localCatName} → {mapping.connection.name} (#{mappingId})",
            connectionId: mapping.connectionId, connectionName: mapping.connection.name);

        // Detached from the request: a page reload must not cancel the run
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var opLogger = scope.ServiceProvider.GetRequiredService<Delegat.Services.IOperationLogger>();
            try
            {
                var result = await RunMappingSuggestAsync(
                    scope.ServiceProvider, mappingId, storeId.Value, operationId, CancellationToken.None);

                var summary = $"Подключено полей: {result.Connected}"
                    + (result.Skipped > 0 ? $", пропущено: {result.Skipped}" : "")
                    + (result.AlreadyConnected > 0 ? $", уже было: {result.AlreadyConnected}" : "");
                var status = result.Connected > 0
                    ? "completed"
                    : result.Skipped > 0 ? "failed" : "completed";
                await opLogger.FinishAsync(operationId, storeId.Value, status, summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background AI mapping failed for mapping {Id}", mappingId);
                await opLogger.FinishAsync(operationId, storeId.Value, "failed", ex.Message);
            }
        });

        return Accepted(new { operationId, mappingId });
    }

    /// <summary>Loads mapping context inside the given scope and runs the batched AI fill.</summary>
    private static async Task<AiMappingResult> RunMappingSuggestAsync(
        IServiceProvider services, int mappingId, int storeId, string? operationId, CancellationToken ct)
    {
        var context = services.GetRequiredService<DataContext>();
        var mappingService = services.GetRequiredService<AiMappingService>();
        var adapterRegistry = services.GetRequiredService<Delegat.Services.Marketplace.IMarketplaceAdapterRegistry>();

        var mapping = await context.categoryMappings
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct)
            ?? throw new InvalidOperationException("Маппинг не найден");
        var localCat = await context.categories
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == mapping.localCategoryId, ct)
            ?? throw new InvalidOperationException("Локальная категория не найдена");

        var adapter = adapterRegistry.GetByKey(mapping.connection.adapterKey)
            ?? throw new InvalidOperationException($"Адаптер {mapping.connection.adapterKey} не найден");
        var adapterFields = await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);

        return await mappingService.GenerateSuggestionsAsync(
            mapping, localCat, adapterFields, operationId, operationId != null ? storeId : null, ct);
    }

    public class MappingSuggestRequest
    {
        public string? OperationId { get; set; }
    }

    // POST /api/ai/stop — cancel server-side generation for the given session
    [HttpPost("stop")]
    public IActionResult Stop([FromBody] StopRequest req)
    {
        if (string.IsNullOrEmpty(req.SessionId)) return BadRequest("sessionId required");
        var stopped = AiChatService.StopSession(req.SessionId);
        return Ok(new { stopped });
    }

    // POST /api/ai/explain-error
    [HttpPost("explain-error")]
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
    [HttpGet("operations")]
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
