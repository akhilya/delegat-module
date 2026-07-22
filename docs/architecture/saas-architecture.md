# Delegat.Module - SaaS Distribution Architecture

> This document covers the distribution infrastructure: provisioning, scaling, quota management, and the central portal (technical draft).
>
> **Product decisions for the Control Plane** (self-serve billing, Ops vs Customer surfaces, support, backups policy):  
> [control-plane.md](control-plane.md)
>
> Platform features and roadmap: [../platform/capabilities.md](../platform/capabilities.md)

---

## 1. Isolation Model: Per-Instance

Each customer receives an **isolated Docker stack**: dedicated MySQL, MinIO, delegat-api, and frontend.

**Why not shared MySQL/MinIO:**
- Migrating from shared to per-instance requires schema dumps, coordinated downtime, and updating connection strings in Dokploy for every instance - unjustifiably complex
- Physical isolation makes cross-tenant data leakage architecturally impossible

**Per-instance resource consumption (idle):**

| Service | RAM |
|---------|-----|
| delegat-api (.NET 8) | 150-250 MB |
| frontend (nginx) | 10-20 MB |
| MySQL 8.4 (tuned) | 180-350 MB |
| MinIO | 50-80 MB |
| **Total** | **390-700 MB** |

8 GB VM: 10-12 instances. 16 GB VM: 20-24 instances.

**MySQL tuning** (add to `docker-compose.prod.yml`):
```yaml
services:
  mysql:
    command: >
      --innodb-buffer-pool-size=64M
      --performance-schema=OFF
      --max-connections=50
      --skip-name-resolve
```
Reduces MySQL consumption from ~350 MB to ~180 MB.

---

## 2. DNS - Selectel Wildcard

**Current configuration:** Selectel DNS with wildcard record `*.your-domain.com → node IP`.

Individual A records per subdomain are **not needed** - the wildcard covers all subdomains automatically.

**Multi-node DNS routing:**
When multiple nodes have different IPs, the wildcard maps to a single IP. Options:
- **Option A (recommended for start):** A single nginx/HAProxy in front of all nodes - one IP, routes by HTTP Host header
- **Option B:** Per-node wildcards (`*.node-a.your-domain.com`, `*.node-b.your-domain.com`)

---

## 3. Overall Architecture

```
[Selectel DNS]
  *.your-domain.com → L4 load balancer (or single node IP)

[Node A - 16 GB VM]          [Node B - 16 GB VM]
  Dokploy Agent                 Dokploy Agent
  ├── client1.your-domain.com   ├── client3.your-domain.com
  │   ├── delegat-api           │   ├── delegat-api
  │   ├── frontend              │   └── ...
  │   ├── mysql
  │   └── minio
  └── client2.your-domain.com
      └── ...

[Central Portal - 4 GB VM]
  PostgreSQL (Registry DB)
  Provisioner Service
  Monitoring Poller

[CI/CD - GitHub Actions]
  push → build images → push GHCR → rolling update all instances
```

---

## 4. Quota Management on Plan Changes

**Current implementation (already in code):**
- `AppSettingsService` reads from env vars: `MAX_STORAGE_GB`, `MAX_PRODUCTS`, `MAX_FILES`, `MAX_CONNECTIONS`
- `QuotaService` enforces per-store (by `storeId`)
- Exceeded quota → `ApiException.Conflict` (409)

**Plan tiers:**

| Plan | MAX_STORAGE_GB | MAX_PRODUCTS | MAX_CONNECTIONS | AI_ENABLED |
|------|---------------|--------------|----------------|------------|
| Starter | 15 | 5,000 | 3 | false |
| Pro | 50 | 50,000 | 10 | true |
| Business | 150 | 200,000 | 25 | true |
| Enterprise | 500 | - | - | true |

`AI_ENABLED=true` unlocks the chat assistant, auto-mapping, and inline error explanation.
The AI model itself is configured by the instance admin in `/settings/admin/ai` - the platform
does not supply an API key; customers bring their own (Anthropic, OpenAI, etc.).

**Upgrade** (15 GB → 500 GB):
1. Portal updates `MAX_STORAGE_GB=500` via Dokploy API
2. Trigger redeploy (~30-60 sec downtime)

**Downgrade** (500 GB → 15 GB) - soft-cap strategy:
1. Set new limit via Dokploy API → trigger redeploy
2. Existing data is **not deleted** (grandfathered)
3. New uploads are blocked (`EnsureStorageQuotaAsync` handles this already)
4. Email customer: current usage + deadline to remove excess files

---

## 5. Scaling

**Node selection algorithm:**
```
1. SELECT * FROM nodes WHERE is_active AND current_instances < max_instances
2. ORDER BY current_instances ASC → pick least-loaded node
3. If empty → alert administrator
4. Create instance on selected node, current_instances++
```

**When all nodes are full:**
- **Stage A (startup):** email/Telegram alert → admin manually adds a VM, installs Dokploy, registers in Registry DB
- **Stage B (>1 VM/week):** auto-provision VM via Hetzner Cloud API + cloud-init script

---

## 6. Redis - Not Needed

| Use case | Redis needed? | Solution |
|----------|--------------|---------|
| Rate limiting | ❌ | In-memory (1 API container per instance) |
| Refresh tokens | ❌ | MySQL is sufficient |
| SignalR backplane | ❌ | In-memory (1 API container) |
| Marketplace API cache | ⚠️ | `IMemoryCache` (built into ASP.NET) - 1 hour TTL |

**The only worthwhile case - caching `GetCategoryFieldsAsync`:**

Marketplace API calls take 1-5 seconds. Opening `fieldMappingPage` triggers a new request each time.

Solution without Redis:
```csharp
// Program.cs
builder.Services.AddMemoryCache();

// In CategoryMappingController:
var cacheKey = $"category-fields:{connection.adapterKey}:{externalCategoryId}";
if (!_cache.TryGetValue(cacheKey, out List<ExternalField> fields))
{
    fields = await adapter.GetCategoryFieldsAsync(connection, externalCategoryId, ct);
    _cache.Set(cacheKey, fields, TimeSpan.FromHours(1));
}
```

Add Redis only if: horizontal scaling of the API within an instance (not planned).

---

## 7. Central Portal

> Product scope, Customer vs Ops consoles, RF billing, tickets+Telegram, and open questions:  
> [control-plane.md](control-plane.md). This section remains the technical Registry/Provisioner sketch.

**Stack:** .NET 8 + PostgreSQL + Vue 3. Separate VM (~4 GB RAM).

### Registry DB Schema

```sql
nodes (
  id, name, ip, dokploy_url, dokploy_api_key_encrypted,
  max_instances DEFAULT 20, current_instances DEFAULT 0,
  is_active BOOLEAN, last_health_check TIMESTAMP
)

instances (
  id, node_id FK, subdomain UNIQUE,
  user_id FK, plan_id FK,
  mysql_password_encrypted, minio_key_encrypted,
  jwt_key_encrypted, encryption_key_encrypted, admin_password_encrypted,
  status ENUM('provisioning','active','suspended','terminated'),
  created_at, last_seen_at, last_deployed_version
)

users (id, email, password_hash, current_plan_id FK, subscription_status)
plans (id, name, max_storage_gb, max_products, max_connections, ai_enabled BOOLEAN, price_monthly)
```

### Provisioner Flow

```
Trigger: successful payment
  1. Select node (least-loaded)
  2. Generate secrets (JWT_KEY, MYSQL_PASSWORD, MINIO_ACCESS_KEY, MINIO_SECRET_KEY,
                       ENCRYPTION_KEY, ADMIN_PASSWORD)
  3. Encrypt and store in Registry DB (AES-256)
  4. Dokploy API → create compose project from template with env vars
  5. Poll {subdomain}.your-domain.com/api/health until status=ok (timeout: 5 min)
  6. Send welcome email: URL + admin login + temporary password
  7. instances.status = 'active'
```

### Compose Template for Provisioning

Instance public URL: `https://${SUBDOMAIN}.module.delegat.pro`  
Control Plane: `https://module.delegat.pro`

```yaml
services:
  delegat-api:
    image: ghcr.io/akhilya/delegat-module/api:${IMAGE_TAG}
    environment:
      MYSQL_SERVER: mysql
      MYSQL_DATABASE: delegat
      MYSQL_USER: delegat
      MYSQL_PASSWORD: ${MYSQL_PASSWORD}
      MINIO_ENDPOINT: minio:9000
      MINIO_ACCESS_KEY: ${MINIO_ACCESS_KEY}
      MINIO_SECRET_KEY: ${MINIO_SECRET_KEY}
      MINIO_BUCKET: delegat
      JWT_KEY: ${JWT_KEY}
      ENCRYPTION_KEY: ${ENCRYPTION_KEY}
      CORS_ORIGINS: https://${SUBDOMAIN}.module.delegat.pro
      ADMIN_EMAIL: ${ADMIN_EMAIL}
      ADMIN_USERNAME: admin
      ADMIN_PASSWORD: ${ADMIN_PASSWORD}
      MAX_STORAGE_GB: ${MAX_STORAGE_GB}
      MAX_PRODUCTS: ${MAX_PRODUCTS}
      MAX_CONNECTIONS: ${MAX_CONNECTIONS}
      CONTROL_PLANE_URL: https://module.delegat.pro
      INSTANCE_ID: ${INSTANCE_ID}
      INSTANCE_LINK_SECRET: ${INSTANCE_LINK_SECRET}
      ASPNETCORE_ENVIRONMENT: Production
  mysql:
    image: mysql:8.4
    command: >
      --innodb-buffer-pool-size=64M
      --performance-schema=OFF
      --max-connections=50
```

> Registry DDL выше - черновик; канон v0 (Account/Org + Platform Link secret) живёт в CP monorepo после миграции docs - см. [control-plane-mvp-brief.md](../plans/2026-07-21-control-plane-mvp-brief.md).
### Plan Change Handler

```
Trigger: customer changes plan
  1. Update env vars via Dokploy API:
       MAX_STORAGE_GB, MAX_PRODUCTS, MAX_CONNECTIONS, AI_ENABLED
  2. Trigger redeploy
  3. If downgrade → email customer with current storage usage
  4. If AI_ENABLED: false → AI services/controllers are not registered at startup; frontend does not load the chat panel (restart required to enable)
  5. Update instances.plan_id in Registry DB
```

### Update Manager

```
Trigger: POST /internal/update-all with { imageTag }
  Rolling update for each active instance:
    1. Update image tag in Dokploy project
    2. Trigger redeploy
    3. Poll /api/health (timeout: 3 min)
    4. Fail → rollback to previous tag + alert
    5. Record last_deployed_version
```

### Monitoring Poller

```
Cron: every 5 minutes
  For each active instance:
    GET {subdomain}.your-domain.com/api/health
    → 200: instances.last_seen_at = now()
    → 3 consecutive failures: email/Telegram alert
    → 10 consecutive failures: instances.status = 'degraded'
```

---

## 8. Roadmap

| Stage | Task | Status |
|-------|------|--------|
| 0 | Health check, quotas, admin seed, startup recovery | ✅ Done |
| 1 | MySQL tuning in `docker-compose.prod.yml` | Quick |
| 2 | IMemoryCache for marketplace API | 1-2 days |
| 3 | First-run wizard in Dashboard | Before first customers |
| 4 | Central Portal backend (Registry DB + Provisioner) | 2-3 weeks |
| 5 | Central Portal frontend + billing | 1-2 weeks |
| 6 | Auto-scaling (Hetzner API + cloud-init) | At >1 VM/week growth |

---

## Open Questions

> Broader Control Plane open questions (trial, suspend behavior, docs engine, etc.):  
> [control-plane.md](control-plane.md) §12.

1. **Multi-node DNS:** L4 load balancer (single IP for all nodes) or per-node wildcard?
2. **Payments:** **T-Bank** chosen (see [control-plane.md](control-plane.md)). Remaining: exact API product, recurring, trial card hold.
3. **Email provider:** SendGrid / Postmark / self-hosted SMTP / RF transactional provider?
4. **License key:** offline HMAC protection or closed-source repo is sufficient?
5. **Alerts:** email only or add Telegram bot for monitoring alerts? (Support Telegram bridge: DM to one ops in MVP - see control-plane.md §10.)
