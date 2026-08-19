import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  MENU_ANIMATION,
  MENU_ITEM,
  MENU_ITEM_CHECK,
  MENU_ITEM_INDICATOR,
  MENU_LABEL,
  MENU_SEPARATOR,
  MENU_SURFACE,
} from "./menuSurface.js";

describe("menuSurface", () => {
  it("keeps the frosted menu class on the shared surface", () => {
    assert.match(MENU_SURFACE, /cn-menu-translucent/);
    assert.match(MENU_SURFACE, /rounded-lg/);
    assert.doesNotMatch(MENU_SURFACE, /\bborder\b/);
  });

  it("highlights rows with muted fill, not accent invert", () => {
    assert.match(MENU_ITEM, /muted\/80/);
    assert.match(MENU_ITEM_INDICATOR, /muted\/80/);
    assert.doesNotMatch(MENU_ITEM, /focus:bg-accent/);
  });

  it("puts exclusive-choice checks on the trailing edge", () => {
    assert.match(MENU_ITEM_INDICATOR, /pr-8/);
    assert.doesNotMatch(MENU_ITEM_INDICATOR, / pl-8/);
    assert.match(MENU_ITEM_CHECK, /right-2/);
    assert.doesNotMatch(MENU_ITEM_CHECK, /left-2/);
  });

  it("keeps open animation and quiet separators", () => {
    assert.match(MENU_ANIMATION, /zoom-in-95/);
    assert.match(MENU_SURFACE, /--duration-ui/);
    assert.match(MENU_SEPARATOR, /bg-border\/50/);
    assert.match(MENU_LABEL, /text-xs/);
    assert.match(MENU_LABEL, /data-\[inset\]:pl-8/);
  });

  it("portals nested menu panels so parent overflow cannot clip them", () => {
    const dropdown = readFileSync(
      new URL("../components/ui/dropdown-menu/DropdownMenuSubContent.vue", import.meta.url),
      "utf8",
    );
    const context = readFileSync(
      new URL("../components/ui/context-menu/ContextMenuSubContent.vue", import.meta.url),
      "utf8",
    );
    assert.match(dropdown, /DropdownMenuPortal/);
    assert.match(context, /ContextMenuPortal/);
  });

  it("uses a trailing check in radio menu rows, not a leading disc", () => {
    const dropdown = readFileSync(
      new URL("../components/ui/dropdown-menu/DropdownMenuRadioItem.vue", import.meta.url),
      "utf8",
    );
    const context = readFileSync(
      new URL("../components/ui/context-menu/ContextMenuRadioItem.vue", import.meta.url),
      "utf8",
    );
    assert.match(dropdown, /MENU_ITEM_CHECK/);
    assert.match(context, /MENU_ITEM_CHECK/);
    assert.match(dropdown, /Check/);
    assert.match(context, /Check/);
    assert.doesNotMatch(dropdown, /Circle/);
    assert.doesNotMatch(context, /Circle/);
  });
});
