import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  getAttributeUnit,
  getUnitConversion,
  normalizeUnitCode,
} from "./attributeUnits.js";

describe("attributeUnits", () => {
  it("normalizes common aliases to shared unit codes", () => {
    assert.equal(normalizeUnitCode(" KG "), "kg");
    assert.equal(normalizeUnitCode("сантиметр"), "cm");
    assert.equal(normalizeUnitCode("Н·м"), "n_m");
    assert.equal(normalizeUnitCode("μm"), "um");
    assert.equal(getAttributeUnit("кг")?.dimension, "mass");
  });

  it("preserves unknown marketplace unit codes", () => {
    assert.equal(normalizeUnitCode("vendor:unit-42"), "vendor:unit-42");
    assert.equal(getAttributeUnit("vendor:unit-42"), null);
  });

  it("converts only known units in the same dimension", () => {
    assert.deepEqual(getUnitConversion("kg", "g"), { scale: 1000, offset: 0 });
    assert.deepEqual(getUnitConversion("m", "cm"), { scale: 100, offset: 0 });
    assert.equal(getUnitConversion("kg", "cm"), null);
    assert.equal(getUnitConversion("month", "day"), null);
    assert.equal(getUnitConversion("vendor:a", "vendor:b"), null);
    assert.equal(getUnitConversion("vendor:a", "vendor:a"), null);
  });

  it("supports affine temperature conversion", () => {
    const conversion = getUnitConversion("f", "c");
    assert.ok(conversion);
    assert.ok(Math.abs(32 * conversion.scale + conversion.offset) < 1e-10);
  });
});
