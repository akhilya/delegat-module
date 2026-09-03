import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  adapterGroupLabel,
  groupToolsCatalog,
  isJobTool,
  isPageTool,
  isQuickRunTool,
  toolConfirmTitleKey,
  toolDescription,
  toolGroupId,
  toolHref,
  toolQuickTooltipKey,
  toolSectionBlocks,
  toolStage,
  toolStageClass,
  toolStageLabelKey,
  toolTitle,
} from "./toolsCatalog.js";

describe("toolsCatalog", () => {
  it("uses i18n keys for platform page tools", () => {
    const entry = {
      id: "catalog-connect",
      mode: "page",
      path: "/tools/catalog-connect",
      titleKey: "pages.toolsPage.catalogConnectTitle",
      descriptionKey: "pages.toolsPage.catalogConnectDescription",
    };
    const t = (key) => `T:${key}`;
    assert.equal(toolTitle(entry, t), "T:pages.toolsPage.catalogConnectTitle");
    assert.equal(
      toolDescription(entry, t),
      "T:pages.toolsPage.catalogConnectDescription",
    );
    assert.equal(toolHref(entry), "/tools/catalog-connect");
    assert.equal(isPageTool(entry), true);
    assert.equal(isJobTool(entry), false);
  });

  it("uses i18n keys for catalog scrape", () => {
    const entry = {
      id: "catalog-scrape",
      mode: "page",
      path: "/tools/catalog-scrape",
      titleKey: "pages.toolsPage.catalogScrapeTitle",
      descriptionKey: "pages.toolsPage.catalogScrapeDescription",
    };
    const t = (key) => `T:${key}`;
    assert.equal(toolTitle(entry, t), "T:pages.toolsPage.catalogScrapeTitle");
    assert.equal(toolHref(entry), "/tools/catalog-scrape");
    assert.equal(isPageTool(entry), true);
  });

  it("uses i18n keys for Public API try", () => {
    const entry = {
      id: "public-api-try",
      mode: "page",
      path: "/tools/public-api-try",
      titleKey: "pages.toolsPage.publicApiTryTitle",
      descriptionKey: "pages.toolsPage.publicApiTryDescription",
    };
    const t = (key) => `T:${key}`;
    assert.equal(toolTitle(entry, t), "T:pages.toolsPage.publicApiTryTitle");
    assert.equal(toolHref(entry), "/tools/public-api-try");
    assert.equal(isPageTool(entry), true);
  });

  it("uses plugin strings for adapter job tools", () => {
    const entry = {
      id: "wildberries.duplicate-cards",
      mode: "job",
      path: "/tools/wildberries.duplicate-cards",
      title: "Find copies",
      description: "Search copied cards",
    };
    const t = () => "unused";
    assert.equal(toolTitle(entry, t), "Find copies");
    assert.equal(toolDescription(entry, t), "Search copied cards");
    assert.equal(toolHref(entry), "/tools/wildberries.duplicate-cards");
    assert.equal(isJobTool(entry), true);
  });

  it("falls back to /tools/{id} when path is missing", () => {
    assert.equal(toolHref({ id: "ozon.scraper" }), "/tools/ozon.scraper");
    assert.equal(toolHref(null), "/tools");
  });

  it("marks platform job tools for catalog quick-run", () => {
    const entry = { id: "export-all-prices", mode: "job", quickRun: true };
    assert.equal(isQuickRunTool(entry), true);
    assert.equal(
      toolConfirmTitleKey(entry),
      "pages.toolsPage.exportAllPricesConfirmTitle",
    );
    assert.equal(
      toolQuickTooltipKey(entry),
      "pages.toolsPage.exportAllPricesQuickTooltip",
    );
    assert.equal(isQuickRunTool({ id: "catalog-connect", mode: "page" }), false);
  });

  it("groups catalog entries into separate lists", () => {
    const sections = groupToolsCatalog([
      { id: "public-api-try", group: "api" },
      { id: "wildberries.ok-tool", source: "adapter", adapterKey: "wildberries" },
      { id: "catalog-scrape", group: "catalog" },
      { id: "export-all-prices", group: "prices", mode: "job", quickRun: true },
      { id: "ozon.scraper", source: "adapter", adapterKey: "ozon" },
      { id: "catalog-connect", group: "catalog" },
      { id: "clear-promotion-products", group: "promotions" },
    ]);
    assert.deepEqual(
      sections.map((s) => s.id),
      ["catalog", "prices", "promotions", "api", "adapter:ozon", "adapter:wildberries"],
    );
    assert.deepEqual(
      sections[0].tools.map((e) => e.id),
      ["catalog-scrape", "catalog-connect"],
    );
    assert.equal(sections[4].adapterKey, "ozon");
    assert.equal(toolGroupId({ id: "export-all-prices" }), "prices");
    assert.equal(adapterGroupLabel("yandex-market"), "Yandex Market");
  });

  it("puts each quick-run job in its own list block", () => {
    const blocks = toolSectionBlocks([
      { id: "catalog-connect", mode: "page" },
      { id: "export-all-prices", mode: "job", quickRun: true },
      { id: "catalog-scrape", mode: "page" },
    ]);
    assert.deepEqual(
      blocks.map((b) => b.type),
      ["list", "job", "list"],
    );
    assert.equal(blocks[0].tools[0].id, "catalog-connect");
    assert.equal(blocks[1].entry.id, "export-all-prices");
    assert.equal(blocks[2].tools[0].id, "catalog-scrape");
  });

  it("labels experimental tools and treats missing stage as stable", () => {
    assert.equal(toolStage({ stage: "experimental" }), "experimental");
    assert.equal(toolStage({ stage: "Experimental" }), "experimental");
    assert.equal(toolStage({ id: "catalog-connect" }), "stable");
    assert.equal(toolStage(null), "stable");
    assert.equal(
      toolStageLabelKey({ stage: "experimental" }),
      "pages.toolsPage.stageExperimental",
    );
    assert.equal(toolStageClass({ stage: "experimental" }), "text-warning");
    assert.equal(toolStageLabelKey({ stage: "stable" }), "");
    assert.equal(toolStageClass({ stage: "deprecated" }), "text-destructive");
  });
});
