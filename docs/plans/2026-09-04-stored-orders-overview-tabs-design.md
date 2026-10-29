# Stored orders and overview tabs

> **Implemented design record.** Current partner-facing behavior is in [`docs/api/public-v1.md`](../api/public-v1.md); implementation details here are historical context.

Locked 2026-09-04. Revised 2026-10-11 for order freshness. English. Supersedes the pass-through / “assembly-only, no tabs” parts of [`2026-09-04-orders-assembly-overview-design.md`](2026-09-04-orders-assembly-overview-design.md). Mail-style groups and connection sidebar rows stay.

## Problem

`/orders` is only the packing queue. Operators want the same all-adapters screen for the rest of the pipeline. Live pass-through also means an order vanishes from the cabinet if the marketplace list no longer returns it.

## Operator UI

`/orders` navbar tabs (all GetOrders connections, grouped as today):

| Tab (ru / en key) | Mailbox ids | Statuses |
| --- | --- | --- |
| К сборке (`assembly`) | `new` + `pack` | `new`, `unpaid`, `confirmed`, `awaiting_pack` |
| Собираются (`ship`) | `ship` | `awaiting_deliver` |
| В доставке (`delivery`) | `delivery` | `delivering` |
| Доставлены (`done`) | `done` | `delivered` |

URL: `/orders?tab=assembly|ship|delivery|done`. Default `assembly`. Do **not** use `/orders/ship` (legacy mailbox redirect).

Sidebar unchanged: first item **To pack** → `/orders`; then each connection’s mailboxes (`/orders/:connectionId/:bucket`), including Closed. Cancelled / returned are not overview tabs.

Woo / OpenCart have no ship or delivery mailbox: those connections contribute no rows to Собираются / В доставке.

Same Mail inset groups, sheet, context menu, photo size. Search that hits a non-active-tab status still jumps to that connection mailbox (or Closed).

## Snapshot

New EF entity (migration **only** via `dotnet ef migrations add`). Unique `(StoreId, ConnectionId, MarketplaceOrderId)`. Indexed `(StoreId, Status)`, `(ConnectionId, Status)`, display id.

Row holds the full `ExternalOrder` JSON (header, lines, extra) plus denormalized list columns (`Status`, `Channel`, `CreatedAt`, `Total`, `DisplayId`, `LastSyncedAt`). Keep forever; cascade delete with the connection / store. No historical backfill job — only orders seen after this ships.

Labels, supplies, and shipment mutations stay live. Not stored.

## Sync

Silent `BackgroundService` (not an Operations job). Every active GetOrders connection:

- **Recent pull** (open cabinet screen, and the background cycle while a full scan is cooling down): first page of each in-flight status (`new` … `delivering`) over the last 2 days.
- **Full scan:** in-flight statuses paginate until exhausted. Terminal statuses (`delivered`, `cancelled`, `returned`) are the first page only, plus `GetOrder` for stored in-flight ids the list did not return.

Per `(connectionId, status)` last-pull timestamp. A recent pull and a full scan use different rows, so a full scan does not block the next recent pull.

- Cabinet orders screens paint from the snapshot, then `POST` sync. While the screen is open they pull again about every **20 seconds**. That pull is the first page of each requested status over the last **2 days**, and it is skipped only when that slice was pulled under **15 seconds** ago.
- Background cycle: **30 seconds**. When the full scan is still cooling down, the same recent pull runs for in-flight statuses. Full pagination of each status, plus `GetOrder` for stored in-flight ids the list did not return, runs when that slice is older than **5 minutes**.

Pull errors leave the last snapshot. No wipe.

Seller status actions still go to the marketplace; upsert the returned order (or re-get).

## API

Cabinet:

- `GET /marketplace/orders?tab=&search=&page=` — store-wide snapshot for an overview tab.
- Existing `GET /marketplace/connections/{id}/orders` and `GET …/orders/{orderId}` — same JSON, from DB.
- `POST …/sync` (store tab or connection + statuses) — throttled pull; `{ pulled, skipped }`.

Public `/api/v1/stores/{storeId}/connections/{connectionId}/orders` reads the snapshot. URL and page shape stay; additive `syncedAt`. No partner-triggered live pull (background + cabinet cover freshness). Document as eventually consistent.

Search: DB on display / external id first; miss → live `GetOrder` (existing Ozon `…-1` retry), upsert, then navigate.

## Out of scope

Backfill of cabinet history, order row TTL / `MAX_ORDERS`, FBO-only queues, warehouse grouping, storing labels or WB supplies, Public API live refresh.
