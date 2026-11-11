import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { marketplaceCategoryLeaf } from "./marketplaceCategoryName.js";

describe("product import categories", () => {
  it("uses inset target selection on the standalone category import page too", () => {
    const page = readFileSync(
      new URL("../pages/marketplace/categoryImportPage.vue", import.meta.url),
      "utf8",
    );
    const start = page.indexOf('<p id="category-import-target-label"');
    const target = page.slice(start, page.indexOf("<SettingsFormField", start));
    assert.match(target, /<InsetGroup\s/);
    assert.doesNotMatch(target, /RadioGroup/);
    assert.match(target, /@click="setImportMode\('new'\)"/);
    assert.match(target, /@click="setImportMode\('existing'\)"/);
    assert.match(target, /:aria-pressed="importMode === 'existing'"/);
  });
  it("uses breadcrumb leaves before name limits and preserves literal slashes", () => {
    assert.equal(
      marketplaceCategoryLeaf(
        "Хобби и творчество / Настольные и карточные игры / Аксессуар для настольных игр",
      ),
      "Аксессуар для настольных игр",
    );
    assert.equal(marketplaceCategoryLeaf("A".repeat(180) + " / Leaf"), "Leaf");
    assert.equal(
      marketplaceCategoryLeaf("AC/DC accessories"),
      "AC/DC accessories",
    );
    assert.equal(marketplaceCategoryLeaf(null), "");
  });

  it("uses native inset choice buttons without nesting form controls inside them", () => {
    const choice = readFileSync(
      new URL(
        "../components/marketplace/ImportCategoryChoice.vue",
        import.meta.url,
      ),
      "utf8",
    );
    const page = readFileSync(
      new URL("../pages/marketplace/connectionPage.vue", import.meta.url),
      "utf8",
    );
    assert.match(choice, /<InsetGroup>/);
    assert.match(choice, /:aria-pressed="mode === choice"/);
    assert.match(choice, /<Check\s+v-if="mode === choice"/);
    assert.ok(
      choice.indexOf("</InsetGroup>") < choice.indexOf("<SearchableSelect"),
    );
    assert.doesNotMatch(page, /RadioGroup/);
    assert.match(page, /<ImportCategoryChoice/);
    assert.match(
      page,
      /newCategoryName: marketplaceCategoryLeaf\(\s*group.externalCategoryName,?\s*\)/,
    );
    assert.match(page, /targetCategoryName: categoryNameMap\[key\]/);
  });
});
