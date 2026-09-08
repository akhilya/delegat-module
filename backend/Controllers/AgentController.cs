using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Services.Ai.Agent;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Controllers;

[ApiController]
[AgentAuthorize]
[Route("agent")]
public class AgentController : ControllerBase
{
    private readonly AgentSessionResolver _session;
    private readonly AgentToolHost _tools;
    private readonly AgentFileUploadService _uploads;
    private readonly KnowledgeIndexService _knowledge;
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AgentController(
        AgentSessionResolver session,
        AgentToolHost tools,
        AgentFileUploadService uploads,
        KnowledgeIndexService knowledge)
    {
        _session = session;
        _tools = tools;
        _uploads = uploads;
        _knowledge = knowledge;
    }

    [HttpPut("stores/{storeId:int}/products/{productId:int}/files")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int storeId, int productId, [FromForm] IFormFile[] files, [FromQuery] int? folderId, CancellationToken ct)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        if (!AgentAccess.CanMutate(s!.Permissions, Permissions.FilesManage))
            return StatusCode(403, new { error = new { code = "forbidden", message = "files.manage required" } });
        try
        {
            var saved = await _uploads.UploadProductFilesAsync(s.StoreId, s.UserId, productId, files ?? [], folderId, ct);
            return Ok(new { items = saved });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = new { code = ex.Message, message = ex.Message } });
        }
    }

    [HttpPost("stores/{storeId:int}/catalog/search")]
    public async Task<IActionResult> CatalogSearch(int storeId, [FromBody] AgentQueryBody body, CancellationToken ct)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var (result, _) = await _tools.DispatchAsync("search_catalog", JsonSerializer.Serialize(body, Camel), s!.StoreId, s.UserId, ct);
        return Ok(result);
    }

    [HttpPost("stores/{storeId:int}/sql")]
    public async Task<IActionResult> Sql(int storeId, [FromBody] AgentSqlBody body, CancellationToken ct)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var (result, _) = await _tools.DispatchAsync("sql_readonly", JsonSerializer.Serialize(body, Camel), s!.StoreId, s.UserId, ct);
        return Ok(result);
    }

    [HttpPost("stores/{storeId:int}/python")]
    public async Task<IActionResult> Python(int storeId, [FromBody] AgentPythonBody body, CancellationToken ct)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var (result, _) = await _tools.DispatchAsync("run_python", JsonSerializer.Serialize(body, Camel), s!.StoreId, s.UserId, ct);
        return Ok(result);
    }

    [HttpPost("stores/{storeId:int}/platform")]
    public async Task<IActionResult> Platform(int storeId, [FromBody] AgentPlatformBody body, CancellationToken ct)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var json = JsonSerializer.Serialize(body, Camel);
        var (result, _) = await _tools.DispatchAsync("platform_request", json, s!.StoreId, s.UserId, ct);
        return Ok(result);
    }

    [HttpPost("stores/{storeId:int}/files/match")]
    public async Task<IActionResult> Match(int storeId, [FromBody] AgentMatchBody body, CancellationToken ct)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var (result, _) = await _tools.DispatchAsync("match_files", JsonSerializer.Serialize(body, Camel), s!.StoreId, s.UserId, ct);
        return Ok(result);
    }

    [HttpGet("stores/{storeId:int}/knowledge")]
    public async Task<IActionResult> Knowledge(int storeId, [FromQuery] string q, [FromQuery] int limit = 8, CancellationToken ct = default)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var hits = await _knowledge.SearchAsync(q ?? "", limit, ct);
        return Ok(new { items = hits });
    }

    [HttpGet("stores/{storeId:int}/openapi")]
    public async Task<IActionResult> OpenApi(int storeId, CancellationToken ct)
    {
        EnsureAi();
        var (_, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        return Ok(PlatformRequestPolicy.OpenApiSurface());
    }

    private async Task<(AgentSession? Session, IActionResult? Error)> TryResolve(int storeId, CancellationToken ct)
    {
        try
        {
            return (await _session.ResolveAsync(storeId, ct), null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (null, StatusCode(403, new { error = new { code = ex.Message, message = ex.Message } }));
        }
        catch (InvalidOperationException ex)
        {
            return (null, BadRequest(new { error = new { code = ex.Message, message = ex.Message } }));
        }
    }

    private static void EnsureAi()
    {
        if (!FeatureFlags.IsAiEnabled())
            throw ApiException.Forbidden("AI assistant is disabled for this instance", code: "ai_disabled");
    }
}

public class AgentQueryBody
{
    public string? Query { get; set; }
    public int? Limit { get; set; }
}

public class AgentSqlBody
{
    public string Sql { get; set; } = "";
}

public class AgentPythonBody
{
    public string? Code { get; set; }
    public string? Script { get; set; }
}

public class AgentPlatformBody
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "";
    public JsonElement? Body { get; set; }
}

public class AgentMatchBody
{
    public List<AgentMatchFile> Files { get; set; } = [];
}

public class AgentMatchFile
{
    public string RelativePath { get; set; } = "";
    public string? FileName { get; set; }
}
