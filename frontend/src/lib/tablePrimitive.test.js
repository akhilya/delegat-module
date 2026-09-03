import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const table = readFileSync(
  new URL("../components/ui/table/Table.vue", import.meta.url),
  "utf8",
);
const cell = readFileSync(
  new URL("../components/ui/table/TableCell.vue", import.meta.url),
  "utf8",
);
const frame = readFileSync(
  new URL("../components/logs/LogTableFrame.vue", import.meta.url),
  "utf8",
);

describe("flush table primitive", () => {
  it("keeps contain=false so sticky headers and row-height refs still bind to <table>", () => {
    assert.match(table, /contain:\s*\{\s*type:\s*Boolean,\s*default:\s*true/);
    assert.match(table, /defineExpose\(\{\s*\$el:\s*rootEl\s*\}\)/);
    assert.match(table, /:is="contain \? 'div' : 'table'"/);
    assert.doesNotMatch(table, /"table-fixed"/);
    assert.doesNotMatch(table, /rounded-/);
  });

  it("forwards cell attrs (colspan, style) onto <td>", () => {
    assert.match(cell, /inheritAttrs:\s*false/);
    assert.match(cell, /v-bind="\$attrs"/);
  });

  it("keeps catalog tables flush and content-height (footer under last row)", () => {
    assert.doesNotMatch(frame, /rounded/);
    assert.doesNotMatch(frame, /ring-/);
    assert.doesNotMatch(frame, /flex-1/);
    assert.match(frame, /\$slots\.footer/);
    assert.match(frame, /<slot name="footer"/);
    assert.match(frame, /overflow-x-auto/);
  });
});
