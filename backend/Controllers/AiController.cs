using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("ai")]
public class AiController : ControllerBase
{
    private readonly AiChatService _chatService;
    private readonly AiMappingService _mappingService;
    private readonly MappingCompatibilityService _compatibilityService;
    private readonly AiOperationService _operationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AiController> _logger;

    public AiController(
        AiChatService chatService,
        AiMappingService mappingService,
        MappingCompatibilityService compatibilityService,
        AiOperationService operationService,
        ICurrentUserService currentUserService,
        ILogger<AiController> logger)
    {
        _chatService = chatService;
        _mappingService = mappingService;
        _compatibilityService = compatibilityService;
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

        var storedCtx = CompatibilityContextHelper.Parse(mapping.compatibilityContextJson);
        if (CompatibilityContextHelper.HasPendingSurvey(storedCtx)
            && !CompatibilityContextHelper.AreAllQuestionsAnswered(storedCtx, request?.Answers))
        {
            return BadRequest(new { error = "Сначала завершите опрос совместимости" });
        }

        var operationId = request?.OperationId;
        if (string.IsNullOrEmpty(operationId))
        {
            // Legacy synchronous path (AI chat and older clients)
            try
            {
                var result = await RunMappingSuggestAsync(
                    HttpContext.RequestServices, mappingId, storeId.Value, null, request?.Answers, ct);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI mapping-suggest failed for mapping {Id}", mappingId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        var alreadyRunning = await context.operations.AnyAsync(
            o => o.storeId == storeId.Value
                 && o.type == "ai-mapping"
                 && o.status == "running"
                 && o.title.Contains($"(#{mappingId})"), ct);
        if (alreadyRunning)
            return Conflict(new { error = "Генерация маппинга уже выполняется" });

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
                    scope.ServiceProvider, mappingId, storeId.Value, operationId, request?.Answers, CancellationToken.None);

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
        IServiceProvider services, int mappingId, int storeId, string? operationId, Dictionary<string, string>? answers, CancellationToken ct)
    {
        var context = services.GetRequiredService<DataContext>();
        var mappingService = services.GetRequiredService<AiMappingService>();
        var graphMutation = services.GetRequiredService<GraphMutationService>();
        var operationLogger = services.GetRequiredService<Delegat.Services.IOperationLogger>();
        var adapterRegistry = services.GetRequiredService<IMarketplaceAdapterRegistry>();

        var mapping = await context.categoryMappings
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct)
            ?? throw new InvalidOperationException("Маппинг не найден");
        var localCat = await context.categories
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == mapping.localCategoryId, ct)
            ?? throw new InvalidOperationException("Локальная категория не найдена");

        var storedCtx = CompatibilityContextHelper.Parse(mapping.compatibilityContextJson);
        var mergedAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (storedCtx?.Answers != null)
        {
            foreach (var (k, v) in storedCtx.Answers)
                mergedAnswers[k] = v;
        }
        if (answers != null)
        {
            foreach (var (k, v) in answers)
                mergedAnswers[k] = v;
        }

        if (CompatibilityContextHelper.HasPendingSurvey(storedCtx)
            && !CompatibilityContextHelper.AreAllQuestionsAnswered(storedCtx, mergedAnswers))
        {
            throw new InvalidOperationException("Сначала завершите опрос совместимости");
        }

        var adapter = adapterRegistry.GetByKey(mapping.connection.adapterKey)
            ?? throw new InvalidOperationException($"Адаптер {mapping.connection.adapterKey} не найден");
        var adapterFields = await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);

        var genResult = await mappingService.GenerateSuggestionsAsync(
            mapping, localCat, adapterFields,
            operationId, operationId != null ? storeId : null,
            compatibilityReport: storedCtx?.Report,
            userAnswers: mergedAnswers.Count > 0 ? mergedAnswers : null,
            ct: ct);

        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
        {
            try
            {
                var priceFields = await adapter.GetPriceFieldsAsync(
                    mapping.connection, mapping.externalCategoryId, ct);
                if (priceFields.Count > 0)
                {
                    if (operationId != null)
                        await operationLogger.LogAsync(operationId, storeId, "info",
                            $"Ценовые поля: {priceFields.Count} — детерминированное подключение…");

                    var priceDeterministic = await graphMutation.ApplyDeterministicPriceMappingsAsync(
                        mappingId, priceFields, ct);
                    genResult.Connected += priceDeterministic.Connected;
                    genResult.AlreadyConnected += priceDeterministic.AlreadyConnected;

                    mapping = await context.categoryMappings
                        .Include(m => m.connection)
                        .FirstAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct);

                    if (operationId != null)
                        await operationLogger.LogAsync(operationId, storeId, "info",
                            "Ценовые поля: AI для оставшихся…");

                    var priceResult = await mappingService.GenerateSuggestionsAsync(
                        mapping, localCat, priceFields,
                        operationId, operationId != null ? storeId : null,
                        sink: "price",
                        compatibilityReport: null,
                        userAnswers: null,
                        ct: ct);
                    genResult.Connected += priceResult.Connected;
                    genResult.Skipped += priceResult.Skipped;
                    genResult.AlreadyConnected += priceResult.AlreadyConnected;
                    genResult.Proposed += priceResult.Proposed;
                    genResult.NoSource += priceResult.NoSource;
                    genResult.UnknownKeys.AddRange(priceResult.UnknownKeys);
                    genResult.InvalidModifiers.AddRange(priceResult.InvalidModifiers);
                }
            }
            catch (Exception ex)
            {
                if (operationId != null)
                    await operationLogger.LogAsync(operationId, storeId, "warning",
                        $"Ценовые поля: ошибка — {ex.Message}");
            }
        }

        if (answers != null && answers.Count > 0 && storedCtx != null)
        {
            try
            {
                var updated = CompatibilityContextHelper.MergeAnswers(storedCtx, answers);
                mapping.compatibilityContextJson = CompatibilityContextHelper.Serialize(updated);
                await context.SaveChangesAsync(ct);
            }
            catch { /* non critical */ }
        }

        return genResult;
    }

    public class MappingSuggestRequest
    {
        public string? OperationId { get; set; }
        // PR2 support (optional)
        public Dictionary<string, string>? Answers { get; set; }
    }

    public class MappingAnalyzeRequest
    {
        public string? OperationId { get; set; }
        /// <summary>Only for AI chat tools — UI must never send this.</summary>
        public bool Force { get; set; }
    }

    // POST /api/ai/mapping-analyze/{mappingId}
    // Phase 1 of compatibility flow. Returns CompatibilityReport + questions.
    // With operationId: returns 202 immediately; analysis runs in the background.
    // Without operationId: synchronous (wizard, chat tools).
    [HttpPost("mapping-analyze/{mappingId:int}")]
    public async Task<IActionResult> MappingAnalyzePost(
        int mappingId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] MappingAnalyzeRequest? request,
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

        var operationId = request?.OperationId;
        if (!string.IsNullOrEmpty(operationId))
        {
            var localCatName = await context.categories
                .Where(c => c.Id == mapping.localCategoryId)
                .Select(c => c.name)
                .FirstOrDefaultAsync(ct) ?? "Категория";

            var operationLogger = HttpContext.RequestServices.GetRequiredService<Delegat.Services.IOperationLogger>();
            await operationLogger.StartAsync(operationId, storeId.Value, userId.Value,
                "ai-compatibility", $"Анализ совместимости: {localCatName} → {mapping.connection.name} (#{mappingId})",
                connectionId: mapping.connectionId, connectionName: mapping.connection.name);

            var cachedCtx = CompatibilityContextHelper.Parse(mapping.compatibilityContextJson);
            if (CompatibilityContextHelper.IsAnalyzed(cachedCtx) && !request!.Force)
            {
                var summary = CompatibilityContextHelper.AnalyzeSummary(cachedCtx!);
                await operationLogger.FinishAsync(operationId, storeId.Value, "completed", summary);
                return Accepted(new { operationId, mappingId, cached = true });
            }

            _ = Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var opLogger = scope.ServiceProvider.GetRequiredService<Delegat.Services.IOperationLogger>();
                try
                {
                    var result = await RunMappingAnalyzeAsync(
                        scope.ServiceProvider, mappingId, storeId.Value, force: request!.Force, ct: CancellationToken.None);
                    var summary = result.Questions.Count > 0
                        ? $"{result.Questions.Count} вопросов совместимости"
                        : "Совместимость проверена, вопросов нет";
                    await opLogger.FinishAsync(operationId, storeId.Value, "completed", summary);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background compatibility analyze failed for mapping {Id}", mappingId);
                    await opLogger.FinishAsync(operationId, storeId.Value, "failed", ex.Message);
                }
            });

            return Accepted(new { operationId, mappingId });
        }

        try
        {
            var result = await RunMappingAnalyzeAsync(
                HttpContext.RequestServices, mappingId, storeId.Value, force: request?.Force == true, ct);
            return Ok(BuildMappingAnalyzeResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mapping-analyze failed for {Id}", mappingId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("mapping-analyze/{mappingId:int}")]
    public async Task<IActionResult> MappingAnalyzeGet(int mappingId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null) return Unauthorized();

        var context = HttpContext.RequestServices.GetRequiredService<DataContext>();
        var mapping = await context.categoryMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct);
        if (mapping == null)
            return NotFound();

        try
        {
            var result = await RunMappingAnalyzeAsync(
                HttpContext.RequestServices, mappingId, storeId.Value, force: false, ct);
            return Ok(BuildMappingAnalyzeResponse(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mapping-analyze failed for {Id}", mappingId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static object BuildMappingAnalyzeResponse(MappingCompatibilityService.CompatibilityAnalysisResult result) =>
        new
        {
            mappingId = result.MappingId,
            report = result.Report,
            questions = result.Questions,
            schemaContextHint = result.SchemaContextHint,
            summary = new
            {
                clearMatches = result.Report.Summary.ClearMatches,
                ambiguous = result.Report.Summary.Ambiguous,
                requiredGaps = result.Report.Summary.RequiredGaps,
                intentionallyEmpty = result.Report.Summary.IntentionallyLeftEmpty,
                compatibilityScore = result.Report.CompatibilityScore
            }
        };

    private static async Task<MappingCompatibilityService.CompatibilityAnalysisResult> RunMappingAnalyzeAsync(
        IServiceProvider services, int mappingId, int storeId, bool force, CancellationToken ct)
    {
        var context = services.GetRequiredService<DataContext>();
        var compatibilityService = services.GetRequiredService<MappingCompatibilityService>();

        var mapping = await context.categoryMappings
            .AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct)
            ?? throw new InvalidOperationException("Маппинг не найден");

        var storedCtx = CompatibilityContextHelper.Parse(mapping.compatibilityContextJson);
        if (!force && CompatibilityContextHelper.IsAnalyzed(storedCtx))
            return CompatibilityContextHelper.ToAnalyzeResult(mappingId, storedCtx!);

        var result = await compatibilityService.AnalyzeAsync(mappingId, null, ct);

        var newCtx = CompatibilityContextHelper.FromAnalyzeResult(result);
        if (storedCtx?.Answers.Count > 0)
        {
            newCtx.Answers = new Dictionary<string, string>(storedCtx.Answers, StringComparer.OrdinalIgnoreCase);
            if (!CompatibilityContextHelper.HasPendingSurvey(newCtx))
                newCtx.SurveyCompletedAt = storedCtx.SurveyCompletedAt ?? DateTime.UtcNow;
        }

        var tracked = await context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.connection.storeId == storeId, ct);
        if (tracked != null)
        {
            tracked.compatibilityContextJson = CompatibilityContextHelper.Serialize(newCtx);
            await context.SaveChangesAsync(ct);
        }

        if (CompatibilityContextHelper.HasPendingSurvey(newCtx))
            result.Questions = CompatibilityContextHelper.PendingQuestions(newCtx).ToList();
        else
            result.Questions.Clear();

        return result;
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
