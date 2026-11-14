import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { analyzeUnitWarnings, unitMismatch } from "./mappingUnitWarnings.js";

describe("mapping unit warnings", () => {
  it("does not warn when known aliases resolve to the same unit", () => {
    assert.equal(unitMismatch("килограмм", "kg"), null);
  });

  it("warns on the adapter wire when numeric source and target units differ", () => {
    const result = analyzeUnitWarnings(
      [
        { id: "attribute-1", type: "attribute", data: { attributeId: 7 } },
        { id: "modifier-1", type: "modifier", data: {} },
        {
          id: "adapter-fields",
          type: "adapterFields",
          data: { fields: [{ fieldKey: "width", unitCode: "cm" }] },
        },
      ],
      [
        { id: "source-wire", source: "attribute-1", target: "modifier-1" },
        {
          id: "sink-wire",
          source: "modifier-1",
          target: "adapter-fields",
          targetHandle: "f:width",
        },
      ],
      { categoryAttributes: [{ id: 7, unitCode: "m" }] },
    );

    assert.equal(result.edgeWarnings.has("source-wire"), false);
    assert.equal(result.edgeWarnings.get("sink-wire")?.kind, "different");
    assert.equal(result.edgeWarnings.get("sink-wire")?.sourceUnit, "m");
    assert.equal(result.edgeWarnings.get("sink-wire")?.targetUnit, "cm");
  });

  it("keeps direct card and price warnings separate for identical field keys", () => {
    const result = analyzeUnitWarnings(
      [
        {
          id: "adapter-fields",
          type: "adapterFields",
          data: {
            fields: [
              {
                fieldKey: "amount",
                unitCode: "cm",
                source: { type: "attribute", attributeId: 7 },
              },
            ],
          },
        },
        {
          id: "adapter-price-fields",
          type: "adapterPriceFields",
          data: {
            fields: [
              {
                fieldKey: "amount",
                unitCode: "kg",
                source: { type: "storeAttribute", attributeId: 9 },
              },
            ],
          },
        },
      ],
      [],
      {
        categoryAttributes: [{ id: 7, unitCode: "m" }],
        storeAttributes: [{ id: 9, unitCode: "g" }],
      },
    );

    assert.equal(
      result.directWarnings["adapter-fields"].amount?.sourceUnit,
      "m",
    );
    assert.equal(
      result.directWarnings["adapter-price-fields"].amount?.sourceUnit,
      "g",
    );
  });

  it("treats a conversion block as changing the unit flowing to the sink", () => {
    const result = analyzeUnitWarnings(
      [
        { id: "source", type: "attribute", data: { attributeId: 7 } },
        {
          id: "convert",
          type: "modifier",
          data: {
            modifierType: "unitConversion",
            params: { fromUnit: "kg", toUnit: "g" },
          },
        },
        {
          id: "adapter-fields",
          type: "adapterFields",
          data: { fields: [{ fieldKey: "weight", unitCode: "g" }] },
        },
      ],
      [
        { id: "source-wire", source: "source", target: "convert" },
        {
          id: "sink-wire",
          source: "convert",
          target: "adapter-fields",
          targetHandle: "f:weight",
        },
      ],
      { categoryAttributes: [{ id: 7, unitCode: "kg" }] },
    );

    assert.equal(result.edgeWarnings.size, 0);
  });
});

describe("mapping unit warnings (definitions and conversions)", () => {
  it("treats the same unit written differently as a match", () => {
    assert.equal(unitMismatch("mm", "Миллиметр"), null);
    assert.equal(unitMismatch("мм", "mm"), null);
  });

  it("prefers the attribute definition unit over a stale block unit", () => {
    const result = analyzeUnitWarnings(
      [
        {
          id: "attribute-1",
          type: "attribute",
          data: { attributeId: 7, unitCode: "cm" },
        },
        {
          id: "adapter-fields",
          type: "adapterFields",
          data: { fields: [{ fieldKey: "height", unitCode: "mm" }] },
        },
      ],
      [
        {
          id: "wire",
          source: "attribute-1",
          target: "adapter-fields",
          targetHandle: "f:height",
        },
      ],
      { categoryAttributes: [{ id: 7, unitCode: "mm" }] },
    );
    assert.equal(result.edgeWarnings.size, 0);
  });

  it("does not carry the source unit through arithmetic modifiers", () => {
    const result = analyzeUnitWarnings(
      [
        { id: "attribute-1", type: "attribute", data: { attributeId: 7 } },
        {
          id: "divide",
          type: "modifier",
          data: { modifierType: "divide", params: { divisor: 10 } },
        },
        {
          id: "adapter-fields",
          type: "adapterFields",
          data: { fields: [{ fieldKey: "height", unitCode: "cm" }] },
        },
      ],
      [
        { id: "w1", source: "attribute-1", target: "divide" },
        {
          id: "w2",
          source: "divide",
          target: "adapter-fields",
          targetHandle: "f:height",
        },
      ],
      { categoryAttributes: [{ id: 7, unitCode: "mm" }] },
    );
    assert.equal(result.edgeWarnings.size, 0);
  });

  it("marks convertible warnings with where to insert the conversion", () => {
    const result = analyzeUnitWarnings(
      [
        {
          id: "adapter-fields",
          type: "adapterFields",
          data: {
            fields: [
              {
                fieldKey: "height",
                unitCode: "cm",
                source: { type: "attribute", attributeId: 7 },
              },
            ],
          },
        },
      ],
      [],
      { categoryAttributes: [{ id: 7, unitCode: "mm" }] },
    );
    const warning = result.directWarnings["adapter-fields"].height;
    assert.equal(warning.convertible, true);
    assert.equal(warning.sinkId, "adapter-fields");
    assert.equal(warning.fieldKey, "height");
    assert.equal(unitMismatch("mm", "kg").convertible, false);
  });
});
