import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  countAttributeOptions,
  formatAttributeOptionsForEdit,
  formFromStoreAttribute,
  generateAttributeKey,
  isOptionsAttributeType,
  parseAttributeOptions,
  resolveStoreAttributeTypeValue,
  serializeAttributeOptions,
  storeAttributeRowCaption,
} from "./storeAttributeUi.js";

describe("storeAttributeUi", () => {
  it("transliterates a Cyrillic name into a snake_case key", () => {
    assert.equal(
      generateAttributeKey("\u0411\u0440\u0435\u043d\u0434"),
      "brend",
    );
    assert.equal(generateAttributeKey("My Brand!"), "my_brand");
  });

  it("resolves numeric and JSON enum type names", () => {
    assert.equal(resolveStoreAttributeTypeValue(4), 4);
    assert.equal(resolveStoreAttributeTypeValue("Barcode"), 5);
    assert.equal(resolveStoreAttributeTypeValue("3"), 3);
    assert.equal(isOptionsAttributeType("Select"), true);
    assert.equal(isOptionsAttributeType(0), false);
  });

  it("parses JSON, CSV, and value:label option lists", () => {
    assert.deepEqual(parseAttributeOptions('["Red","Blue"]'), [
      { value: "Red", label: "Red" },
      { value: "Blue", label: "Blue" },
    ]);
    assert.deepEqual(parseAttributeOptions("r:Red, b:Blue"), [
      { value: "r", label: "Red" },
      { value: "b", label: "Blue" },
    ]);
    assert.equal(countAttributeOptions('[{"value":"a","label":"A"}]'), 1);
  });

  it("serializes textarea options back to JSON", () => {
    assert.equal(
      serializeAttributeOptions("Red, Blue"),
      JSON.stringify([
        { value: "Red", label: "Red" },
        { value: "Blue", label: "Blue" },
      ]),
    );
    assert.equal(serializeAttributeOptions("  "), null);
  });

  it("builds a list caption from type, key, and option count", () => {
    const caption = storeAttributeRowCaption(
      {
        key: "color",
        options: '["Red","Blue","Green"]',
      },
      { typeLabel: "Select", optionsWord: "options" },
    );
    assert.equal(caption, "Select · color · 3 options");
  });

  it("loads a stored attribute into the editor form", () => {
    const loaded = formFromStoreAttribute({
      name: "Color",
      key: "color",
      type: "Select",
      isRequired: true,
      options: '["Red","Blue"]',
      showOnProductPage: false,
    });
    assert.equal(loaded.form.type, 3);
    assert.equal(loaded.form.showOnProductPage, true);
    assert.equal(loaded.form.options, "Red, Blue");
    assert.equal(loaded.optionsCount, 2);
    assert.equal(formatAttributeOptionsForEdit('["Red","Blue"]'), "Red, Blue");
  });
});
