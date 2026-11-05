# Documentation map

This repository documents the **Delegat.Module instance**. The Control Plane has its own canonical documentation in the sibling `delegat-module-platform` repository; this repo must not become a second source of truth for it.

## Which document wins?

1. **Implemented behavior:** code and API contract tests.
2. **Partner contract:** [Public API v1](api/public-v1.md); keep it in sync with `backend/Controllers/PublicApi/` and Swagger.
3. **Instance architecture and operator API:** the current docs below. `docs/plans/` is a dated design/history record, not a competing architecture canon.
4. **Product and design:** root [PRODUCT.md](../PRODUCT.md), [DESIGN.md](../DESIGN.md), and [DESIGN.json](../DESIGN.json).
5. **Agent workflow:** [.github/copilot-instructions.md](../.github/copilot-instructions.md) is the project-specific entry point. Read root [MEMORY.md](../MEMORY.md) before substantive work; it is a compact decision log, not a changelog or a substitute for the docs.

If sources disagree, trust verified implementation and its contract tests; update the canonical doc and mark the stale design/history entry as superseded. Never silently treat an old plan, roadmap, or audit as current behavior.

## Product and design

- [Product purpose and audience](../PRODUCT.md)
- [Visual design system](../DESIGN.md)
- [Design tooling sidecar](../DESIGN.json) — structured companion to `DESIGN.md`, not an independent design canon
- [Instance UX brief](platform/product.md) — supplementary context; root `PRODUCT.md` wins on conflicts

## Current instance reference

- [Capabilities and roadmap](platform/capabilities.md) — feature overview; roadmap items are proposals and require verification
- [Releasing](platform/releasing.md) — `VERSION`, `CHANGELOG.md`, SemVer, release process
- [SaaS distribution architecture](architecture/saas-architecture.md) — **technical draft**; provisioning and Control Plane product decisions are maintained in the sibling repo
- [Platform Link](platform/platform-link.md) — instance client notes; Control Plane contract is maintained in the sibling repo
- [Control Plane stub](architecture/control-plane.md) — pointer only

## API and integrations

- [Partner Public API v1](api/public-v1.md) — external REST contract
- [Operator MCP and Agent HTTP](api/mcp.md) — tools for operators and MCP clients; not partner API
- [OpenCart bridge](adapters/opencart.md)
- [WooCommerce bridge](adapters/woocommerce.md)
- [Avito adapter](adapters/avito.md) — experimental; listing import and price operations, no listing publication

## AI assistant reference

- [AI architecture](architecture/ai-system.md)
- [AI maintenance and prompt/tool contract](architecture/ai-assistant.md)
- [Assistant quality evaluation](ai/assistant-quality-evaluation.md) — offline fixtures, isolated live-model findings, and comparison gates
- [Assistant production rollout](ai/production-rollout.md) — release, instance-local embeddings, activation checks, and rollback
- [AI-agent quality and efficiency audit](architecture/ai-agent-audit.md) — snapshot dated 2026-10-28; remaining work is listed in the document
- [Authentication implementation notes](architecture/authentication.md)
- [Operator playbook](ai/operator-playbook.md) — indexed operational procedures; keep aligned with API and implementation
- [Adapter API watch prompt](ai/adapter-api-watch.md) and [adapter catalog](ai/adapter-api-watch.json) — bot inputs; update together when adapters change

## Plans and implementation history

See [plans/README.md](plans/README.md) for status, supersession, and historical records. A plan marked implemented or superseded is not an instruction to re-implement it.

## Security review snapshots

- [Backend security audit (2026-07-17)](security/SECURITY_AUDIT.md)
- [Storage/plugin security audit (2026-07-17)](security/SECURITY_AUDIT_REPORT.md)

These are dated review artifacts, **not verified current findings**. Check the current code and tests before treating any finding or mitigation as applicable.
