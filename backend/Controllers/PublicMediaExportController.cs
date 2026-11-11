using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Infrastructure.Storage;

namespace Delegat.Controllers;

/// <summary>Unauthenticated fetch endpoint for explicitly granted marketplace video URLs.</summary>
[ApiController]
public sealed class PublicMediaExportController : ControllerBase
{
    private readonly DataContext _db;
    private readonly IStorageService _storage;

    public PublicMediaExportController(DataContext db, IStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpGet]
    [HttpHead]
    [Route("/pub/media-export/{token}")]
    [Route("/pub/media-export/{token}/{fileName}")]
    public async Task Get(string token, CancellationToken ct, string? fileName = null)
    {
        if (!IsToken(token))
            throw ApiException.NotFound(code: "file_not_found");

        var grant = await _db.productMediaExportGrants.AsNoTracking()
            .Where(g => g.token == token && g.revokedAt == null)
            .Select(g => new
            {
                g.file.filePath,
                g.file.displayName,
                g.file.contentType,
                g.file.length
            })
            .FirstOrDefaultAsync(ct);

        if (grant == null
            || string.IsNullOrWhiteSpace(grant.contentType)
            || !grant.contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || !await _storage.ExistsAsync(grant.filePath, ct))
        {
            throw ApiException.NotFound(code: "file_not_found");
        }

        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        await StorageDownloadHttp.StreamAsync(
            Request,
            Response,
            _storage,
            grant.filePath,
            grant.contentType,
            grant.displayName ?? Path.GetFileName(grant.filePath),
            inline: true,
            grant.length,
            ct);
    }

    private static bool IsToken(string? token)
        => token is { Length: 64 }
            && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
