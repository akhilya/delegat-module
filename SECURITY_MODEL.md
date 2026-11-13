# Threat model: Delegat.Module instances + Delegat Control Plane

Scope: `akhilya/delegat-module` (customer instance), `akhilya/delegat-module-platform` (Control Plane, "CP"), the marketplace adapters (`akhilya/delegat-adapter-ozon`, `-wildberries`, `-opencart`, `-woocommerce`, `-yandex-market`) and `akhilya/delegat-marketplace-sdk`. No separate infrastructure repo: deployment is the docker-compose files in each repo, plus the CP's Dokploy instance template (`delegat-module-platform/backend/Provisioning/templates/instance.docker-compose.yml`, `InstanceComposeTemplate.cs`).

Sources: the code, `delegat-module-platform/docs/security-pass-p12.md`, `docs/platform-link.md`, `docs/runbooks/ops-mcp.md`, `delegat-module/docs/architecture/authentication.md`. The historical `delegat-module/docs/security/SECURITY_AUDIT_REPORT.md` (2026-07-17) is not revalidated.

## User decisions (from the scan owner)
- **Assets:** all of these are equally critical: customer catalog data and marketplace API credentials; CP billing, payments and the T-Bank flow; fleet control (Dokploy keys, Platform Link secrets, Ops access).
- **Attackers:** all of them are in scope: anonymous internet users, registered CP customers, role-limited instance employees, a store admin attacking the CP or other instances, and prompt injection through the AI assistant or marketplace data.
- **Out of scope / accepted risk:** none. Report everything, including store-admin capabilities inside their own instance (plugin DLL upload, Python sandbox, DB dumps), self-hosted instances and malicious staff.
- **Tags:** Component, CWE, OWASP category.

## System overview and trust boundaries
- Per-customer **isolated instance** (Docker stack on `{label}.module.delegat.pro`): ASP.NET Core API (.NET 8, EF Core, MySQL 8.4), Vue SPA behind nginx, MinIO-compatible object storage, an optional Python sandbox (`sandbox/python`, AI profile) and optional Ollama embeddings. Each instance has its own DB, so there is no multi-tenancy inside one DB. A single instance can hold several **stores**, and store membership and role permissions isolate them.
- **Control Plane** (`module.delegat.pro`): .NET 8 minimal-API backend plus Postgres, Vue SPA with Marketing, Customer and Ops consoles. It sells plans (T-Bank), provisions instances on compute nodes via the Dokploy API (tRPC/WebSocket), reconciles the fleet, handles backups/restore and tickets, and proxies hosted AI.
- **Boundaries:**
  1. Internet → instance API/SPA (JWT cookies, API keys for the Public API `/api/v1`).
  2. Internet → CP API/SPA (Identity cookie `delegat.platform.auth`, anonymous marketing/public endpoints, payment webhooks).
  3. Instance → CP over **Platform Link** `/platform/v1/*`, HMAC-SHA256 with a per-instance `INSTANCE_LINK_SECRET` and 300 s skew. An instance must not reach another instance's data, other orgs' entitlements or billing, or Ops functions. Covers SSO exchange (`/sso/exchange`, CP `/api/sso/consume`) and the hosted-AI proxy (`/ai/{provider}/{**path}`).
  4. CP → Dokploy/nodes (Dokploy API keys, encrypted with `SecretProtector` AES-GCM, key `ENCRYPTION_KEY`).
  5. Instance → external marketplaces and stores (Ozon, WB, Yandex Market, OpenCart, WooCommerce, Medusa, Avito) through in-process plugin DLLs built on the SDK. Data coming back from marketplaces is untrusted.
  6. Instance API → Python sandbox (`SANDBOX_SECRET`, rlimits, read-only container) and connected MCP servers (AI agent).
  7. WooCommerce/OpenCart **bridge plugins** (PHP, e.g. `delegat-adapter-woocommerce/wordpress-plugin/delegat-bridge`) run on the customer's own shop and are reachable from the internet.

## Components
| Component | Code |
|---|---|
| **Instance API** | `delegat-module/backend/**` (Controllers, Services, Infrastructure/Auth, Hubs) except AI below |
| **Instance AI Assistant** | `delegat-module/backend/Services/Ai/**`, `Controllers/Agent*`, `Controllers/Ai*`, `sandbox/python/**` |
| **Instance Frontend** | `delegat-module/frontend/**` |
| **Marketplace Adapters** | `delegat-module/plugins/**`, `delegat-adapter-*/**` (C# and PHP bridge plugins), `delegat-marketplace-sdk/**` |
| **Control Plane API** | `delegat-module-platform/backend/**` except Provisioning/PlatformLink/Ops |
| **Platform Link** | `delegat-module-platform/backend/PlatformLink/**`, `delegat-module/backend/Services/PlatformLink/**`, SSO endpoints on both sides |
| **CP Ops & Provisioning** | `delegat-module-platform/backend/{Ops,Provisioning,Infrastructure}/**`, `backend/Auth/Ops*`, `ops-mcp/**` |
| **CP Frontend** | `delegat-module-platform/frontend/**` |
| **Deployment** | `docker-compose*.yml`, Dockerfiles, nginx configs, `.github/workflows/**` in all repos |

## Entry points
- **Instance, anonymous:** `JwtAuthController` login/refresh/passkey/TOTP/invitation flows; `StoreController` `GET /stores/` and `/stores/{id}` (`[AllowAnonymous]`); `SettingsController` public settings; `AgentController` `files/{id}/download` (ticket-based); `PublicMediaExportController`; `DocsController`.
- **Instance, authenticated:** all other controllers, with permissions via `RequiresPermission` / `PermissionService` / `CabinetViewOnlyGuard`. High-risk ones: `PluginController` (DLL upload, `Permissions.PluginsManage`), DB dump (`DatabaseDumpService`), file upload/storage, Excel import, catalog scrape (`CatalogScrapeController`, SSRF surface, `UrlSafety`), AI agent tools (`AgentSqlGuard`, `SqlReadonlyService`, `CabinetRequestInvoker`, `McpToolGateway`, `PythonSandboxClient`), SignalR `OperationHub`.
- **Instance Public API** `/api/v1/*`: API key bearer (`ApiKeyAuthenticationHandler`), store-scoped.
- **CP, anonymous:** `/api/auth/register|login|login/totp|login/recovery`, passkey login, `/api/public/contact`, `/api/public/site-status`, `/api/public/plans`, `/api/billing/webhooks/{provider}`, `/api/sso/consume`.
- **CP, customer:** `/api/billing/*` (incl. `/stub/complete`, `/trial`, `/checkout`, `/return/confirm`), `/api/orgs`, `/api/invites`, `/account`, tickets (with attachments on disk).
- **CP, staff:** `/api/ops/*` with policies `RequireOpsStaff` (Ops or Moderator), `RequireOps`, `RequirePrimaryOps`; Ops MCP bearer `OPS_MCP_TOKEN`, which impersonates PrimaryOps.
- **CP, machine:** `/platform/v1/*` (HMAC).

## Assets
- Marketplace/store API credentials stored per connection in instance DBs; customer catalog, orders, files and media.
- Instance JWT signing keys, API keys, `SANDBOX_SECRET`, `INSTANCE_LINK_SECRET`.
- CP: customer accounts, orgs, subscriptions and payment state (T-Bank terminal key/password), promo codes, entitlements and quotas.
- Fleet control: Dokploy API keys, node access, backup/restore, break-glass, release pins, `ENCRYPTION_KEY`, Data Protection keys, `OPS_MCP_TOKEN`, Ops accounts.
- Hosted-AI provider keys and per-org AI usage accounting on the CP.

## Attacker profiles (all in scope)
1. Anonymous internet attacker against CP or instance.
2. Registered CP customer trying to reach other orgs, get free/paid plan state, or abuse provisioning.
3. Role-limited instance employee (e.g. Viewer, `workspace.view` demo member) escalating to admin, other stores, plugins, Python or dumps.
4. Store admin, or anyone holding an instance's link secret, attacking the CP, other instances, or the host node.
5. Prompt injection through marketplace data, scraped pages, uploaded files or MCP tool output that steers the AI assistant into unauthorized actions or data exfiltration.
6. Malicious or compromised marketplace/store endpoint returning hostile responses to adapters, or attacking the bridge plugins.
7. Malicious staff (Moderator vs Ops vs PrimaryOps separation).

## Existing controls
- Platform Link HMAC with skew check; instance purge disables the link (`PlatformLinkAuthMiddleware`).
- Secrets at rest AES-256-GCM (`SecretProtector`, CP; `EncryptionService`, instance).
- Instance auth: HttpOnly SameSite=Lax JWT cookies, TOTP and passkeys, login tickets, permission policy provider, view-only guard, API key handler.
- AI: `AgentAccess`, `AgentSqlGuard`, `PlatformRequestPolicy`, `ConnectedMcpToolAllowlist`, plan confirmation tokens, file-download tickets.
- Sandbox: secret header, read-only container, `no-new-privileges`, pids/mem/cpu limits, `/tmp` tmpfs. It shares `delegat-network` with API/DB/MinIO.
- CP: rate-limited auth endpoints, Identity roles, Moderator cannot grant Ops, maintenance-mode middleware, disposable-email blocklist.
- `UrlSafety`, `FileNameSafety` helpers.

## Accepted risk / out of scope
None. The owner asked for everything to be reported.

## Open questions
- Are instance store admins customers (untrusted relative to the host node and the CP)? Assumed yes, which makes plugin DLL upload and sandbox escape cross-boundary.
- Is the sandbox network-isolated from MySQL/MinIO? The compose file puts it on `delegat-network`.
- Are self-hosted instances linked to the production CP?
