using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Services.Ai.Agent;
using Delegat.Services.PlatformLink;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    private readonly IPlatformLinkClient _platform;
    private readonly MarketplaceCredentialBroker _marketplaceBroker;
    private readonly AgentFileDownloadService _fileDownloads;
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AgentController(
        AgentSessionResolver session,
        AgentToolHost tools,
        AgentFileUploadService uploads,
        KnowledgeIndexService knowledge,
        IPlatformLinkClient platform,
        MarketplaceCredentialBroker marketplaceBroker,
        AgentFileDownloadService fileDownloads)
    {
        _session = session;
        _tools = tools;
        _uploads = uploads;
        _knowledge = knowledge;
        _platform = platform;
        _marketplaceBroker = marketplaceBroker;
        _fileDownloads = fileDownloads;
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

    [HttpGet("stores/{storeId:int}/knowledge")]
    public async Task<IActionResult> Knowledge(int storeId, [FromQuery] string q, [FromQuery] int limit = 8, CancellationToken ct = default)
    {
        EnsureAi();
        var (s, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        var hits = await _knowledge.SearchAsync(q ?? "", limit, ct);
        return Ok(new { items = hits });
    }

    /// <summary>
    /// Streams a private product file using a short-lived, user/store-bound download ticket.
    /// The ticket is resumable and does not change the file's public visibility.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("files/{fileId:int}/download")]
    public async Task<IActionResult> DownloadFile(
        int fileId,
        [FromQuery] string? ticket,
        CancellationToken ct)
    {
        EnsureAi();
        if (!_fileDownloads.TryValidateTicket(ticket, DateTimeOffset.UtcNow, out var grant)
            || grant.FileId != fileId)
            return NotFound();

        AgentSession session;
        try
        {
            session = await _session.ResolveForUserAsync(grant.UserId, grant.StoreId, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }

        var file = await _fileDownloads.GetFileAsync(session.StoreId, fileId, ct);
        if (file == null || !await _fileDownloads.ExistsInStorageAsync(file, ct))
            return NotFound();

        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers["Accept-Ranges"] = "bytes";

        if (!FileByteRangeParser.TryParse(Request.Headers["Range"].ToString(), file.SizeBytes, out var range))
        {
            Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            Response.Headers["Content-Range"] = $"bytes */{file.SizeBytes}";
            return new EmptyResult();
        }

        StorageDownloadHttp.Prepare(Response, file.ContentType, file.FileName, inline: false, contentLength: file.SizeBytes);
        if (range is null)
        {
            await _fileDownloads.StreamAsync(file, Response.Body, ct);
            return new EmptyResult();
        }

        Response.StatusCode = StatusCodes.Status206PartialContent;
        Response.Headers["Content-Range"] = $"bytes {range.Value.Start}-{range.Value.End}/{file.SizeBytes}";
        Response.ContentLength = range.Value.Length;
        await _fileDownloads.StreamRangeAsync(file, range.Value.Start, range.Value.Length, Response.Body, ct);
        return new EmptyResult();
    }

    [HttpGet("stores/{storeId:int}/marketplace-docs/{adapter}")]
    public async Task<IActionResult> MarketplaceDoc(
        int storeId, string adapter, [FromQuery] string? q, CancellationToken ct)
    {
        EnsureAi();
        var (_, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        if (!_platform.IsEnabled)
            return Ok(new { error = "platform_link_off" });

        var doc = await _platform.GetMarketplaceDocAsync(adapter, q, ct);
        if (doc is null)
            return Ok(new { error = "not_ready", adapter });
        return Ok(doc);
    }

    /// <summary>
    /// Read-only GET broker endpoint used by selected sandbox stores and available to
    /// authenticated agent sessions. Credentials remain server-side.
    /// </summary>
    [HttpPost("stores/{storeId:int}/marketplace/{connectionId:int}/read")]
    [EnableRateLimiting("marketplace-broker")]
    public async Task<IActionResult> MarketplaceRead(
        int storeId,
        int connectionId,
        [FromBody] AgentMarketplaceReadBody body,
        CancellationToken ct)
    {
        EnsureAi();
        var (session, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        if (!AgentAccess.CanRunPython(session!.Permissions))
            return StatusCode(403, new { error = "ai.use required" });

        var result = await _marketplaceBroker.ReadAsync(
            session.StoreId,
            session.UserId,
            connectionId,
            body.Host,
            body.Path,
            ct);
        if (result.Ok) return Ok(result);

        return result.Error switch
        {
            "connection_not_found" => NotFound(new { error = result.Error }),
            "connection_inactive" or "credentials_missing" => Conflict(new { error = result.Error }),
            "upstream_unavailable" => StatusCode(502, new { error = result.Error }),
            "upstream_timeout" => StatusCode(504, new { error = result.Error }),
            _ => BadRequest(new { error = result.Error }),
        };
    }

    /// <summary>Execute a named adapter-owned read operation, including upstream POST reads.</summary>
    [HttpPost("stores/{storeId:int}/marketplace/{connectionId:int}/read-operation")]
    [RequestSizeLimit(32_768)]
    [EnableRateLimiting("marketplace-broker")]
    public async Task<IActionResult> MarketplaceReadOperation(
        int storeId,
        int connectionId,
        [FromBody] AgentMarketplaceReadOperationBody body,
        CancellationToken ct)
    {
        EnsureAi();
        var (session, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        if (!AgentAccess.CanRunPython(session!.Permissions))
            return StatusCode(403, new { error = "ai.use required" });

        var result = await _marketplaceBroker.ReadOperationAsync(
            session.StoreId,
            session.UserId,
            connectionId,
            body.Operation,
            body.Arguments,
            ct);
        if (result.Ok) return Ok(result);

        return result.Error switch
        {
            "connection_not_found" => NotFound(new { error = result.Error }),
            "connection_inactive" or "credentials_missing" => Conflict(new { error = result.Error }),
            "upstream_unavailable" => StatusCode(502, new { error = result.Error }),
            "upstream_timeout" => StatusCode(504, new { error = result.Error }),
            _ => BadRequest(new { error = result.Error }),
        };
    }

    /// <summary>List the named read operations exposed by the selected Marketplace adapter.</summary>
    [HttpGet("stores/{storeId:int}/marketplace/{connectionId:int}/read-operations")]
    [EnableRateLimiting("marketplace-broker")]
    public async Task<IActionResult> MarketplaceReadOperations(
        int storeId, int connectionId, CancellationToken ct)
    {
        EnsureAi();
        var (session, err) = await TryResolve(storeId, ct);
        if (err != null) return err;
        if (!AgentAccess.CanRunPython(session!.Permissions))
            return StatusCode(403, new { error = "ai.use required" });

        var result = await _marketplaceBroker.GetReadOperationsAsync(session.StoreId, connectionId, ct);
        if (result.Ok) return Ok(new { operations = result.Operations });

        return result.Error switch
        {
            "connection_not_found" => NotFound(new { error = result.Error }),
            "connection_inactive" => Conflict(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error }),
        };
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

public sealed class AgentMarketplaceReadBody
{
    public string? Host { get; set; }
    public string? Path { get; set; }
}

public sealed class AgentMarketplaceReadOperationBody
{
    public string? Operation { get; set; }
    public JsonElement Arguments { get; set; }
}
