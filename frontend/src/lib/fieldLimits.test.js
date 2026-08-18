import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  FIELD_LIMITS,
  clipToLimit,
  fieldLimitIssue,
  isNonEmptyWithinLimit,
  isWithinLimit,
  trimmedLength,
} from "./fieldLimits.js";

describe("FIELD_LIMITS", () => {
  it("matches backend store name cap", () => {
    assert.equal(FIELD_LIMITS.storeName, 40);
  });

  it("matches product / category / connection caps", () => {
    assert.equal(FIELD_LIMITS.productName, 100);
    assert.equal(FIELD_LIMITS.productSku, 20);
    assert.equal(FIELD_LIMITS.categoryName, 100);
    assert.equal(FIELD_LIMITS.connectionName, 100);
    assert.equal(FIELD_LIMITS.groupName, 200);
    assert.equal(FIELD_LIMITS.attributeGroup, 100);
    assert.equal(FIELD_LIMITS.attributeGroupIcon, 80);
  });
});

describe("isNonEmptyWithinLimit", () => {
  it("rejects empty and whitespace", () => {
    assert.equal(isNonEmptyWithinLimit("", 40), false);
    assert.equal(isNonEmptyWithinLimit("   ", 40), false);
  });

  it("accepts a name at the store cap", () => {
    assert.equal(isNonEmptyWithinLimit("a".repeat(40), 40), true);
  });

  it("rejects a name over the store cap", () => {
    assert.equal(isNonEmptyWithinLimit("a".repeat(41), 40), false);
    assert.equal(isWithinLimit("a".repeat(41), 40), false);
  });

  it("trims before measuring", () => {
    assert.equal(trimmedLength("  shop  "), 4);
    assert.equal(isNonEmptyWithinLimit("  ok  ", 40), true);
  });
});

describe("fieldLimitIssue", () => {
  it("returns empty when required and blank", () => {
    assert.equal(fieldLimitIssue("  ", 40), "empty");
  });

  it("allows blank when not required", () => {
    assert.equal(fieldLimitIssue("  ", 40, { required: false }), null);
  });

  it("returns tooLong over the cap", () => {
    assert.equal(fieldLimitIssue("a".repeat(41), 40), "tooLong");
  });
});

describe("clipToLimit", () => {
  it("trims and clips to max", () => {
    assert.equal(clipToLimit("  abcdef  ", 3), "abc");
  });
});
