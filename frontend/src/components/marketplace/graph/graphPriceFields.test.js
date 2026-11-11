import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  priceFieldGroupCount,
  samePriceFieldSchema,
  syncAdapterPriceFields,
} from "./graphPriceFields.js";

describe("price-field schema sync", () => {
  it("refreshes metadata, schema order, and groups without replacing bindings", () => {
    const nodes = [
      {
        id: "adapter-price-fields",
        type: "adapterPriceFields",
        data: {
          fields: [
            {
              fieldKey: "price",
              label: "Stale label",
              outputType: "string",
              group: "Old group",
              source: { type: "productField", field: "price" },
            },
            {
              fieldKey: "discountedPrice",
              label: "Extended price",
              outputType: "number",
            },
          ],
        },
      },
    ];

    syncAdapterPriceFields(nodes, [
      {
        externalFieldKey: "minPrice",
        externalFieldName: "Minimum price",
        externalFieldType: "number",
        group: "Price strategy",
        description: "Promotion floor",
        sortOrder: 1,
      },
      {
        externalFieldKey: "price",
        externalFieldName: "Price",
        externalFieldType: "number",
        group: "Prices",
        description: "Current price",
        sortOrder: 0,
        isRequired: true,
        externalFieldUnit: "kilogram",
        externalFieldUnitCode: "kg",
        externalFieldUnitId: "701",
      },
    ]);

    const fields = nodes[0].data.fields;
    assert.deepEqual(
      fields.map((field) => field.fieldKey),
      ["price", "minPrice"],
    );
    assert.equal(fields[0].label, "Price");
    assert.equal(fields[0].outputType, "number");
    assert.equal(fields[0].isRequired, true);
    assert.equal(fields[0].group, "Prices");
    assert.equal(fields[0].description, "Current price");
    assert.equal(fields[0].unit, "kilogram");
    assert.equal(fields[0].unitCode, "kg");
    assert.equal(fields[0].unitId, "701");
    assert.deepEqual(fields[0].source, {
      type: "productField",
      field: "price",
    });
    assert.equal(fields[1].group, "Price strategy");
  });

  it("counts visible group headers, including separate repeated group runs", () => {
    assert.equal(
      priceFieldGroupCount([
        { group: "Prices" },
        { group: "Prices" },
        { group: "Price strategy" },
        { group: "Prices" },
      ]),
      3,
    );
    assert.equal(priceFieldGroupCount([{ fieldKey: "price" }]), 0);
  });

  it("detects changes to displayed and schema metadata", () => {
    const current = [
      {
        fieldKey: "price",
        label: "Price",
        outputType: "number",
        group: "Prices",
      },
    ];
    const same = [
      {
        fieldKey: "price",
        label: "Price",
        outputType: "number",
        group: "Prices",
      },
    ];
    const changed = [
      {
        fieldKey: "price",
        label: "Updated price",
        outputType: "number",
        group: "Price values",
      },
    ];
    const changedUnit = [{ ...current[0], unitCode: "g" }];

    assert.equal(samePriceFieldSchema(current, same), true);
    assert.equal(samePriceFieldSchema(current, changed), false);
    assert.equal(samePriceFieldSchema(current, changedUnit), false);
  });
});
