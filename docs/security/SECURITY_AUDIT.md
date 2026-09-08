# Security Audit Report — Delegat Backend

**Date:** 2026-07-17  
**Scope:** `/workspace/backend` — encryption, secrets handling, AI integration, injection vectors  
**Severity scale:** Critical > High > Medium > Low > Informational

---

## Finding 1: Padding Oracle Vulnerability in EncryptionService (HIGH)

**File:** `backend/Infrastructure/Security/EncryptionService.cs`, lines 17–51  
**CWE:** CWE-649 (Reliance on Obfuscation or Protection Mechanism That is Not a Cryptographic Primitive)

### Vulnerable Code

```csharp
// Encrypt: AES-256-CBC with random IV, no HMAC
public string Encrypt(string plainText)
{
    using var aes = Aes.Create();
    aes.Key = _key;
    aes.GenerateIV();
    using var encryptor = aes.CreateEncryptor();
    var plainBytes = Encoding.UTF8.GetBytes(plainText);
    var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
    var result = new byte[aes.IV.Length + cipherBytes.Length];
    Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
    Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
    return Convert.ToBase64String(result);
}

// Decrypt: no authentication check before decryption
public string Decrypt(string cipherText)
{
    var fullCipher = Convert.FromBase64String(cipherText);
    using var aes = Aes.Create();
    aes.Key = _key;
    var iv = new byte[16];
    Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
    aes.IV = iv;
    var cipherBytes = new byte[fullCipher.Length - 16];
    Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);
    using var decryptor = aes.CreateDecryptor();
    var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    return Encoding.UTF8.GetString(plainBytes);
}
```

### Issues

1. **No HMAC/authentication:** AES-256-CBC is used without an HMAC or any authenticated encryption mode. The ciphertext is not integrity-protected. This makes the system vulnerable to padding oracle attacks if an attacker can observe whether decryption succeeds or fails (e.g., via different HTTP error codes or timing).

2. **IV handling is correct:** `aes.GenerateIV()` produces a cryptographically random IV per encryption call, and it is prepended to the ciphertext. This part is properly implemented.

3. **Single static key for all tenants:** All marketplace credentials across all stores are encrypted with the same `ENCRYPTION_KEY`. Compromise of this single key exposes every tenant's credentials.

### Attack Path

If an attacker can submit modified ciphertexts to an endpoint that decrypts them (e.g., `GET /marketplace/connections/{id}` which decrypts `credentialsJson`), they can determine whether padding was valid based on the server's response, then iteratively decrypt any ciphertext byte-by-byte.

### Impact

Full recovery of all encrypted marketplace API credentials (Ozon API keys, Wildberries tokens, etc.) stored in the database.

### Recommendation

Replace AES-256-CBC with AES-256-GCM (authenticated encryption), or add HMAC-SHA256 over (IV || ciphertext) and verify before decryption. The .NET `AesGcm` class is available in `System.Security.Cryptography`.

---

## Finding 2: Marketplace Credentials Returned in Plaintext via API (HIGH)

**File:** `backend/Controllers/MarketplaceConnectionController.cs`, lines 112–144

### Vulnerable Code

```csharp
[HttpGet]
[Route("/marketplace/connections/{connectionId}")]
public async Task<IActionResult> GetConnection(int connectionId)
{
    // ...
    var credentialsJson = connection.credentialsJson;
    if (_encryptionService != null
        && !string.IsNullOrEmpty(credentialsJson)
        && _encryptionService.IsEncrypted(credentialsJson))
    {
        credentialsJson = _encryptionService.Decrypt(credentialsJson);
    }

    return Ok(new MarketplaceConnection.ConnectionDetailDTO
    {
        // ...
        credentialsJson = credentialsJson,  // <-- full plaintext credentials in response
        // ...
    });
}
```

### Issue

When a user requests connection details, the full decrypted credentials (API keys, tokens, client secrets) are returned in the HTTP response body. Any user with access to the store can retrieve the raw marketplace API credentials.

### Existing Mitigation

The `GetSelectedStoreIdAsync()` call validates store membership, preventing cross-store access. However, any authenticated store member can extract all marketplace credentials for that store.

### Impact

- Any store member (not just admins) can exfiltrate marketplace API credentials
- Credentials are visible in browser network tools, proxy logs, and any network interceptor
- XSS in the frontend would allow credential theft

### Recommendation

- Never return raw credentials in API responses; return a masked version (e.g., `****abcd`)
- Require admin role to view connection details, or at minimum to view credential fields
- Provide a separate admin-only endpoint for credential retrieval if editing is needed

---

## Finding 3: Store ID from Cookie Without Server-Side Session Binding (MEDIUM)

**File:** `backend/Infrastructure/Auth/CurrentUserService.cs`, lines 78–85

### Vulnerable Code

```csharp
public int? GetSelectedStoreId()
{
    var storeIdCookie = _httpContextAccessor.HttpContext?.Request.Cookies["selected_store_id"];
    if (storeIdCookie != null && int.TryParse(storeIdCookie, out var storeId))
        return storeId;
    return null;
}
```

### Issue

The `selected_store_id` is read from a client-side cookie. While `GetSelectedStoreIdAsync()` validates membership, some controllers and the AI tool dispatcher call `GetSelectedStoreId()` (the synchronous version) which does **not** validate membership — it trusts the cookie value directly.

### Existing Mitigation

Most critical paths (`MarketplaceConnectionController`, `AiController`) use `GetSelectedStoreIdAsync()` which validates store membership. The `AiSettingsController.GetStoreSettings()` and `SaveStoreSettings()` endpoints use `GetSelectedStoreId()` without membership validation (line 179, 199), but these only expose/modify AI personalization settings, not sensitive data.

### Impact

A user could potentially read/write AI store settings (communication style, store description) for a store they don't belong to by manipulating the `selected_store_id` cookie. Low direct impact but violates the principle of least privilege.

### Recommendation

Remove the synchronous `GetSelectedStoreId()` method or always validate membership. Consider storing the selected store ID server-side (in the JWT claims or session).

---

## Finding 4: SSRF via Admin-Controlled AI Model Configuration (MEDIUM)

**File:** `backend/Controllers/AiSettingsController.cs`, lines 77–101, 148–173  
**File:** `backend/Services/Ai/AiApiClient.cs`, lines 42–66, 126–145

### Vulnerable Code

```csharp
// Admin sets arbitrary apiUrl and proxyUrl for AI models
var model = await _modelService.CreateAsync(
    req.Name, req.ApiUrl, req.ApiKey, req.ModelId,
    req.Provider, proxyUrl: req.ProxyUrl, ct: ct);

// AiApiClient connects to whatever URL is configured
var baseUrl = model.apiUrl.TrimEnd('/');
// ... builds HTTP client with user-provided proxy
var proxied = CreateProxiedClient(model.proxyUrl);
```

### Issue

Admin users can configure AI model endpoints (`apiUrl`) and proxy URLs (`proxyUrl`) to point to arbitrary internal or external hosts. The `AiApiClient` then makes HTTP requests to these URLs without any allowlist validation. The `DiscoverModels` endpoint also makes outbound requests to arbitrary URLs.

Additionally, `proxyUrl` supports SOCKS5 proxies with embedded credentials (line 46: `socks5://user:pass@host:port`), which could be used to pivot into internal networks.

### Existing Mitigation

These endpoints are admin-only (`EnsureAdminAsync`). Non-admin users cannot configure AI model URLs.

### Impact

A compromised admin account or malicious admin could:
- Scan internal network services via the server
- Exfiltrate data by routing AI requests through an attacker-controlled proxy
- Access cloud metadata endpoints (e.g., `http://169.254.169.254/`)

### Recommendation

- Validate `apiUrl` and `proxyUrl` against an allowlist of permitted hosts/schemes
- Block private/internal IP ranges (RFC 1918, link-local, cloud metadata)
- Consider DNS rebinding protection

---

## Finding 5: Encryption Optionally Disabled — Credentials Stored in Plaintext (MEDIUM)

**File:** `backend/Program.cs`, lines 50–52  
**File:** `backend/Controllers/MarketplaceConnectionController.cs`, lines 166–168  
**File:** `.env.example`, line 28

### Vulnerable Code

```csharp
// Program.cs
var encryptionKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ?? "";
if (!string.IsNullOrEmpty(encryptionKey))
    builder.Services.AddSingleton(new EncryptionService(encryptionKey));

// MarketplaceConnectionController.cs
var encryptedCreds = _encryptionService != null && !string.IsNullOrEmpty(input.credentialsJson)
    ? _encryptionService.Encrypt(input.credentialsJson)
    : input.credentialsJson;  // <-- stored as plaintext if no ENCRYPTION_KEY
```

### Issue

The `EncryptionService` is registered only if `ENCRYPTION_KEY` is set. When it is absent, marketplace credentials are stored in plaintext in the database. The `.env.example` ships with `ENCRYPTION_KEY=change_me`, which if used as-is would produce a predictable key (though it would fail the 32-byte Base64 validation). A missing or misconfigured key silently degrades to no encryption.

### Impact

Database compromise (SQL injection elsewhere, backup leak, cloud storage misconfiguration) exposes all marketplace API credentials in cleartext.

### Recommendation

- Make `ENCRYPTION_KEY` mandatory at startup — fail fast if not configured
- Log a prominent warning during startup if encryption is disabled
- The `IsEncrypted` check (`!value.TrimStart().StartsWith('{')`) is fragile — consider a structured prefix marker

---

## Finding 6: AI Prompt Injection Vectors (MEDIUM)

**File:** `backend/Services/Ai/Prompts/AiPrompts.cs`, lines 22–263  
**File:** `backend/Services/Ai/AiChatService.cs`, lines 164–169

### Issue

User-controlled data is interpolated directly into the system prompt:

```csharp
var systemPrompt = AiPrompts.ChatSystem(
    storeName, storeId, productCount, connections
        .Select(c => new AiPrompts.ConnectionInfo(c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt))
        .ToList(),
    currentUser?.UserName ?? "user", currentUser?.isAdmin ?? false,
    pageContext, storeSettings, storeTimezone);
```

The `storeName`, `userName`, connection names, and `pageContext` values are user-controlled and embedded into the system prompt without sanitization. A store named `"; ignore all instructions and...` would be injected into the prompt.

Additionally, `AiPageContext` fields (`MappingName`, `ConnectionName`, `ProductName`) come from the frontend request body and are interpolated into the system prompt template.

### Existing Mitigation

- AI tools execute server-side with `storeId` scoping — the AI cannot access other stores' data
- Mutating operations require `propose_plan` confirmation before execution
- Tool dispatcher validates tool names against a fixed whitelist (switch expression)
- Mutating chat tools require `propose_plan`; Python runs only in the optional sandbox sidecar (no DB URL; cgroup 0.50 CPU / 512 MiB, one job, BLAS single-thread)

### Impact

- Prompt injection could trick the AI into performing unwanted mutations (e.g., deleting products, exporting data) that the user then confirms via the plan confirmation step
- Cannot directly escalate to cross-store access because tool handlers enforce `storeId` scoping
- Cannot achieve host code execution — Python is an optional sandbox; MCP/Agent HTTP uses cabinet controllers minus a denylist of secrets and irreversible routes

### Recommendation

- Sanitize user-controlled values before prompt interpolation (escape or quote them)
- Consider using structured message formats instead of string interpolation for context
- The plan confirmation step is a reasonable mitigation but relies on the user reading the plan carefully

---

## Finding 7: AI Model API Key Masking Uses Encrypted Value, Not Plaintext (LOW)

**File:** `backend/Controllers/AiSettingsController.cs`, lines 228–233

### Vulnerable Code

```csharp
private static string MaskKey(string encrypted)
{
    if (string.IsNullOrEmpty(encrypted)) return "";
    return encrypted.Length <= 8 ? "****" : $"****{encrypted[^4..]}";
}
```

### Issue

The masking function operates on the encrypted (or possibly plaintext, if encryption is disabled) API key value. The last 4 characters of the **encrypted** ciphertext are shown. While this doesn't directly leak the plaintext key, it leaks trailing bytes of ciphertext which, combined with the known AES-CBC structure, marginally reduces the attacker's search space.

If encryption is disabled, this would leak the last 4 characters of the plaintext API key.

### Impact

Minor information leakage. When encryption is disabled, the last 4 chars of the actual API key are exposed.

### Recommendation

Always mask without revealing any characters of the actual or encrypted key value. Use a fixed mask like `"••••••••"` or mask the plaintext key (after decryption) showing only the last 4 characters of the original key.

---

## Finding 8: No Raw SQL Injection or Command Execution Found (INFORMATIONAL)

### Analysis

- **No `FromSqlRaw` or `ExecuteSqlRaw` usage** found in the codebase. All database queries use Entity Framework LINQ/lambda expressions with parameterized queries.
- **No `Process.Start` or command execution** found anywhere in the backend code.
- **No string interpolation in SQL** — the one use of `ExecuteUpdateAsync` (in `DatabaseSeeder.cs`) uses the EF SetProperty API, not raw SQL.
- String interpolation in C# is used only for log messages, error messages, and UI text — never for query construction.

### Conclusion

The codebase has good hygiene regarding SQL injection and command injection. Entity Framework Core's LINQ API provides effective parameterization by default.

---

## Finding 9: Database Seeder — Credentials from Environment Variables (LOW)

**File:** `backend/Infrastructure/Data/DatabaseSeeder.cs`, lines 14–83  
**File:** `backend/Infrastructure/Configuration/ConfigurationExtensions.cs`, lines 84–92

### Analysis

The admin user is seeded from environment variables (`ADMIN_EMAIL`, `ADMIN_USERNAME`, `ADMIN_PASSWORD`). No hardcoded default credentials exist. The seeder skips creation if any of these are empty.

The `ADMIN_FORCE_RESET` flag allows password reset — this is logged with a reminder to remove it after use.

### Minor Issue

Admin credentials are logged at `Information` level:
```csharp
logger.LogInformation("Admin reset: username={Username}, email={Email} (ADMIN_FORCE_RESET=true — remove after use)",
    settings.Username, settings.Email);
```

The password is not logged, but username and email are. This is acceptable for operational logs but could be a concern in shared logging infrastructure.

### Impact

No hardcoded defaults. Secure design pattern. Minor log exposure of admin identity (not password).

---

## Finding 10: Settings Endpoints Properly Admin-Gated (INFORMATIONAL)

**File:** `backend/Controllers/SettingsController.cs`, lines 21–47

### Analysis

Both `GetAppSettings` and `PatchAppSetting` call `EnsureAdminAsync()` before proceeding. The `AppSettingsService` exposes operational limits (max products, rate limits, storage quotas) — no secrets are stored in app settings.

AI model configurations with API keys are managed through `AiSettingsController`, which also enforces admin access for all mutating and listing operations. The picker endpoint (`GET /ai/models/picker`) available to all authenticated users only returns model ID, name, and default status — no API keys or URLs.

### Conclusion

Configuration exposure is well-controlled. Admin-only access is consistently enforced.

---

## Finding 11: Cross-Store Isolation in AI Tool Handlers (INFORMATIONAL)

**File:** `backend/Services/Ai/Agent/AgentToolHost.cs`  
**File:** `backend/Services/Ai/Agent/SqlReadonlyService.cs`

### Analysis

Tool methods receive `storeId` from the authenticated session (`AgentSessionResolver`), not from model output. SQL always applies the current store. Catalog and platform handlers filter by `storeId`.

The AI model cannot choose another store's slice.

### Conclusion

Cross-store data isolation is properly enforced. The AI cannot be tricked into accessing another store's data through prompt injection.

---

## Summary Table

| # | Finding | Severity | Exploitable? |
|---|---------|----------|-------------|
| 1 | AES-CBC without HMAC (padding oracle) | HIGH | Requires oracle access (error-based) |
| 2 | Plaintext credentials in API response | HIGH | Yes — any store member |
| 3 | Cookie-based store ID without membership check | MEDIUM | Limited — AI settings only |
| 4 | SSRF via admin AI model config | MEDIUM | Admin-only |
| 5 | Optional encryption — silent plaintext fallback | MEDIUM | Misconfiguration-dependent |
| 6 | AI prompt injection vectors | MEDIUM | Mitigated by plan confirmation |
| 7 | API key masking leaks trailing chars | LOW | Minor info leak |
| 8 | No SQL/command injection | INFO | N/A — clean |
| 9 | Seeder uses env vars, no hardcoded creds | LOW | Log exposure only |
| 10 | Settings properly admin-gated | INFO | N/A — secure |
| 11 | Cross-store isolation enforced | INFO | N/A — secure |

---

## Priority Remediation Order

1. **Finding 1** — Switch to AES-256-GCM or add HMAC-SHA256 to prevent padding oracle attacks
2. **Finding 2** — Stop returning plaintext credentials in API responses; mask them
3. **Finding 5** — Make `ENCRYPTION_KEY` mandatory; fail startup if missing
4. **Finding 4** — Add URL allowlisting for AI model endpoints
5. **Finding 3** — Always validate store membership for selected store ID
6. **Finding 6** — Sanitize user-controlled values in AI system prompts
