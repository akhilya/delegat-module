# Delegat.Module instance — project memory

This is a **short decision log**, not an implementation diary or a duplicate product manual. The repository documentation index is [`docs/README.md`](docs/README.md); agent entry point is [`.github/copilot-instructions.md`](.github/copilot-instructions.md). If a human explicitly changes a durable decision, update the owning canonical document and this file only when the decision itself belongs here.

## Product and repository boundaries

- Delegat.Module is an isolated per-customer product catalog and marketplace operations instance.
- This repository is the instance (`akhilya/delegat-module`). The sibling `delegat-module-platform` repository owns Control Plane product, billing, provisioning, Ops, and the Platform Link server/contract. Do not implement Control Plane features here.
- Durable product/design canon: [`PRODUCT.md`](PRODUCT.md), [`DESIGN.md`](DESIGN.md), and [`DESIGN.json`](DESIGN.json). The JSON is a tooling sidecar, not another source of truth.
- Repository-authored code, comments, docs, and commits are English; translated UI locale values are the exception.

## Non-negotiable change rules

1. **EF Core migrations:** change the model, then generate migrations with `dotnet ef migrations add <Name> --output-dir Migrations` from `backend/`. Keep the generated migration and Designer pair plus snapshot. Never hand-write/edit generated migrations, omit the `[Migration]` attribute, add raw SQL to migrations, or edit an applied migration.
2. **Changelog:** user-visible, operational, breaking, and security changes belong under `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md). Release procedure and canonical version source (`VERSION`) are in [`docs/platform/releasing.md`](docs/platform/releasing.md). Do not use the changelog as an implementation diary.
3. **Partner API:** changes to partner-facing behavior must update `backend/Controllers/PublicApi/`, [`docs/api/public-v1.md`](docs/api/public-v1.md), and Swagger/XML contract documentation in the same change. Prefer additive `/api/v1` changes.
4. **Secrets and local tools:** never commit credentials. `.env*`, `.cursor/`, MCP configs, `.playwright-mcp/`, generated browser traces, and assistant caches are local-only. Do not force-add ignored paths. Shared project-specific agent instructions belong in `.github/copilot-instructions.md`; track that file when it is intended to travel with the repository.
5. **Control Plane:** its canonical docs and code are in the sibling repository. Instance-side Platform Link client notes are in [`docs/platform/platform-link.md`](docs/platform/platform-link.md).
6. **User-authorized instance testing:** never run product export to any already configured marketplace adapter. Other tests, including imports, are permitted only against the user's designated test instance.
7. **Agent test history:** run agent-chat tests as the user's existing admin identity and persist the conversation so it appears in that account's history; do not create a synthetic test user.

## Design and implementation references

- Apply [`DESIGN.md`](DESIGN.md) for visual decisions; use existing shared components and tokens rather than introducing local variants.
- Current instance capabilities and proposed roadmap are in [`docs/platform/capabilities.md`](docs/platform/capabilities.md); verify roadmap claims against code and tests.
- Current AI architecture, prompt/tool contract, and operator API live in [`docs/architecture/ai-system.md`](docs/architecture/ai-system.md), [`docs/architecture/ai-assistant.md`](docs/architecture/ai-assistant.md), and [`docs/api/mcp.md`](docs/api/mcp.md).
- Product-file bytes stay outside MCP/model JSON: the download tool issues a 10-minute private Agent HTTP ticket with store/user checks. Cabinet chat reads staged attachments through bounded extraction, not server-path searches; a nonzero Python exit is a failed plan step. Connected HTTP MCP remains a separate opt-in surface and requires an exact instance tool allowlist; [`docs/api/mcp.md`](docs/api/mcp.md) owns the contract.
- AI performance traces use an optional, manual Sentry core SDK integration; `SENTRY_DSN` is unset by default, and no request or model content is exported. See [`docs/ai/operator-playbook.md`](docs/ai/operator-playbook.md).
- Authentication details live in [`docs/architecture/authentication.md`](docs/architecture/authentication.md).
- Dated designs under `docs/plans/` are historical records, not instructions to reimplement. Check [`docs/plans/README.md`](docs/plans/README.md) for their status.
- Security audit files are dated snapshots; revalidate claims against current code before treating them as findings.

## Stack pointers

| Area | Canonical location |
|---|---|
| Backend | ASP.NET Core 8, EF Core, Pomelo MySQL, MinIO; SDK pin in [`global.json`](global.json) |
| Frontend | Vue 3, Vite, shadcn-vue; shared icon aliases in `frontend/src/lib/icons.js` |
| Database migrations | `backend/Migrations/`; baseline `20260731183033_Baseline`, then additive EF-generated migrations |
| Partner API | `/api/v1`; [`docs/api/public-v1.md`](docs/api/public-v1.md) |
| Operator MCP / Agent HTTP | `POST /api/mcp` and `/api/agent`; [`docs/api/mcp.md`](docs/api/mcp.md) |
