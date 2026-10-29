# Delegat.Module repository instructions

Read this file and the root `MEMORY.md` before substantive work, then use the relevant canonical source below. Do not load the entire documentation tree by default.

## Source of truth

- Implemented behavior: source code and tests. Verify old design notes against them.
- Partner API contract: [`docs/api/public-v1.md`](../docs/api/public-v1.md), kept in sync with `backend/Controllers/PublicApi/` and Swagger.
- Operator MCP / Agent HTTP: [`docs/api/mcp.md`](../docs/api/mcp.md).
- Instance architecture and AI: [`docs/README.md`](../docs/README.md), especially `docs/architecture/`.
- Product/design: [`PRODUCT.md`](../PRODUCT.md), [`DESIGN.md`](../DESIGN.md), and [`DESIGN.json`](../DESIGN.json). `DESIGN.json` is a tooling sidecar, not a competing canon.
- Release/version policy: [`docs/platform/releasing.md`](../docs/platform/releasing.md), root [`VERSION`](../VERSION), and [`CHANGELOG.md`](../CHANGELOG.md).
- Dated plans and their lifecycle: [`docs/plans/README.md`](../docs/plans/README.md).
- Root [`MEMORY.md`](../MEMORY.md) is a compact decision log to read before substantive work. It is not a replacement for maintained references; consult the relevant canonical docs for details.

If sources conflict, verify code and contract tests, then update the canonical reference and label stale plans as superseded. Do not present an old roadmap or security audit as current fact.

## Non-negotiable engineering rules

- Repository-authored code, comments, documentation, and commits are in English; translated UI locale values are the exception.
- Create EF Core migrations only with `dotnet ef migrations add`; never hand-write or edit generated migrations or add raw SQL to migrations. See [`MEMORY.md`](../MEMORY.md) and `backend/Migrations/`.
- Partner-facing behavior changes must update the Public API docs and Swagger/XML contract in the same change.
- User-visible, operational, or security changes update `## [Unreleased]` in `CHANGELOG.md`; follow `docs/platform/releasing.md`.
- Keep credentials out of Git. `.env*`, `.cursor/`, `.playwright-mcp/`, MCP configuration, and generated agent/browser artifacts are local-only; never force-add them.
- Do not implement Control Plane features in this instance repo. Its canonical docs and implementation live in the sibling `delegat-module-platform` repo.
- Prefer small, tested changes that follow existing project patterns.
- Run `npm run check:docs` after changing project documentation, indexes, changelog, adapter inventory, or release metadata. CI runs this gate on pushes and pull requests.
- For UI design, implementation, or review, read the local `.agents/skills/craft-design-engineering/SKILL.md` and the relevant reference before making design decisions. Apply it alongside `DESIGN.md`; existing product tokens and components remain the source of truth.

## API error localization

- Backend API errors must expose a stable, semantic machine-readable `code`; do not rely on a hardcoded server-authored `message`/`title` for user-facing text. Use `ApiException` with `code:` (or the equivalent structured error contract) and avoid forwarding raw exception or marketplace `ErrorMessage` text to API responses. Keep diagnostic details in server logs.
- Frontend API error UI must resolve the message from the error code through the locale catalog (`errors.<code>`; `apiErrors.<code>` only for existing compatibility paths). Never display response `message`/`title` or `Error.message` as an API-error fallback. Preserve structured `data` only for deliberate localized interpolation parameters.
- Add the code's user-facing text to both `frontend/src/locales/en.json` and `frontend/src/locales/ru.json`, and extend the locale-code coverage test in `frontend/src/lib/i18nMessages.test.js` whenever adding a code.
- Keep existing public API error codes stable. If a partner-facing code or response contract must change, update `docs/api/public-v1.md` and the corresponding Swagger/XML documentation in the same change.
