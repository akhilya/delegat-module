# Security Audit Report — Delegat ASP.NET Core Backend

> **Historical security review snapshot — 2026-07-17.** Findings and code excerpts have not been revalidated against the current implementation. Do not treat them as confirmed vulnerabilities or mitigations; reproduce each claim in current code before acting on it.

**Date:** 2026-07-17  
**Scope:** File upload/download, shared file access, plugin upload, storage, Excel import  
**Severity scale:** CRITICAL / HIGH / MEDIUM / LOW / INFO

---

## Finding 1 — CRITICAL: Plugin Upload Enables Remote Code Execution (RCE)

### Files
- `backend/Controllers/PluginController.cs` (lines 84–154)
- `backend/Services/Plugins/PluginLoader.cs` (lines 16–68)
- `backend/Services/Plugins/PluginLoadContext.cs` (lines 1–71)

### Vulnerable Code

```csharp
// PluginController.cs:84-93
[HttpPost("upload")]
[RequestSizeLimit(50 * 1024 * 1024)]
public async Task<IActionResult> UploadPlugin(IFormFile file)
{
    if (file == null || file.Length == 0)
        throw ApiException.BadRequest("Файл не предоставлен");

    if (!file.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        throw ApiException.BadRequest("Разрешены только файлы .dll");
```

```csharp
// PluginLoader.cs:28-45
var assembly = loadContext.LoadFromFileWithoutLock(pluginPath);
var pluginType = GetLoadableTypes(assembly)
    .FirstOrDefault(IsMarketplacePluginType);
// ...
var plugin = (IMarketplacePlugin?)Activator.CreateInstance(pluginType);
```

### Issue Details

**1a. No admin-only authorization on the plugin upload endpoint (CRITICAL)**

The `PluginController` is decorated with `[JwtAuthorize]` only, which merely requires a valid JWT token — *any* authenticated user can upload plugins. Other controllers in the codebase enforce admin-only access explicitly (e.g., `if (currentUser == null || !currentUser.isAdmin) throw ApiException.Forbidden(...)`), but the plugin controller does not perform this check on any of its endpoints: `upload`, `delete`, `reload`, or list.

**1b. Arbitrary code execution via DLL loading (CRITICAL)**

When a `.dll` file is uploaded, it is immediately loaded via `Assembly.LoadFromStream()` and `Activator.CreateInstance()` is called on a type within it. A malicious DLL can execute arbitrary code in the constructor of a class implementing `IMarketplacePlugin`, or in a static constructor, or a module initializer. This grants full RCE with the same permissions as the application process.

**1c. No DLL content validation (HIGH)**

The only file-type check is that the filename ends with `.dll`. There is no:
- Code-signing / signature verification
- Allowlist of known plugin assemblies
- Hash verification
- Analysis of the assembly's contents or capabilities

**1d. No sandboxing (HIGH)**

`PluginLoadContext` uses `AssemblyLoadContext(isCollectible: true)` for isolation, but this is *assembly-loading isolation* only — not a security sandbox. The loaded plugin code runs with full trust within the application's process. It can:
- Access the filesystem
- Open network connections
- Read environment variables and secrets
- Access the DI container
- Modify the database

**1e. Path traversal in plugin filename (MEDIUM)**

```csharp
// PluginController.cs:99
var destPath = Path.Combine(pluginsDir, file.FileName);
```

`file.FileName` is user-controlled. A filename like `../../../etc/cron.d/backdoor.dll` would resolve outside the plugins directory. While ASP.NET Core's `IFormFile.FileName` includes the original filename from the `Content-Disposition` header, `Path.Combine` with relative path segments can escape the intended directory.

### Attack Path
1. Any authenticated user uploads a crafted `.dll` containing a malicious `IMarketplacePlugin` implementation
2. The server immediately loads the assembly and executes `Activator.CreateInstance()`
3. Attacker code runs in the app's process — full server compromise

### Impact
- Full remote code execution on the server
- Data exfiltration from MinIO, database, and environment
- Lateral movement within the infrastructure
- Privilege escalation to any admin operation

### Existing Mitigations
- JWT authentication required (but not admin-only)
- `.dll` extension check (easily satisfied by any compiled .NET assembly)

### Recommendations
1. **Immediately** add admin-only authorization: check `currentUser.isAdmin` before any plugin operation
2. Require cryptographic signing of plugin assemblies and verify signatures before loading
3. Consider running plugins in a separate process with restricted permissions
4. Sanitize the upload filename or generate a random filename
5. Add audit logging for all plugin operations

---

## Finding 2 — HIGH: SSRF via Image Download Service (UploadFromUriAsync)

### Files
- `backend/Infrastructure/Storage/MinIOStorageService.cs` (lines 110–183)
- `backend/Services/ImageDownloadBackgroundService.cs` (line 194)

### Vulnerable Code

```csharp
// MinIOStorageService.cs:131-133
using var httpClient = _httpClientFactory.CreateClient("MinIOStorage");
using var response = await httpClient.GetAsync(uri, ct);
```

### Issue Details

The `UploadFromUriWithMetadataAsync` method accepts a URI and performs an HTTP GET request to it using the server's HTTP client. The `ImageDownloadBackgroundService` calls this method with URLs from marketplace imports (`pending.imageUrl`). While this is a background service, the image URLs originate from user-controlled marketplace data.

The `HttpClient` is configured in `Program.cs` with only a timeout setting — there are no restrictions on:
- Internal/private IP ranges (e.g., `127.0.0.1`, `169.254.169.254`, `10.0.0.0/8`)
- URL schemes (e.g., `file://`, `gopher://`)
- DNS rebinding protection

### Attack Path
1. An attacker creates a marketplace product listing with an image URL pointing to `http://169.254.169.254/latest/meta-data/` (AWS metadata) or an internal service
2. The backend downloads the content from the internal URL and stores it as a "product image"
3. The attacker retrieves the file via the download endpoint, obtaining internal data

### Impact
- Access to cloud instance metadata (IAM credentials on AWS/GCP/Azure)
- Port scanning of internal networks
- Reading data from internal HTTP services

### Existing Mitigations
- None. The `MinIOStorage` HttpClient has no URL validation.

### Recommendations
1. Validate URLs before fetching: reject private/internal IP ranges, non-HTTP(S) schemes
2. Use DNS resolution validation (resolve the hostname, check the resulting IP is public)
3. Consider using a dedicated outbound proxy for URL fetching

---

## Finding 3 — MEDIUM: Path Traversal in File Upload Filenames (Product & Store Storage)

### Files
- `backend/Controllers/FileController.cs` (lines 222, 304)
- `backend/Controllers/StoreStorageController.cs` (line 95)

### Vulnerable Code

```csharp
// FileController.cs:222
var filePath = $"{selectedStoreId}/{productId}/{uniqueKey}/{file.FileName}";

// FileController.cs:304 (streaming upload)
var filePath = $"{selectedStoreId}/{productId}/{uniqueKey}/{fileName}";

// StoreStorageController.cs:95
var filePath = $"{storeId}/store/{uniqueKey}/{file.FileName}";
```

### Issue Details

The user-supplied `file.FileName` (from `IFormFile`) or `contentDisposition.FileName.Value` (from manual multipart parsing) is directly interpolated into the MinIO object key without sanitization. While MinIO object storage uses flat key-value semantics (not a true filesystem), a filename like `../../../other-store/secrets` would still create an object with that exact key, potentially overlapping with other tenants' key space.

The GUID segment (`uniqueKey`) provides some mitigation since the path becomes `{storeId}/{productId}/{guid}/../../../other`, but the traversal segments may still escape the tenant prefix depending on how MinIO handles the key normalization.

Additionally, in the streaming upload endpoint (line 300-304):
```csharp
var fileName = contentDisposition.FileName.Value ?? "unknown";
```
The `FileName.Value` from manual `ContentDispositionHeaderValue` parsing may not strip path segments.

### Impact
- Potential cross-tenant data access or overwrite in object storage
- Object key pollution

### Existing Mitigations
- GUID path segment adds unpredictability
- MinIO treats keys as opaque strings (no filesystem path traversal), but key overlap is possible
- Each download validates `storeId` ownership via database records

### Recommendations
1. Sanitize filenames: `Path.GetFileName(file.FileName)` to strip directory components
2. Consider generating fully random object keys and storing the original display name only in the database

---

## Finding 4 — MEDIUM: Public File Endpoint Exposes File Enumeration (IDOR)

### Files
- `backend/Controllers/FileController.cs` (lines 666–688, 690–712)

### Vulnerable Code

```csharp
// PublicFileController.cs
[HttpGet]
[Route("/pub/files/{id}/{filename}")]
public async Task GetPublicFile(int id, string filename)
{
    var file = await _db.files.FirstOrDefaultAsync(f => f.Id == id && f.isPublic);
```

```csharp
[HttpGet]
[Route("/pub/store-files/{id}/{filename}")]
public async Task GetPublicStoreFile(int id, string filename)
{
    var file = await _db.storeFiles.FirstOrDefaultAsync(f => f.Id == id && f.isPublic);
```

### Issue Details

The `PublicFileController` serves files by sequential integer ID with no authentication. The `filename` path segment is decorative — the actual file served is determined solely by the `id` parameter. An attacker can:
1. Enumerate all public file IDs by iterating integers
2. Download any file marked as `isPublic = true` across all stores/tenants

Additionally, responses include `Cache-Control: public, max-age=31536000, immutable`, meaning these files could be cached by CDNs/proxies indefinitely, making revocation of public access ineffective.

### Impact
- Cross-tenant information disclosure for all public files
- No ability to scope public file access to specific consumers
- Revocation of `isPublic` flag does not clear CDN caches

### Existing Mitigations
- Only files explicitly marked `isPublic = true` are served
- The `filename` segment is ignored (no path traversal possible)

### Recommendations
1. Use opaque, unguessable identifiers (UUIDs) instead of sequential integer IDs for public file URLs
2. Add `Cache-Control: private, no-cache` or shorter max-age with `s-maxage=0` for revocability
3. Consider per-tenant scoping in the URL path

---

## Finding 5 — LOW: Share Token Security Assessment (Satisfactory with Notes)

### Files
- `backend/Controllers/FileController.cs` (lines 519–571) — product file sharing
- `backend/Controllers/StoreStorageController.cs` (lines 546–612) — store file/folder sharing
- `backend/Models/FileShareToken.cs`, `StoreFileShareToken.cs`, `StoreFolderShareToken.cs`

### Token Generation

```csharp
// FileController.cs:550-551
var tokenBytes = RandomNumberGenerator.GetBytes(32);
var token = Base64UrlEncoder.Encode(tokenBytes);
```

### Assessment

**Strengths:**
- Tokens use `RandomNumberGenerator.GetBytes(32)` — cryptographically secure, 256 bits of entropy
- Brute-force is infeasible (2^256 keyspace)
- Tokens have mandatory expiration with restricted TTL values: 1h, 24h, 7d, 30d
- Expiration is checked on every access via `IsExpired` property
- Token lookup uses exact string match against database

**Weaknesses / Notes:**
- Tokens are not single-use — they can be accessed unlimited times until expiration
- No rate limiting on the shared file/folder endpoints (`/shared/{token}`, `/shared/store-file/{token}`, `/shared/store-folder/{token}`)
- No mechanism to revoke individual tokens before expiration
- The `SharedFileController` and `SharedStoreStorageController` have no authentication — by design, but worth noting

### Impact
- Low risk of token compromise via brute-force (effectively impossible)
- Shared links remain valid for their full TTL even if access should be revoked

### Recommendations
1. Add token revocation capability (delete token endpoint)
2. Consider adding rate limiting on shared endpoints to prevent abuse
3. Add optional single-use token support for sensitive files

---

## Finding 6 — MEDIUM: Shared Folder Download Bypasses Store Ownership Verification

### Files
- `backend/Controllers/StoreStorageController.cs` (lines 864–878)

### Vulnerable Code

```csharp
// SharedStoreStorageController
[HttpGet]
[Route("/shared/store-folder/{token}")]
public async Task GetSharedFolder(string token, CancellationToken ct)
{
    var shareToken = await _context.storeFolderShareTokens
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.token == token, ct);

    if (shareToken == null || shareToken.IsExpired)
        throw ApiException.NotFound("Ссылка не найдена или истекла");

    var folderController = HttpContext.RequestServices.GetRequiredService<StoreStorageController>();
    folderController.ControllerContext = ControllerContext;
    await folderController.StreamFolderZip(shareToken.storeId, shareToken.folderId, ct);
}
```

### Issue Details

The `SharedStoreStorageController.GetSharedFolder` method resolves a `StoreStorageController` from DI and calls `StreamFolderZip` directly, bypassing the authenticated controller's `GetSelectedStoreId()` check. The `storeId` comes from the `StoreFolderShareToken` entity, which is set at token creation time when the user is authenticated.

This is by design (shared links should work without auth), but the `StreamFolderZip` method is `internal`, meaning any code in the same assembly can call it with any `storeId` — this is an implicit trust boundary.

When `folderId` is `null` in the share token, **all files in the entire store** are included in the ZIP download. This means a shared "root folder" link exposes the entire store's file storage.

### Impact
- A single shared root-folder link exposes all store files
- No granular access control within the ZIP — all files are included regardless of individual file permissions

### Recommendations
1. Prevent sharing the root folder (require explicit folder selection)
2. Or exclude files not in the specific folder hierarchy when sharing root

---

## Finding 7 — LOW: Excel/ClosedXML Import — Formula Injection Risk

### Files
- `backend/Services/ExcelProductService.cs` (lines 136–184 for import, lines 11–134 for export)

### Code Review

```csharp
// ExcelProductService.cs:138
var wb = new XLWorkbook(file);
```

### Assessment

**XXE (XML External Entity):** ClosedXML is built on top of OpenXML SDK which processes XLSX files (ZIP-compressed XML). Modern versions of ClosedXML (v0.95+) and the underlying .NET XML parsers default to secure settings with DTD processing disabled. **XXE is not a concern here.**

**Formula Injection (CSV Injection):** The export side writes cell values directly:
```csharp
ws.Cell(row, c++).Value = p.name;
ws.Cell(row, c++).Value = p.description;
```

If product data contains strings starting with `=`, `+`, `-`, `@`, or `\t`, these could be interpreted as formulas when the exported XLSX is opened in Excel or LibreOffice. However:
- ClosedXML's `.Value` property writes cell values as explicit text/number types, not as formula cells
- Formulas would need to be set via `.FormulaA1` property
- The import path (`ParseImportFile`) reads values with `.GetString()`, which returns the cell's display value — formulas in imported cells would be read as their formula text, not executed server-side

**Risk:** If an attacker can control product names/descriptions and a store owner exports products to Excel, the cells could contain misleading content but would not execute as formulas due to ClosedXML's type-safe value assignment. **This is a LOW risk.**

### Recommendations
1. Consider prefixing cell values starting with `=`, `+`, `-`, `@` with a single quote during export as defense-in-depth
2. Validate/sanitize imported cell values for unexpected content

---

## Finding 8 — INFO: MinIO Storage Service — Path Sanitization Assessment

### Files
- `backend/Infrastructure/Storage/MinIOStorageService.cs`

### Assessment

The `MinIOStorageService` passes file paths directly to the MinIO SDK as object keys:
```csharp
var request = new PutObjectArgs()
    .WithBucket(_bucket)
    .WithObject(path)
```

MinIO treats object keys as opaque strings — there is no filesystem-level path traversal. A key like `../../../etc/passwd` would simply create an object with that literal key name. The security boundary is maintained at the application level where:
- Upload paths are constructed with `{storeId}/{productId}/{guid}/{filename}`
- Download paths come from database records, not user input directly
- The bucket policy is set to private (no anonymous access)

**The MinIO service itself does not introduce path traversal vulnerabilities**, but the callers must ensure tenant-scoped path construction (see Finding 3).

---

## Finding 9 — INFO: Thumbnail Service Assessment

### Files
- `backend/Services/ThumbnailService.cs`
- `backend/Controllers/ThumbnailController.cs`

### Assessment

- **No SSRF:** Thumbnails are generated from files already stored in MinIO (fetched via presigned URL to the configured MinIO endpoint). The `originalPath` comes from the database record, not user input.
- **No path traversal:** The thumbnail path is constructed as `_thumbnails/{width}/{originalPath}` where `originalPath` is from the database.
- **Width validation:** Only allowed widths (64, 128, 256, 512) are accepted.
- **Auth:** The `ThumbnailController` requires JWT auth and validates store ownership. The `StoreStorageController` thumbnail endpoint also validates store ownership.
- **DoS potential (LOW):** Thumbnail generation uses ImageSharp to decode and resize images. A crafted image (decompression bomb) could consume excessive memory. ImageSharp has default memory limits, but this should be verified.

### Recommendations
1. Consider setting explicit `Configuration.MemoryAllocator` limits in ImageSharp to prevent decompression bombs

---

## Summary Table

| # | Severity | Finding | File(s) | Exploitable? |
|---|----------|---------|---------|-------------|
| 1a | **CRITICAL** | Plugin upload lacks admin authorization — any authenticated user can upload | `PluginController.cs:84` | Yes |
| 1b | **CRITICAL** | Plugin upload enables RCE via DLL loading + `Activator.CreateInstance` | `PluginLoader.cs:29-45` | Yes |
| 1c | **HIGH** | No DLL signing, hash verification, or content validation | `PluginController.cs:91` | Yes |
| 1d | **HIGH** | No security sandboxing for loaded plugins | `PluginLoadContext.cs` | Yes |
| 1e | **MEDIUM** | Path traversal in plugin upload filename | `PluginController.cs:99` | Likely |
| 2 | **HIGH** | SSRF via UploadFromUriAsync (image download) | `MinIOStorageService.cs:131` | Depends on input source |
| 3 | **MEDIUM** | Unsanitized filenames in storage paths | `FileController.cs:222,304` | Possible |
| 4 | **MEDIUM** | Public file endpoint allows ID enumeration (IDOR) | `FileController.cs:668` | Yes |
| 5 | **LOW** | Share tokens: no revocation, no rate limiting | `FileController.cs:550` | Low |
| 6 | **MEDIUM** | Shared root-folder link exposes entire store | `StoreStorageController.cs:875` | Yes |
| 7 | **LOW** | Excel export formula injection risk | `ExcelProductService.cs` | Low |
| 8 | **INFO** | MinIO path sanitization (adequate) | `MinIOStorageService.cs` | No |
| 9 | **INFO** | Thumbnail service (adequate) | `ThumbnailService.cs` | No |

---

## Priority Remediation Order

1. **Immediate (Finding 1a+1b):** Add `isAdmin` check to `PluginController`. This is a CRITICAL RCE vulnerability exploitable by any authenticated user.
2. **Short-term (Finding 1c+1d):** Implement plugin signing verification. Evaluate process-level sandboxing.
3. **Short-term (Finding 2):** Add URL validation to `UploadFromUriAsync` to block private/internal IPs.
4. **Short-term (Finding 3):** Sanitize all user-supplied filenames before constructing storage paths.
5. **Medium-term (Finding 4):** Replace sequential integer IDs with UUIDs for public file URLs.
6. **Medium-term (Finding 6):** Restrict root-folder sharing or scope downloads.
7. **Low priority (Findings 5, 7):** Add token revocation, rate limiting, and formula injection defense-in-depth.
