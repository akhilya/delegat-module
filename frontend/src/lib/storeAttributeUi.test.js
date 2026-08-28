import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  countAttributeOptions,
  emptyStoreAttributeForm,
  formatAttributeOptionsForEdit,
  formFromStoreAttribute,
  generateAttributeKey,
  isNumberAttributeType,
  isOptionsAttributeType,
  parseAttributeOptions,
  resolveStoreAttributeTypeValue,
  serializeAttributeOptions,
  storeAttributeRowCaption,
  storeAttributeSavePayload,
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

  it("loads the same value constraints as category attributes", () => {
    const loaded = formFromStoreAttribute({
      name: "Color",
      key: "color",
      type: "Select",
      allowCustomValues: true,
      maxValues: 3,
      maxLength: 40,
      minValue: 1.5,
      maxValue: 10,
      validationPattern: "^[A-Z]+$",
    });
    assert.equal(loaded.form.allowCustomValues, true);
    assert.equal(loaded.form.maxValues, "3");
    assert.equal(loaded.form.maxLength, "40");
    assert.equal(loaded.form.minValue, "1.5");
    assert.equal(loaded.form.maxValue, "10");
    assert.equal(loaded.form.validationPattern, "^[A-Z]+$");
    assert.equal(isNumberAttributeType(1), true);
    assert.equal(isNumberAttributeType("Select"), false);
  });

  it("puts select constraints on the save payload and clears them for other types", () => {
    const selectPayload = storeAttributeSavePayload({
      ...emptyStoreAttributeForm(),
      name: "Color",
      key: "color",
      type: 3,
      allowCustomValues: true,
      maxValues: "2",
      maxLength: "20",
      options: "Red, Blue",
    });
    assert.equal(selectPayload.allowCustomValues, true);
    assert.equal(selectPayload.maxValues, 2);
    assert.equal(selectPayload.maxLength, 20);
    assert.equal(selectPayload.minValue, null);
    assert.equal(selectPayload.validationPattern, null);

    const numberPayload = storeAttributeSavePayload({
      ...emptyStoreAttributeForm(),
      name: "Weight",
      key: "weight",
      type: 1,
      allowCustomValues: true,
      maxValues: "4",
      minValue: "0.5",
      maxValue: "10",
      validationPattern: "^x$",
    });
    assert.equal(numberPayload.allowCustomValues, false);
    assert.equal(numberPayload.maxValues, null);
    assert.equal(numberPayload.maxLength, null);
    assert.equal(numberPayload.minValue, 0.5);
    assert.equal(numberPayload.maxValue, 10);
    assert.equal(numberPayload.validationPattern, null);

    const textPayload = storeAttributeSavePayload({
      ...emptyStoreAttributeForm(),
      name: "SKU extra",
      key: "sku_extra",
      type: 0,
      isMultiline: true,
      validationPattern: "^[A-Z0-9]+$",
      maxLength: "12",
    });
    assert.equal(textPayload.isMultiline, true);
    assert.equal(textPayload.validationPattern, "^[A-Z0-9]+$");
    assert.equal(textPayload.maxLength, 12);
  });
});
