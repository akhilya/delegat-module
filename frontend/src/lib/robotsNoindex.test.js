import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

describe("search engine noindex", () => {
  it("disallows crawling and marks the SPA as noindex", () => {
    const robots = readFileSync(new URL("../../public/robots.txt", import.meta.url), "utf8");
    const html = readFileSync(new URL("../../index.html", import.meta.url), "utf8");
    const nginx = readFileSync(new URL("../../nginx.conf.template", import.meta.url), "utf8");
    assert.match(robots, /User-agent:\s*\*/);
    assert.match(robots, /Disallow:\s*\//);
    assert.match(html, /name="robots"[^>]*content="noindex, nofollow, noarchive"/);
    assert.match(nginx, /X-Robots-Tag "noindex, nofollow, noarchive"/);
  });
});
