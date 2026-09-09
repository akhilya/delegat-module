import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const source = readFileSync(new URL("./ProductGroupSidebar.vue", import.meta.url), "utf8");

describe("ProductGroupSidebar compact preview", () => {
  it("keeps the context-menu virtual anchor outside the tooltip popper", () => {
    const memberBlock = source.split('v-for="member in group.members"')[1] ?? "";
    const triggerIdx = memberBlock.indexOf("<ContextMenuTrigger");
    const tooltipIdx = memberBlock.indexOf("<Tooltip ");
    assert.notEqual(triggerIdx, -1);
    assert.notEqual(tooltipIdx, -1);
    assert.ok(
      triggerIdx < tooltipIdx,
      "ContextMenuTrigger must wrap Tooltip so MenuAnchor cannot steal the preview position",
    );
    assert.match(memberBlock, /<TooltipTrigger as-child>\s*<router-link/);
    assert.doesNotMatch(memberBlock, /<TooltipTrigger as-child>\s*<ContextMenuTrigger/);
  });

  it("locks pane height with the shared fill token so the bottom gap matches page-scroll-end", () => {
    assert.match(source, /h-\[var\(--app-fill-pane-height\)\]/);
    assert.doesNotMatch(source, /4\.75rem/);
  });
});
