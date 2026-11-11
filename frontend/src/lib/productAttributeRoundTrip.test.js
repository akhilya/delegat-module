import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const source = readFileSync(
  new URL("./productAttributeValidation.js", import.meta.url),
  "utf8",
)
  .replace(/^import .*;\r?\n/gm, "")
  .replace(/export /g, "");
const helpers = new Function(
  "parseAttributeFileIds",
  `${source}; return {attributesToFormRecord, formRecordToAttributeValues, categoryAttributeCacheToValues};`,
)(JSON.parse);

test("scalar values survive DTO fileIds arrays and product save/reload", () => {
  const attributes = [0, 1, 2, 3, 4, 5, 6].map((type) => ({
    id: type + 1,
    type,
  }));
  const values = [
    "text",
    "12.5",
    "true",
    "91246",
    '["91246"]',
    "1234567890123",
    "[7,8]",
  ];
  const rows = values.map((value, index) => ({
    attributeId: index + 1,
    value,
    fileIds: index === 6 ? [7, 8] : [],
  }));
  const record = helpers.attributesToFormRecord(attributes, rows);
  assert.deepEqual(Object.values(record), values);
  const saved = helpers.formRecordToAttributeValues(
    record,
    "attributeId",
    attributes,
  );
  assert.deepEqual(
    saved.map((row) => row.value),
    values,
  );
  assert.deepEqual(saved[6].fileIds, [7, 8]);
  assert.deepEqual(helpers.attributesToFormRecord(attributes, saved), record);
  assert.deepEqual(
    helpers.categoryAttributeCacheToValues(record, attributes),
    saved,
  );
});

test("store attribute scalar values do not use the empty attachment projection", () => {
  const rows = [{ storeAttributeId: 1, value: "42", fileIds: [] }];
  const attributes = [{ id: 1, type: "Number" }];
  assert.deepEqual(
    helpers.attributesToFormRecord(attributes, rows, "storeAttributeId"),
    { 1: "42" },
  );
});

test("partial options load on selector open, including keyboard multi-select", () => {
  const field = readFileSync(
    new URL(
      "../components/products/AttributeFieldControl.vue",
      import.meta.url,
    ),
    "utf8",
  );
  assert.match(field, /attribute\.optionsPartial/);
  assert.equal((field.match(/@open="loadOptions\(value\)"/g) || []).length, 2);
  const multi = readFileSync(
    new URL(
      "../components/ui/multi-select/MultiSelectDropdown.vue",
      import.meta.url,
    ),
    "utf8",
  );
  assert.match(multi, /emit\("open"\)/);
  assert.match(multi, /emit\("search", query\)/);
  assert.match(multi, /@keydown\.up\.prevent="moveOptionFocus/);
  assert.match(multi, /@keydown\.down\.prevent="moveOptionFocus/);
  assert.match(
    field,
    /categories\/\$\{categoryId\}\/attributes\/\$\{attributeId\}\/options/,
  );
  assert.match(field, /requestId !== this\.optionsRequestId/);
  assert.match(field, /@load-more="loadMoreOptions\(value\)"/);
});
