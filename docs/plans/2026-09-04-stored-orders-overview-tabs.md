# Stored orders and overview tabs

> **Completion note.** The detailed design and current API references are linked below; this file is a concise shipped summary.

Shipped. Design: [`2026-09-04-stored-orders-overview-tabs-design.md`](2026-09-04-stored-orders-overview-tabs-design.md).

Cabinet lists and Public API GET read `MarketplaceOrder` snapshots. Recent orders are pulled about every 30 seconds, and about every 20 seconds while a cabinet orders screen is open. A fuller scan runs about every 5 minutes. `/orders` has four store-wide tabs; connection mailboxes stay at `/orders/:connectionId/:mailbox`. Seller status, labels, and shipments stay live.
