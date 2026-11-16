import { expect, test } from "@playwright/test";

// Verifies the production renderAiMarkdown (marked + DOMPurify) inside a real
// browser DOM: the authenticated store-file download link must survive
// sanitisation, while javascript: URLs must never reach an anchor. The harness
// page is served by route interception so no backend is needed; the module
// itself is loaded through the Vite dev server.
test("ai markdown keeps the store download link and strips javascript hrefs", async ({
  page,
}) => {
  await page.route("**/ai-md-harness", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: "<!doctype html><html><body></body></html>",
    }),
  );
  await page.goto("/ai-md-harness");

  const rendered = await page.evaluate(async () => {
    const load = new Function(
      "return import('/src/lib/ai/markdown.js')",
    ) as () => Promise<{ renderAiMarkdown: (text: string) => string }>;
    const { renderAiMarkdown } = await load();
    const parse = (html: string) =>
      new DOMParser().parseFromString(html, "text/html");
    const csv = parse(
      renderAiMarkdown("[Download CSV](/api/store-storage/files/123/download)"),
    );
    const js = parse(renderAiMarkdown("[click](javascript:alert(1))"));
    return {
      csvAnchors: Array.from(csv.querySelectorAll("a")).map((a) => ({
        href: a.getAttribute("href"),
        text: a.textContent,
      })),
      jsAnchorHrefs: Array.from(js.querySelectorAll("a")).map((a) =>
        a.getAttribute("href"),
      ),
      jsHtml: js.body.innerHTML,
    };
  });

  expect(rendered.csvAnchors).toEqual([
    { href: "/api/store-storage/files/123/download", text: "Download CSV" },
  ]);
  expect(
    rendered.jsAnchorHrefs.every(
      (href) => !href?.toLowerCase().startsWith("javascript:"),
    ),
  ).toBe(true);
});
