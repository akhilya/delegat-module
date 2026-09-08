import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  extraValue,
  indexFulfillmentWarehouses,
  orderBuyer,
  orderDelivery,
  orderDiscountAmount,
  orderItemBarcode,
  orderSheetHeadlineFacts,
  orderSheetMetaFacts,
  orderTotalAmount,
  orderTracking,
} from "./orderSheetFacts.js";

describe("orderSheetHeadlineFacts", () => {
  it("puts status, channel, created, and total on their own rows", () => {
    const facts = orderSheetHeadlineFacts({
      status: "In delivery",
      channel: "FBS",
      created: "03.09.2026 09:12",
      total: "1 931,00 ₽",
    });
    assert.deepEqual(
      facts.map((f) => [f.id, f.value]),
      [
        ["status", "In delivery"],
        ["channel", "FBS"],
        ["created", "03.09.2026 09:12"],
        ["total", "1 931,00 ₽"],
      ],
    );
  });

  it("omits empty headline rows", () => {
    assert.deepEqual(
      orderSheetHeadlineFacts({ status: "Packed", channel: "  " }).map(
        (f) => f.id,
      ),
      ["status"],
    );
  });
});

describe("indexFulfillmentWarehouses", () => {
  it("joins a seller warehouse to its office address", () => {
    const map = indexFulfillmentWarehouses([
      {
        externalId: "1104728",
        name: "Склад",
        kind: "seller_warehouse",
        extra: { officeId: 15 },
      },
      {
        externalId: "15",
        name: "Коледино",
        kind: "marketplace_office",
        address: "МО, Коледино",
      },
    ]);
    assert.equal(map["1104728"].name, "Склад");
    assert.equal(map["1104728"].address, "МО, Коледино");
    assert.equal(map["1104728"].officeName, "Коледино");
  });
});

describe("orderSheetMetaFacts", () => {
  it("hides posting when display id matches external id", () => {
    const facts = orderSheetMetaFacts({
      displayId: "55",
      externalId: "55",
      extra: { rid: "eP.abc", warehouseId: 12 },
    });
    assert.deepEqual(
      facts.map((f) => f.id),
      ["rid", "warehouse"],
    );
    assert.equal(facts.find((f) => f.id === "warehouse").value, "12");
  });

  it("shows posting and cabinet ids when they differ", () => {
    const facts = orderSheetMetaFacts({
      displayId: "76728543-0063",
      externalId: "76728543-0063-1",
      extra: { orderId: 99, warehouse: "Хоругвино", deliveryMethod: "ПВЗ" },
    });
    assert.equal(facts.find((f) => f.id === "posting").value, "76728543-0063-1");
    assert.equal(facts.find((f) => f.id === "cabinetOrder").value, "99");
    assert.equal(facts.find((f) => f.id === "warehouse").value, "Хоругвино");
    assert.equal(facts.find((f) => f.id === "deliveryMethod").value, "ПВЗ");
  });

  it("resolves warehouse id to a name when provided", () => {
    const facts = orderSheetMetaFacts(
      { extra: { warehouseId: "7" } },
      { warehouseNameById: { 7: "Коледино" } },
    );
    const row = facts.find((f) => f.id === "warehouse");
    assert.equal(row.value, "Коледино");
    assert.equal(row.detail, "7");
  });

  it("keeps warehouse address and id under a generic name", () => {
    const facts = orderSheetMetaFacts(
      { extra: { warehouse: "Склад", warehouseId: "1104728" } },
      {
        warehouseById: {
          1104728: { name: "Склад", address: "МО, Коледино" },
        },
      },
    );
    const row = facts.find((f) => f.id === "warehouse");
    assert.equal(row.value, "Склад");
    assert.equal(row.detail, "МО, Коледино · 1104728");
  });

  it("uses the office name when the warehouse is only called Склад", () => {
    const facts = orderSheetMetaFacts(
      { extra: { warehouseId: "1104728" } },
      {
        warehouseById: {
          1104728: {
            name: "Склад",
            address: "МО, Коледино",
            officeName: "Коледино",
          },
        },
      },
    );
    const row = facts.find((f) => f.id === "warehouse");
    assert.equal(row.value, "Коледино");
    assert.equal(row.detail, "МО, Коледино · 1104728");
  });

  it("flags a Yandex test order", () => {
    const facts = orderSheetMetaFacts({ extra: { fake: true } });
    assert.equal(facts.find((f) => f.id === "testOrder").value, "true");
  });
});

describe("buyer and delivery", () => {
  it("omits buyer when every field is empty", () => {
    assert.equal(orderBuyer({ customerName: "  " }), null);
  });

  it("keeps shop-style buyer and address", () => {
    const buyer = orderBuyer({
      customerName: "Ivan",
      customerPhone: "+7",
      customerEmail: "a@b.c",
    });
    assert.deepEqual(buyer, {
      name: "Ivan",
      phone: "+7",
      email: "a@b.c",
    });
    assert.equal(
      orderDelivery({ shippingAddress: "Moscow", comment: "leave at door" })
        .address,
      "Moscow",
    );
  });

  it("reads tracking from extra aliases", () => {
    assert.equal(
      orderTracking({ extra: { trackingNumber: "WB-123" } }),
      "WB-123",
    );
    assert.equal(orderTracking({ Extra: { Tracking: "YM-9" } }), "YM-9");
  });
});

describe("money and line barcode", () => {
  it("reads total and discount", () => {
    assert.equal(orderTotalAmount({ total: 199 }), 199);
    assert.equal(orderDiscountAmount({ DiscountTotal: 10 }), 10);
  });

  it("reads item barcode from either casing", () => {
    assert.equal(orderItemBarcode({ barcode: "203540001" }), "203540001");
    assert.equal(orderItemBarcode({ Barcode: "  x  " }), "x");
  });

  it("reads extra scalars from PascalCase bags", () => {
    assert.equal(extraValue({ Extra: { Rid: "r1" } }, "rid"), "r1");
  });
});
