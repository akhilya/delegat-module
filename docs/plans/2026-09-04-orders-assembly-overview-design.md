# Orders assembly overview

Locked 2026-09-04. English. Mail layout still holds. Pass-through and “assembly-only, no tabs” are superseded by [`2026-09-04-stored-orders-overview-tabs-design.md`](2026-09-04-stored-orders-overview-tabs-design.md).

## Problem

Mailbox pages are one connection and one status. Packing a store that sells on several marketplaces meant hopping Ozon → Wildberries → Yandex. Operators need one screen of everything waiting to pack or already in assembly.

## Page

Route: `/orders`. Sidebar first Orders item: **To pack** (`nav.ordersAssembly`). Connection rows still open `/orders/:connectionId/:mailbox`.

Content: new + assembly only (`new` / `unpaid` / `confirmed` / `awaiting_pack`). Ready to ship, delivery, done, and closed stay on mailboxes.

Groups: one `InsetGroup` + `Collapsible` per GetOrders connection (Operations pattern, several open at once). Default open when the group has rows. Header: adapter icon, connection label, waiting/packing caption, count, chevron. Body: Mail-style inset cells (overlapping thumbs, first product name, order id, amount; muted status). Extra lines are “{id} · n more”. Line items stay on the sheet. Tap opens the existing order sheet (`?order=&connectionId=`).

Not a table. No mailbox tabs on this page. Search by order number looks up every connection; a hit that is not assembly navigates to that mailbox.

## Out of scope

Warehouse grouping, bulk actions, supplies mailbox, FBO-only queues.
