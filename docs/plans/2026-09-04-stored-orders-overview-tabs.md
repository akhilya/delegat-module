# Stored orders and overview tabs

Shipped. Design: [`2026-09-04-stored-orders-overview-tabs-design.md`](2026-09-04-stored-orders-overview-tabs-design.md).

Cabinet lists and Public API GET read `MarketplaceOrder` snapshots. Sync is `MarketplaceOrderSyncService` plus `OrderSyncBackgroundService`. `/orders` has four store-wide tabs; connection mailboxes stay at `/orders/:connectionId/:mailbox`. Seller status, labels, and shipments stay live.
