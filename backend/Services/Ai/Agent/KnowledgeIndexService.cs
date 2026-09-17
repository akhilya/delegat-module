using Delegat.Infrastructure.Storage;
using Delegat.Models.Ai;
using Delegat.Services.Docs;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

public sealed class KnowledgeIndexService
{
    public const string MinioPrefix = "__ai-knowledge/";

    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly ILogger<KnowledgeIndexService> _logger;
    private readonly IWebHostEnvironment? _env;
    private readonly IPublicDocsClient? _publicDocs;

    public KnowledgeIndexService(
        DataContext db,
        IStorageService storage,
        ILogger<KnowledgeIndexService> logger,
        IWebHostEnvironment? env = null,
        IPublicDocsClient? publicDocs = null)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
        _env = env;
        _publicDocs = publicDocs;
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(string query, int take, CancellationToken ct)
    {
        var stored = await _db.aiKnowledgeChunks.AsNoTracking()
            .Where(c => c.storeId == null)
            .ToListAsync(ct);
        var chunks = stored.Select(c => new KnowledgeChunk(c.sourcePath, c.title, c.body)).ToList();
        if (chunks.Count == 0)
            chunks = LoadFromDisk().ToList();
        return KnowledgeSearch.Search(chunks, query, take);
    }

    public async Task<KnowledgeChunk?> ReadSourceAsync(string path, string locale, CancellationToken ct)
    {
        var keys = SourceKeys(path, locale);
        var stored = await _db.aiKnowledgeChunks.AsNoTracking()
            .Where(c => c.storeId == null)
            .ToListAsync(ct);
        var hits = stored
            .Where(c => keys.Any(k => PathsEqual(c.sourcePath, k)))
            .OrderBy(c => c.Id)
            .Select(c => new KnowledgeChunk(c.sourcePath, c.title, c.body))
            .ToList();
        if (hits.Count == 0)
        {
            hits = LoadFromDisk()
                .Where(c => keys.Any(k => PathsEqual(c.SourcePath, k)))
                .ToList();
        }

        if (hits.Count == 0)
            return null;

        var body = string.Join("\n\n", hits.Select(h => h.Body).Where(b => !string.IsNullOrWhiteSpace(b)));
        if (body.Length > 32_000)
            body = body[..32_000];
        return new KnowledgeChunk(hits[0].SourcePath, hits[0].Title, body);
    }

    public static bool IsInstanceDocPath(string path)
    {
        var p = (path ?? "").Replace('\\', '/').TrimStart('/');
        return p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith("knowledge/", StringComparison.OrdinalIgnoreCase)
               || p.Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase)
               || p.Equals("PRODUCT.md", StringComparison.OrdinalIgnoreCase)
               || p.Equals("DESIGN.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string stored, string key) =>
        stored.Replace('\\', '/').Equals(key.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SourceKeys(string path, string locale)
    {
        var p = (path ?? "").Replace('\\', '/').TrimStart('/');
        var keys = new List<string> { p };
        if (!p.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !p.Contains('/'))
            keys.Add(p + ".md");
        if (p.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            keys.Add(p + ".md");
        var loc = string.IsNullOrWhiteSpace(locale) ? "ru" : locale;
        keys.Add($"product-docs/{loc}/{p}.md");
        keys.Add($"product-docs/{loc}/{p}");
        return keys;
    }

    public async Task ReindexAsync(CancellationToken ct = default)
    {
        var files = EnumerateCanonFiles().ToList();
        var rows = new List<AiKnowledgeChunk>();
        foreach (var file in files)
        {
            string text;
            try { text = await System.IO.File.ReadAllTextAsync(file.FullPath, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skip knowledge file {Path}", file.RelPath);
                continue;
            }

            foreach (var chunk in KnowledgeChunker.Chunk(file.RelPath, text))
            {
                rows.Add(new AiKnowledgeChunk
                {
                    sourcePath = chunk.SourcePath,
                    title = chunk.Title.Length > 200 ? chunk.Title[..200] : chunk.Title,
                    body = chunk.Body,
                    storeId = null,
                    createdAt = DateTime.UtcNow,
                });
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(file.FullPath);
                await _storage.UploadAsync(stream, MinioPrefix + file.RelPath.Replace('\\', '/'), stream.Length, "text/markdown", ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Knowledge MinIO copy skipped for {Path}", file.RelPath);
            }
        }

        await AppendProductDocsAsync(rows, ct);

        if (rows.Count == 0)
        {
            _logger.LogInformation("Knowledge index: no canon files found");
            return;
        }

        var existing = _db.aiKnowledgeChunks.Where(c => c.storeId == null);
        _db.aiKnowledgeChunks.RemoveRange(existing);
        _db.aiKnowledgeChunks.AddRange(rows);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Knowledge index: {Count} chunks from {Files} files", rows.Count, files.Count);
    }

    public IReadOnlyList<KnowledgeChunk> LoadFromDisk()
    {
        var chunks = new List<KnowledgeChunk>();
        foreach (var file in EnumerateCanonFiles())
        {
            try
            {
                var text = System.IO.File.ReadAllText(file.FullPath);
                chunks.AddRange(KnowledgeChunker.Chunk(file.RelPath, text));
            }
            catch { /* skip unreadable */ }
        }

        return chunks;
    }

    public IEnumerable<(string RelPath, string FullPath)> EnumerateCanonFiles()
    {
        foreach (var root in CandidateRoots())
        {
            var yielded = false;
            var knowledge = Path.Combine(root, "knowledge");
            if (Directory.Exists(knowledge))
            {
                foreach (var path in Directory.EnumerateFiles(knowledge, "*.*", SearchOption.AllDirectories))
                {
                    if (!IsCanon(path)) continue;
                    yield return (Path.GetRelativePath(knowledge, path).Replace('\\', '/'), path);
                    yielded = true;
                }
            }

            var docs = Path.Combine(root, "docs");
            var hasCanon = Directory.Exists(docs) || System.IO.File.Exists(Path.Combine(root, "MEMORY.md"));
            if (hasCanon)
            {
                foreach (var name in new[] { "MEMORY.md", "PRODUCT.md", "DESIGN.md" })
                {
                    var full = Path.Combine(root, name);
                    if (System.IO.File.Exists(full))
                        yield return (name, full);
                }

                if (Directory.Exists(docs))
                {
                    foreach (var path in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
                    {
                        var rel = "docs/" + Path.GetRelativePath(docs, path).Replace('\\', '/');
                        if (IsOperatorPlan(rel)) continue;
                        yield return (rel, path);
                    }
                }

                yield break;
            }

            if (yielded)
                yield break;
        }
    }

    private IEnumerable<string> CandidateRoots()
    {
        if (_env != null)
        {
            yield return _env.ContentRootPath;
            yield return Path.GetFullPath(Path.Combine(_env.ContentRootPath, ".."));
        }

        yield return AppContext.BaseDirectory;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }

    private static bool IsCanon(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOperatorPlan(string relPath) =>
        relPath.Replace('\\', '/').StartsWith("docs/plans/", StringComparison.OrdinalIgnoreCase);

    private async Task AppendProductDocsAsync(List<AiKnowledgeChunk> rows, CancellationToken ct)
    {
        if (_publicDocs == null) return;

        foreach (var locale in new[] { "ru", "en" })
        {
            DocsCatalog catalog;
            try
            {
                catalog = await _publicDocs.GetCatalogAsync(locale, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Product docs catalog {Locale} skipped", locale);
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in catalog.Groups)
            {
                foreach (var item in group.Items)
                {
                    var slug = item.Slug ?? "";
                    if (!seen.Add(slug)) continue;

                    DocsPage page;
                    try
                    {
                        page = await _publicDocs.GetPageAsync(slug, locale, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Skip product doc {Locale} {Slug}", locale, slug);
                        continue;
                    }

                    var rel = string.IsNullOrEmpty(slug)
                        ? $"product-docs/{locale}/index.md"
                        : $"product-docs/{locale}/{slug}.md";
                    foreach (var chunk in KnowledgeChunker.Chunk(rel, page.Markdown))
                    {
                        rows.Add(new AiKnowledgeChunk
                        {
                            sourcePath = chunk.SourcePath,
                            title = chunk.Title.Length > 200 ? chunk.Title[..200] : chunk.Title,
                            body = chunk.Body,
                            storeId = null,
                            createdAt = DateTime.UtcNow,
                        });
                    }
                }
            }
        }
    }
}

public sealed class KnowledgeIndexHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<KnowledgeIndexHostedService> _logger;

    public KnowledgeIndexHostedService(IServiceScopeFactory scopes, ILogger<KnowledgeIndexHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var index = scope.ServiceProvider.GetRequiredService<KnowledgeIndexService>();
            await index.ReindexAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge reindex on boot failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
