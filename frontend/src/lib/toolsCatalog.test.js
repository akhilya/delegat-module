import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  isJobTool,
  isPageTool,
  toolDescription,
  toolHref,
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
});
