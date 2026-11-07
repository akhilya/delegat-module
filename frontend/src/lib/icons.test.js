import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const icons = readFileSync(new URL("./icons.js", import.meta.url), "utf8");
const builder = readFileSync(
  new URL("../../scripts/build-icons.mjs", import.meta.url),
  "utf8",
);

describe("cabinet icons", () => {
  it("uses MingCute Core Filled for lucide-compatible names, not Phosphor or a fake Hugeicons stroke", () => {
    assert.match(icons, /@mingcute\/vue\/core-filled/);
    assert.match(builder, /@mingcute\/vue\/core-filled/);
    assert.doesNotMatch(icons, /@phosphor-icons\/vue/);
    assert.doesNotMatch(builder, /@phosphor-icons\/vue/);
    assert.doesNotMatch(icons, /FILLED_STROKE_WIDTH/);
    assert.doesNotMatch(icons, /HugeiconsIcon/);
    assert.doesNotMatch(icons, /weight:\s*["']fill["']/);
  });

  it("uses the hollow MingCute search lens, not the filled blob", () => {
    assert.match(builder, /Search:\s*"SearchRegular"/);
    assert.match(icons, /from ["']\.\/searchRegular\.js["']/);
    assert.match(icons, /export const Search = createMingCuteIcon\(SearchRegular/);
    assert.doesNotMatch(icons, /export const Search = createMingCuteIcon\(SearchFilled/);
  });

  it("keeps Hugeicons Loading03 for loaders", () => {
    assert.match(icons, /from ["']\.\/hugeicons["']/);
    assert.match(icons, /export const Loader =/);
    assert.match(icons, /export const Loader2 =/);
  });

  it("avoids square-badge glyphs under the large cabinet radius", () => {
    assert.match(builder, /Check:\s*"CheckFilled"/);
    assert.match(builder, /Save:\s*"CheckFilled"/);
    assert.match(builder, /ExternalLink:\s*"ArrowRightUpFilled"/);
    assert.match(builder, /LayoutGrid:\s*"ClassifyFilled"/);
    assert.match(builder, /Image:\s*"PicFilled"/);
    assert.match(builder, /^\s*Square:\s*"StopCircleFilled"/m);
    assert.match(builder, /Copy:\s*"Copy2Filled"/);
    assert.doesNotMatch(
      builder,
      /CloseSquareFilled|AddSquareFilled|MinusSquareFilled|LayoutGridFilled|ExternalLinkFilled|SquareFilled/,
    );
    assert.doesNotMatch(
      icons,
      /CloseSquareFilled|AddSquareFilled|MinusSquareFilled|LayoutGridFilled|ExternalLinkFilled/,
    );
  });
});
