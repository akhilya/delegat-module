import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const sheet = readFileSync(
  new URL("../components/docs/DocsSheet.vue", import.meta.url),
  "utf8",
);

describe("docs sheet reading chrome", () => {
  it("uses a spacious TOC list instead of inset grouped rows", () => {
    assert.match(sheet, /data-slot="docs-toc"/);
    assert.match(sheet, /w-60/);
    assert.doesNotMatch(sheet, /InsetGroup/);
    assert.match(sheet, /bg-muted font-medium text-foreground/);
  });

  it("does not paint article links with inverted primary (invisible in dark)", () => {
    assert.doesNotMatch(sheet, /var\(--color-primary\)/);
    assert.match(sheet, /color: var\(--muted-foreground\)/);
    assert.match(sheet, /text-decoration: underline/);
  });

  it("gives tables and body copy room to breathe", () => {
    assert.match(sheet, /line-height: 1\.7/);
    assert.match(sheet, /padding: 0\.75rem 1rem/);
    assert.match(sheet, /border-collapse: separate/);
    assert.match(sheet, /px-8 py-7/);
  });
});
