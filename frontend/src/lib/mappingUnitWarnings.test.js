import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  analyzeUnitWarnings,
  unitMismatch,
  resolveConversionUnits,
} from "./mappingUnitWarnings.js";

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

describe("automatic conversion parameters", () => {
  const source = {
    id: "source",
    type: "attribute",
    data: { attributeId: 7, unitCode: "g" },
  };
  const convert = {
    id: "convert",
    type: "modifier",
    data: {
      modifierType: "unitConversion",
      params: { fromUnit: "g", toUnit: "kg" },
    },
  };
  const sink = {
    id: "sink",
    type: "adapterFields",
    data: { fields: [{ fieldKey: "weight", unitCode: "g" }] },
  };
  const edges = [
    { source: "source", target: "convert", targetHandle: "in" },
    { source: "convert", target: "sink", targetHandle: "f:weight" },
  ];
  const sources = { categoryAttributes: [{ id: 7, unitCode: "kg" }] };

  it("refreshes both stale units by default", () => {
    const units = resolveConversionUnits(
      [source, convert, sink],
      edges,
      sources,
    ).get("convert");
    assert.equal(units.fromUnit, "kg");
    assert.equal(units.toUnit, "g");
  });
  it("preserves manual units", () => {
    const manual = {
      ...convert,
      data: {
        ...convert.data,
        params: { ...convert.data.params, autoDetect: false },
      },
    };
    assert.deepEqual(
      resolveConversionUnits([source, manual, sink], edges, sources).get(
        "convert",
      ),
      manual.data.params,
    );
  });
  it("clears stale units when disconnected or removed from the definition", () => {
    const units = resolveConversionUnits([source, convert, sink], edges, {
      categoryAttributes: [{ id: 7, unitCode: null }],
    }).get("convert");
    assert.equal(units.fromUnit, null);
    const disconnected = resolveConversionUnits(
      [source, convert, sink],
      [],
      sources,
    ).get("convert");
    assert.equal(disconnected.fromUnit, null);
    assert.equal(disconnected.toUnit, null);
  });
  it("traces reroutes to the destination", () => {
    const bend = { id: "bend", type: "reroute" };
    const rewired = [
      edges[0],
      { source: "convert", target: "bend", targetHandle: "in" },
      { ...edges[1], source: "bend" },
    ];
    assert.equal(
      resolveConversionUnits(
        [source, convert, sink, bend],
        rewired,
        sources,
      ).get("convert").toUnit,
      "g",
    );
  });
  it("does not guess a unit after arithmetic or across conflicting destinations", () => {
    const divide = {
      id: "divide",
      type: "modifier",
      data: { modifierType: "divide" },
    };
    const rewired = [
      { source: "source", target: "divide", targetHandle: "in" },
      { ...edges[0], source: "divide" },
      edges[1],
    ];
    assert.equal(
      resolveConversionUnits(
        [source, divide, convert, sink],
        rewired,
        sources,
      ).get("convert").fromUnit,
      null,
    );
    const other = {
      ...sink,
      id: "other",
      data: { fields: [{ fieldKey: "weight", unitCode: "kg" }] },
    };
    assert.equal(
      resolveConversionUnits(
        [source, convert, sink, other],
        [...edges, { ...edges[1], target: "other" }],
        sources,
      ).get("convert").toUnit,
      null,
    );
  });
  for (const reverseOrder of [false, true]) {
    it(`keeps the intermediate unit in an automatic chain (reverse=${reverseOrder})`, () => {
      const second = {
        id: "second",
        type: "modifier",
        data: {
          modifierType: "unitConversion",
          params: { fromUnit: "g", toUnit: "kg" },
        },
      };
      const other = {
        ...sink,
        id: "other",
        data: { fields: [{ fieldKey: "weight", unitCode: "kg" }] },
      };
      const nodes = [source, convert, sink, second, other];
      if (reverseOrder) nodes.reverse();
      const resolved = resolveConversionUnits(
        nodes,
        [
          ...edges,
          { source: "convert", target: "second", targetHandle: "in" },
          { source: "second", target: "other", targetHandle: "f:weight" },
        ],
        sources,
      );
      assert.equal(resolved.get("convert").toUnit, "g");
      assert.equal(resolved.get("second").fromUnit, "g");
      assert.equal(resolved.get("second").toUnit, "kg");
    });
  }
  for (const handle of ["p:factor", "field:extra"]) {
    it(`ignores an auxiliary consumer at ${handle}`, () => {
      const consumer = {
        id: "consumer",
        type: "modifier",
        data: { modifierType: "round" },
      };
      const other = {
        ...sink,
        id: "other",
        data: { fields: [{ fieldKey: "weight", unitCode: "kg" }] },
      };
      const resolved = resolveConversionUnits(
        [source, convert, sink, consumer, other],
        [
          ...edges,
          { source: "convert", target: "consumer", targetHandle: handle },
          { source: "consumer", target: "other", targetHandle: "f:weight" },
        ],
        sources,
      );
      assert.equal(resolved.get("convert").toUnit, "g");
    });
  }
  it("takes the input unit of a downstream manual conversion as a constraint", () => {
    const bend = { id: "bend", type: "reroute" };
    const manual = {
      id: "manual",
      type: "modifier",
      data: {
        modifierType: "unitConversion",
        params: { autoDetect: false, fromUnit: "грамм", toUnit: "mg" },
      },
    };
    const resolved = resolveConversionUnits(
      [source, convert, bend, manual],
      [
        edges[0],
        { source: "convert", target: "bend", targetHandle: "in" },
        { source: "bend", target: "manual", targetHandle: "in" },
      ],
      sources,
    );
    assert.equal(resolved.get("convert").toUnit, "g");
    assert.deepEqual(resolved.get("manual"), manual.data.params);
  });
  it("preserves conflicts behind a reroute instead of dropping the ambiguous branch", () => {
    const bend = { id: "bend", type: "reroute" };
    const other = {
      ...sink,
      id: "other",
      data: {
        fields: [
          { fieldKey: "weight", unitCode: "kg" },
          { fieldKey: "grams", unitCode: "g" },
        ],
      },
    };
    const resolved = resolveConversionUnits(
      [source, convert, sink, bend, other],
      [
        ...edges,
        { source: "convert", target: "bend", targetHandle: "in" },
        { source: "bend", target: "other", targetHandle: "f:grams" },
        { source: "bend", target: "other", targetHandle: "f:weight" },
      ],
      sources,
    );
    assert.equal(resolved.get("convert").toUnit, null);
  });
  it("rejects a manual input unit that conflicts with a direct field", () => {
    const manual = {
      id: "manual",
      type: "modifier",
      data: {
        modifierType: "unitConversion",
        params: { autoDetect: false, fromUnit: "kg", toUnit: "mg" },
      },
    };
    const resolved = resolveConversionUnits(
      [source, convert, sink, manual],
      [...edges, { source: "convert", target: "manual", targetHandle: "in" }],
      sources,
    );
    assert.equal(resolved.get("convert").toUnit, null);
  });
  it("ignores operand pins when tracing the main value", () => {
    const round = {
      id: "round",
      type: "modifier",
      data: { modifierType: "round" },
    };
    const rewired = [
      { source: "source", target: "round", targetHandle: "in" },
      { source: "digits", target: "round", targetHandle: "p:decimals" },
      { ...edges[0], source: "round" },
      edges[1],
    ];
    assert.equal(
      resolveConversionUnits(
        [source, round, convert, sink],
        rewired,
        sources,
      ).get("convert").fromUnit,
      "kg",
    );
  });
});
