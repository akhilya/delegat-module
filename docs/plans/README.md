# Design and implementation records

This directory contains dated design decisions, implementation plans, and completion notes. It is a history, **not** the current product or architecture spec. Check the status and the linked current reference before using any record as implementation guidance.

## In progress

- [Marketplace credential broker for the AI sandbox](2026-10-27-marketplace-credential-broker.md) — read-only broker and SDK integration are implemented behind an opt-in; compatibility fixtures and per-store canaries remain required before rollout. No production store is enabled.

## Implemented designs and validated interactions

- [Stored orders and overview tabs](2026-09-04-stored-orders-overview-tabs-design.md) — current order overview design; revised 2026-10-11. Its concise shipped note is [here](2026-09-04-stored-orders-overview-tabs.md).
- [Fulfillment, shipments, and boxes](2026-09-04-fulfillment-shipments-design.md) — **implemented**; current API reference is [MCP/Agent HTTP](../api/mcp.md) and [Public API v1](../api/public-v1.md).
- [Order sheet facts](2026-09-04-order-sheet-facts-design.md) — UI detail design; verify against current UI.
- [Agent and MCP design](2026-09-06-agent-mcp-design.md) — original design with 2026-09-08 amendment; current implementation reference is [AI system](../architecture/ai-system.md), [AI maintenance](../architecture/ai-assistant.md), and [MCP API](../api/mcp.md).
- [Assistant questionnaire](2026-09-09-assistant-questionnaire-design.md) and [assistant reply UI](2026-09-09-assistant-reply-ui-design.md) — validated interaction designs; verify implementation before changing behavior.

## Implemented records — historical, do not re-implement

- [Product soft delete](2026-03-17-product-soft-delete.md) — **implemented**; see `ProductController`, `Product`, and the trash UI. The file preserves the original plan as history; do not copy its old examples.
- [Public API v1 design](2026-07-17-public-api-v1-design.md) — **implemented and extended**; the current contract is [docs/api/public-v1.md](../api/public-v1.md).
- [Workspace view](2026-08-25-workspace-view-design.md) — **implemented**; verify against permissions and tests.
- [API unavailable page](2026-07-22-api-unavailable-page-design.md) — **implemented**.
- [Platform test catalog](2026-07-18-platform-test-catalog.md) — **historical test-count snapshot** from 2026-07-18; test counts and named legacy components are stale.

## Superseded and external-project stubs

- [Orders assembly overview](2026-09-04-orders-assembly-overview-design.md) — **partially superseded** by the stored-orders design; mail-style groups and connection mailboxes remain relevant.
- [Control Plane MVP brief](2026-07-21-control-plane-mvp-brief.md) — pointer to the sibling repo; not an instance plan.
- [Control Plane init prompt](2026-07-21-control-plane-init-prompt.md) — historical stub; scaffolding is complete in the sibling repo.

## Maintenance

- Mark each record `Implemented`, `Superseded`, `Historical snapshot`, or `Open design` when its status is known.
- When a design is implemented, link its current reference and concise completion note; keep detailed history only when it explains a still-valid decision.
- Do not duplicate evolving endpoint contracts, feature inventories, or release notes in dated plans.
- For a conflict, verify implementation and tests, update the canonical current doc, and retain the historical record with a clear supersession note.
