# Order sheet facts

Cabinet order sheet is one scroll (no tabs). Empty groups are omitted.

1. Header: display id only.
2. Details: status, channel, created, total; then posting vs cabinet id, rid, warehouse (name, address, id), delivery method, discount, Yandex test flag. Copyable ids. Marketplace-owned delivery is its own note, not a header caption.
3. Buyer: name / phone / email (Woo, OpenCart, Yandex; often absent on Ozon/WB).
4. Delivery: address, comment, tracking.
5. Fulfillment: WB supply and box when known. Pack confirm still assigns TRBX.
6. Items: photo, name, SKU, barcode, qty, price.

Known `extra` keys only (no raw dump). Additive `ExternalOrderItem.Barcode` (WB `skus`). Warehouse from order extra (`warehouse` / `warehouseId`) or fulfillment locations; WB seller warehouses join the drop-off office name and address. Ozon FBS uses `delivery_method.warehouse` when analytics has no warehouse name.
