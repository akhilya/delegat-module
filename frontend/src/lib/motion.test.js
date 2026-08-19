import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const style = readFileSync(new URL("../style.css", import.meta.url), "utf8");
const button = readFileSync(
  new URL("../components/ui/button/index.js", import.meta.url),
  "utf8",
);
const sheet = readFileSync(
  new URL("../components/ui/sheet/SheetContent.vue", import.meta.url),
  "utf8",
);

describe("product motion tokens", () => {
  it("defines one ease and the press/ui/overlay/drawer durations", () => {
    assert.match(style, /--ease-out-quart:\s*cubic-bezier\(0\.25, 1, 0\.5, 1\)/);
    assert.match(style, /--duration-press:\s*120ms/);
    assert.match(style, /--duration-ui:\s*150ms/);
    assert.match(style, /--duration-overlay:\s*200ms/);
    assert.match(style, /--duration-drawer:\s*280ms/);
  });

  it("presses buttons without bounce and keeps sheets under 300ms", () => {
    assert.match(button, /active:scale-\[0\.98\]/);
    assert.doesNotMatch(button, /bounce|elastic/);
    assert.match(sheet, /--duration-drawer/);
    assert.doesNotMatch(sheet, /duration-500/);
  });
});

describe("button lift (Control Plane)", () => {
  it("adds a short white sheen and hairline drop shadow on filled buttons", () => {
    assert.match(style, /\[data-slot="button"\]:not\(\[data-variant="link"\]\)/);
    assert.match(style, /box-shadow:\s*0 1px 2px oklch\(0 0 0 \/ 0\.1\)/);
    assert.match(
      style,
      /linear-gradient\(\s*180deg,\s*oklch\(1 0 0 \/ 0\.08\)/,
    );
  });

  it("keeps link flat and ghost without a sheen", () => {
    assert.match(
      style,
      /\[data-slot="button"\]\[data-variant="ghost"\][\s\S]*background-image:\s*none/,
    );
  });
});
