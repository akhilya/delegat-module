# Fulfillment: warehouses, supplies, boxes

Locked 2026-09-04. English. Pass-through only — no EF tables, no stored supplies.

## Problem

Wildberries FBS is not one order → pack → ship. A **supply** (`WB-GI-…`) groups assembly tasks, **offices** are drop-off places, and **boxes** (`WB-TRBX-…`) are shipping units with their own QR stickers. Pickup-point supplies must be boxed; warehouse drop-off may skip boxes.

The adapter already created a nameless supply on every pack and hid the rest of the API. There is no new cabinet page: supply, office, and box live on the **order sheet** pack/ship confirms.

## Capabilities

Host [`MarketplaceCapabilities`](../../backend/Services/Marketplace/MarketplaceCapabilities.cs) stays bit-identical to the SDK enum.

- `GetFulfillmentLocations` (`1 << 25`) — list places.
- `ManageShipments` (`1 << 26`) — supplies, boxes, labels. Deliver stays `POST …/orders/{id}/status` `{ action: ship, extra.supplyId }`.

Wildberries: both flags. Ozon: locations only (`seller_warehouse`). Yandex / Woo / OpenCart: off (`400 adapter_capability_missing`).

## DTOs (marketplace-neutral)

[`sdk/Delegat.Marketplace.SDK/DTOs/Fulfillment.cs`](../../sdk/Delegat.Marketplace.SDK/DTOs/Fulfillment.cs)

- Location kinds: `seller_warehouse` | `marketplace_office`.
- Shipment status: `open` | `done`.
- `ExternalFulfillmentLocation`, `ExternalShipment`, `ExternalShipmentBox`.
- Labels reuse `OrderLabel`.

## Wildberries mapping

| Platform | WB |
|---|---|
| Locations `seller_warehouse` | `GET /api/v3/warehouses` |
| Locations `marketplace_office` | `GET /api/v3/offices` |
| List/create shipment | `GET/POST /api/v3/supplies` |
| Orders in shipment | `GET …/order-ids`; add `PATCH …/supplies/{id}/orders` |
| Boxes | `GET/POST /api/v3/supplies/{id}/trbx` (`amount` → `WB-TRBX-…`) |
| Assign orders to a box | `PATCH …/trbx/{trbxId}` `{ orderIds }` (OpenAPI lists GET/POST/DELETE trbx; assign is the documented seller-cabinet path) |
| Box sticker | `POST …/trbx/stickers?type=png` (only if the box has orders) |
| Supply QR | `GET …/barcode` **after** deliver |
| Pack | Add the order to the chosen/created open supply; optionally create one box and assign this order |
| Ship | `PATCH …/supplies/{id}/deliver` — **whole supply** |

`recommendedWhId` / `destinationOfficeId` are display + picker defaults. There is no official write for destination on create (`POST /supplies` is `{ name }` only) — do not invent one.

Pack still creates a supply when `extra.supplyId` is omitted (Public API callers). The sheet creates or picks a supply first, then packs with `extra.supplyId` (+ `boxId` / `createBox`). Duplicate add (`409`) is ignored when the order is already in that supply.

## Host HTTP

JWT [`MarketplaceCommerceController`](../../backend/Controllers/MarketplaceCommerceController.cs) and Public API [`PublicMarketplaceCommerceController`](../../backend/Controllers/PublicApi/PublicMarketplaceCommerceController.cs), permission `orders.manage`:

- `GET …/fulfillment/locations?kind=`
- `GET/POST …/shipments`
- `GET …/shipments/{shipmentId}`
- `POST …/shipments/{shipmentId}/orders`
- `GET/POST …/shipments/{shipmentId}/boxes`
- `POST …/shipments/{shipmentId}/boxes/{boxId}/orders`
- `GET …/shipments/{shipmentId}/boxes/{boxId}/label`
- `GET …/shipments/{shipmentId}/label`

Failures: `400 shipment_failed` / `400 fulfillment_location_failed`. Missing shipment → `404 shipment_not_found`.

## Order sheet

No new route. [`OrderDetailSheet.vue`](../../frontend/src/components/orders/OrderDetailSheet.vue) gated with `adapterCanManageShipments`.

- Assembly pack confirm: open supply (default new), read-only drop-off office from the supply, box (none / create / existing). Then pack with `extra.supplyId` (+ box).
- Ready to ship: show supply + box from `order.extra`; order sticker and box barcode when boxed. Ship copy: handing over the whole supply.
- Delivering: optional supply QR (`GetShipmentLabel`); hidden when the adapter cannot print it.

## Out of scope

New supplies mailbox, creating seller warehouses, acceptance coefficients / timeslots, pass / driver QR beyond the supply barcode after deliver, Ozon multi-package postings, Yandex order boxes.
