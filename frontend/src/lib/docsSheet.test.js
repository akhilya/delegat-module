import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { docsSheetUsesRail } from "./docsSheet.js";

const sheet = readFileSync(
  new URL("../components/docs/DocsSheet.vue", import.meta.url),
  "utf8",
);
const app = readFileSync(new URL("../app.vue", import.meta.url), "utf8");
const prose = readFileSync(
  new URL("../styles/markdown-prose.css", import.meta.url),
  "utf8",
);

describe("documentation sheet availability", () => {
  it("keeps documentation available on the bare setup route", () => {
    assert.match(app, /<DocsSheet v-if="!isPublicPage" \/>/);
    assert.doesNotMatch(
      app,
      /<template v-if="!isBareLayout">[\s\S]*?<DocsSheet/,
    );
  });
});

describe("docs sheet reading chrome", () => {
  it("uses a spacious TOC list instead of inset grouped rows", () => {
    assert.match(sheet, /data-slot="docs-toc"/);
    assert.match(sheet, /w-60/);
    assert.match(sheet, /compactChrome/);
    assert.match(sheet, /docsSheetUsesRail/);
    assert.match(sheet, /data-slot="docs-toc-trigger"/);
    assert.match(sheet, /data-slot="docs-toc-menu"/);
    assert.doesNotMatch(sheet, /InsetGroup/);
    assert.match(sheet, /bg-muted font-medium text-foreground/);
  });

  it("collapses the TOC when the sheet is narrowed", () => {
    assert.equal(docsSheetUsesRail(880), true);
    assert.equal(docsSheetUsesRail(719), false);
  });

  it("renders the article through MarkdownProse", () => {
    assert.match(sheet, /<MarkdownProse/);
    assert.match(sheet, /:html="articleHtml"/);
    assert.doesNotMatch(sheet, /docs-article/);
    assert.doesNotMatch(sheet, /v-html="articleHtml"/);
  });

  it("stacks as a companion without a modal scrim", () => {
    assert.match(sheet, /:modal="false"/);
    assert.match(sheet, /:overlay="false"/);
    assert.match(sheet, /edge-offset="docsEdgeOffset"/);
  });
});

describe("shared markdown prose", () => {
  it("does not paint links with inverted primary (invisible in dark)", () => {
    assert.doesNotMatch(prose, /var\(--color-primary\)/);
    assert.match(prose, /color: var\(--muted-foreground\)/);
    assert.match(prose, /text-decoration: underline/);
  });

  it("draws markdown tables like the cabinet data table", () => {
    assert.match(prose, /line-height: 1\.7/);
    assert.match(prose, /border-collapse: collapse/);
    assert.match(prose, /height: 2\.5rem/);
    assert.match(prose, /padding: 0\.5rem/);
    assert.match(prose, /font-size: 0\.75rem/);
    assert.doesNotMatch(prose, /border-top-left-radius/);
    assert.doesNotMatch(prose, /border-collapse: separate/);
  });

  it("restores list markers against Tailwind preflight", () => {
    assert.match(
      prose,
      /\.markdown-prose ul \{[\s\S]*list-style: disc outside/,
    );
    assert.match(
      prose,
      /\.markdown-prose ol \{[\s\S]*list-style: decimal outside/,
    );
    assert.match(prose, /list-style-type: circle/);
    assert.match(prose, /list-style-type: square/);
  });
});
