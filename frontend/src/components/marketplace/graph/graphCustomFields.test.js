import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  ADD_ADAPTER_FIELD_HANDLE,
  appendCustomAdapterField,
  nextCustomAdapterField,
} from "./graphCustomFields.js";

describe("custom adapter fields", () => {
  it("allocates unique custom keys", () => {
    const first = nextCustomAdapterField([]);
    assert.equal(first.fieldKey, "custom");
    assert.equal(first.isCustom, true);
    const second = nextCustomAdapterField([first]);
    assert.equal(second.fieldKey, "custom_2");
  });

  it("keeps a requested key when free", () => {
    const created = nextCustomAdapterField([], {
      fieldKey: "material",
      label: "Material",
    });
    assert.equal(created.fieldKey, "material");
    assert.equal(created.label, "Material");
  });

  it("suffixes a requested key that is already taken", () => {
    const created = nextCustomAdapterField([{ fieldKey: "material" }], {
      fieldKey: "material",
    });
    assert.equal(created.fieldKey, "material_2");
  });

  it("appends without mutating the original list", () => {
    const original = [{ fieldKey: "sku" }];
    const { fields, created } = appendCustomAdapterField(original);
    assert.equal(original.length, 1);
    assert.equal(fields.length, 2);
    assert.equal(created.fieldKey, "custom");
    assert.equal(ADD_ADAPTER_FIELD_HANDLE, "f:__add__");
  });
});
