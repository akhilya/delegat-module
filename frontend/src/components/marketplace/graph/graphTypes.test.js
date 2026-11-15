import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  MODIFIER_CATEGORIES,
  OP_SPECS,
  OP_NODE_TYPES,
  PIN_COLORS,
  TYPE_SYNONYMS,
  modifierOutputType,
  typeCompatible,
} from "./graphTypes.js";

const PIN_TYPES = Object.keys(PIN_COLORS);
const EXTRA = ["text"];

describe("typeCompatible", () => {
  it("treats missing or any as compatible", () => {
    for (const t of [...PIN_TYPES, ...EXTRA, "mystery"]) {
      assert.equal(typeCompatible(null, t), true);
      assert.equal(typeCompatible(t, null), true);
      assert.equal(typeCompatible("any", t), true);
      assert.equal(typeCompatible(t, "any"), true);
    }
  });

  it("matches synonym groups", () => {
    const groups = {
      string: ["string", "text", "select"],
      number: ["number", "integer"],
      array: ["array", "multiselect"],
      boolean: ["boolean"],
    };
    for (const members of Object.values(groups)) {
      for (const a of members) {
        for (const b of members) {
          assert.equal(typeCompatible(a, b), true, `${a} ~ ${b}`);
        }
      }
    }
  });

  it("rejects cross-group pairs", () => {
    const reps = ["string", "number", "array", "boolean"];
    for (const a of reps) {
      for (const b of reps) {
        if (a === b) continue;
        assert.equal(typeCompatible(a, b), false, `${a} !~ ${b}`);
      }
    }
  });

  it("full matrix matches TYPE_SYNONYMS", () => {
    const types = [...new Set([...PIN_TYPES, ...Object.keys(TYPE_SYNONYMS)])];
    for (const a of types) {
      for (const b of types) {
        const expected =
          a === "any" ||
          b === "any" ||
          (TYPE_SYNONYMS[a] || a) === (TYPE_SYNONYMS[b] || b);
        assert.equal(typeCompatible(a, b), expected, `${a} vs ${b}`);
      }
    }
  });
});

describe("modifierOutputType", () => {
  it("specials always return string except forceType", () => {
    for (const t of ["toText", "join", "template", "takeFirst"]) {
      assert.equal(modifierOutputType(t, "number"), "string");
      assert.equal(modifierOutputType(t, "array"), "string");
    }
    assert.equal(modifierOutputType("forceType", "boolean"), "boolean");
    assert.equal(modifierOutputType("forceType"), "any");
  });

  it("uses catalog categories", () => {
    for (const [type, category] of Object.entries(MODIFIER_CATEGORIES)) {
      if (["toText", "join", "template", "takeFirst", "forceType"].includes(type)) {
        continue;
      }
      const out = modifierOutputType(type, "select");
      if (category === "number") assert.equal(out, "number", type);
      else if (category === "text") assert.equal(out, "string", type);
      else if (category === "array") assert.equal(out, "multiselect", type);
      else if (category === "select") assert.equal(out, "select", type);
      else assert.equal(out, "select", type);
    }
  });

  it("unknown modifier keeps the input type", () => {
    assert.equal(modifierOutputType("nope", "boolean"), "boolean");
    assert.equal(modifierOutputType("nope"), "any");
  });
});

describe("OP_SPECS", () => {
  it("covers the evaluator op node types", () => {
    const expected = [
      "formatString",
      "join",
      "makeArray",
      "arrayAppend",
      "splitToArray",
      "arrayIndex",
      "arrayLength",
      "ifElse",
      "not",
      "and",
      "or",
      "equals",
      "notEquals",
      "greaterThan",
      "lessThan",
      "add",
      "subtract",
      "multiply",
      "divide",
      "contains",
      "isEmpty",
      "isValid",
    ];
    assert.deepEqual(OP_NODE_TYPES.slice().sort(), expected.slice().sort());
  });

  it("output and accepts match current editor hints (not evaluator)", () => {
    const output = {
      formatString: "string",
      join: "string",
      makeArray: "array",
      arrayAppend: "array",
      splitToArray: "array",
      arrayIndex: "string",
      arrayLength: "number",
      ifElse: "any",
      not: "boolean",
      and: "boolean",
      or: "boolean",
      equals: "boolean",
      notEquals: "boolean",
      greaterThan: "boolean",
      lessThan: "boolean",
      add: "number",
      subtract: "number",
      multiply: "number",
      divide: "number",
      contains: "boolean",
      isEmpty: "boolean",
      isValid: "boolean",
    };
    const accepts = {
      splitToArray: ["string", "text", "select"],
      arrayIndex: ["array", "multiselect"],
      arrayLength: ["array", "multiselect"],
      greaterThan: ["number", "integer"],
      lessThan: ["number", "integer"],
      add: ["number", "integer"],
      subtract: ["number", "integer"],
      multiply: ["number", "integer"],
      divide: ["number", "integer"],
    };
    for (const [type, spec] of Object.entries(OP_SPECS)) {
      assert.equal(spec.output, output[type], type);
      assert.deepEqual(spec.accepts, accepts[type] ?? null, type);
    }
  });
});
