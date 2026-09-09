import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  absolutizeDocsMedia,
  classifyDocsHref,
  rewriteDocsHtmlHrefs,
  rewriteDocsMediaInMarkdown,
} from "./docsMarkdown.js";

const ctx = {
  currentSlug: "workspace/products",
  siteOrigin: "https://module.delegat.pro",
};

describe("docs media rewrite", () => {
  it("turns shot: and docs-media into absolute public URLs", () => {
    assert.equal(
      absolutizeDocsMedia("shot:workspace/products/list", ctx.siteOrigin),
      "https://module.delegat.pro/docs-media/workspace/products/list.webp",
    );
    assert.equal(
      absolutizeDocsMedia("/docs-media/start/home.webp", ctx.siteOrigin),
      "https://module.delegat.pro/docs-media/start/home.webp",
    );
    const md = rewriteDocsMediaInMarkdown(
      "![List](shot:workspace/products/list \"Catalog\")",
      ctx.siteOrigin,
    );
    assert.match(md, /https:\/\/module\.delegat\.pro\/docs-media\/workspace\/products\/list\.webp/);
  });
});

describe("classifyDocsHref", () => {
  it("keeps product docs links inside the sheet", () => {
    assert.deepEqual(classifyDocsHref("/docs/workspace/categories", ctx), {
      kind: "sheet",
      slug: "workspace/categories",
    });
    assert.deepEqual(
      classifyDocsHref("https://module.delegat.pro/docs/en/workspace/files", ctx),
      { kind: "sheet", slug: "workspace/files" },
    );
    assert.deepEqual(classifyDocsHref("barcodes.md", ctx), {
      kind: "sheet",
      slug: "workspace/barcodes",
    });
    assert.deepEqual(classifyDocsHref("/docs", ctx), { kind: "sheet", slug: "" });
  });

  it("opens account-cabinet paths on the public site", () => {
    assert.deepEqual(classifyDocsHref("/console/tickets", ctx), {
      kind: "external",
      url: "https://module.delegat.pro/console/tickets",
    });
    assert.deepEqual(classifyDocsHref("/register", ctx), {
      kind: "external",
      url: "https://module.delegat.pro/register",
    });
  });
});

describe("rewriteDocsHtmlHrefs", () => {
  it("marks in-sheet slugs and adds target=_blank on cabinet URLs", () => {
    const html = rewriteDocsHtmlHrefs(
      '<a href="/docs/workspace/files">Files</a> <a href="/console">Console</a>',
      ctx,
    );
    assert.match(html, /data-docs-slug="workspace\/files"/);
    assert.match(html, /href="https:\/\/module\.delegat\.pro\/console"/);
    assert.match(html, /target="_blank"/);
  });
});
