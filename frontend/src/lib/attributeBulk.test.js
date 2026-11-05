import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  attributeBulkPatch,
  retainAttributeSelection,
} from "./attributeBulk.js";

describe("attribute definition bulk actions", () => {
  it("sends only explicitly changed boolean settings, including false", () => {
    assert.deepEqual(
      attributeBulkPatch({
        isRequired: "false",
        isUnique: "keep",
        showOnProductPage: "true",
        name: "Never bulk rename",
        options: null,
      }),
      { isRequired: false, showOnProductPage: true },
    );
    assert.deepEqual(attributeBulkPatch({}), {});
    assert.deepEqual(
      attributeBulkPatch({ isRequired: "keep", isUnique: null }),
      {},
    );
  });

  it("prunes stale selections and deduplicates without selecting new attributes", () => {
    assert.deepEqual(
      retainAttributeSelection(
        [3, 1, 3, 99],
        [{ id: 1 }, { id: 2 }, { id: 3 }],
      ),
      [3, 1],
    );
    assert.deepEqual(retainAttributeSelection([1], []), []);
  });

  it("uses one shared list for both category and global attributes", () => {
    for (const page of ["categoryEditPage", "storeAttributesPage"]) {
      const source = readFileSync(
        new URL(`../pages/${page}.vue`, import.meta.url),
        "utf8",
      );
      assert.match(source, /<AttributeDefinitionList/);
      assert.doesNotMatch(source, /<draggable/);
      assert.doesNotMatch(source, /@changed=/);
      assert.match(source, /summary: true/);
    }
  });

  it("updates rows in place without a reload event and preserves touch/keyboard delete access", () => {
    const source = readFileSync(
      new URL(
        "../components/products/AttributeDefinitionList.vue",
        import.meta.url,
      ),
      "utf8",
    );
    assert.doesNotMatch(source, /emit\("changed"\)|location\.reload/);
    assert.match(source, /reveal-on-row-hover/);
    const style = readFileSync(
      new URL("../style.css", import.meta.url),
      "utf8",
    );
    assert.match(style, /@media \(hover: hover\) and \(pointer: fine\)/);
    assert.match(style, /:is\(:hover, :focus-within\)/);
    assert.match(style, /\[data-row-hover-action\]/);
  });

  it("uses the groups-style shared select-all row and bulk footer on every catalog list", () => {
    for (const path of [
      "pages/categoriesPage.vue",
      "pages/productGroupsPage.vue",
      "components/products/AttributeDefinitionList.vue",
    ]) {
      const source = readFileSync(
        new URL(`../${path}`, import.meta.url),
        "utf8",
      );
      assert.match(source, /<ListSelectAllRow/);
      assert.match(source, /<ListBulkActions/);
      assert.ok(
        source.indexOf("<ListBulkActions") > source.indexOf("</InsetGroup>"),
      );
    }
    const row = readFileSync(
      new URL(
        "../components/ui/inset-group/InsetGroupItem.vue",
        import.meta.url,
      ),
      "utf8",
    );
    assert.match(row, /event.target !== event.currentTarget/);
  });

  it("shares hover-only trash behavior across catalog, settings, and table rows but not action menus", () => {
    for (const path of [
      "pages/categoriesPage.vue",
      "pages/productGroupsPage.vue",
      "pages/productGroupEditPage.vue",
      "pages/admin/apiPage.vue",
      "pages/admin/usersPage.vue",
      "pages/admin/storesPage.vue",
      "pages/settings/AiModelsPage.vue",
      "components/tools/ScheduledTasksPanel.vue",
      "pages/marketplace/marketplacePage.vue",
      "components/ai/AiChatPanel.vue",
      "components/settings/AiMcpServersPanel.vue",
      "components/products/ProductVariantsEditor.vue",
      "components/marketplace/ValueMapEditor.vue",
      "components/marketplace/OptionMapEditor.vue",
      "components/marketplace/graph/NodeInspector.vue",
    ]) {
      assert.match(
        readFileSync(new URL(`../${path}`, import.meta.url), "utf8"),
        /reveal-on-row-hover/,
        path,
      );
    }
    for (const path of [
      "ProductRowActionMenuItems",
      "TrashRowActionMenuItems",
    ]) {
      assert.doesNotMatch(
        readFileSync(
          new URL(`../components/products/${path}.vue`, import.meta.url),
          "utf8",
        ),
        /reveal-on-row-hover/,
      );
    }
  });
});
