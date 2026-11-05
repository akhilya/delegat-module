import { expect, test } from "@playwright/test";

for (const width of [1280, 390]) {
  for (const dark of [false, true]) {
    test(`rounded table frames ${width}px ${dark ? "dark" : "light"}`, async ({
      page,
    }, testInfo) => {
      await page.setViewportSize({ width, height: 844 });
      // No live instance calls: mount the production primitives in a local browser fixture.
      await page.route("**/api/**", (route) => route.fulfill({ json: {} }));
      await page.goto("/signIn");
      await page.evaluate(
        async ({ dark }) => {
          const { createApp, h } =
            await import("/node_modules/.vite/deps/vue.js");
          const { default: Table } =
            await import("/src/components/ui/table/Table.vue");
          const { default: Frame } =
            await import("/src/components/logs/LogTableFrame.vue");
          document.documentElement.classList.toggle("dark", dark);
          const host = document.createElement("div");
          host.id = "table-frame-fixture";
          host.style.cssText =
            "position:fixed;inset:0;z-index:10002;overflow:auto;padding:24px;background:var(--background);color:var(--foreground)";
          document.body.append(host);
          const rows = () => [
            h("thead", [
              h("tr", [
                h(
                  "th",
                  {
                    style:
                      "position:sticky;top:0;background:var(--background);height:40px;text-align:left;padding:8px;min-width:400px",
                  },
                  "Name",
                ),
                h("th", { style: "min-width:240px" }, "Value"),
              ]),
            ]),
            h(
              "tbody",
              Array.from({ length: 20 }, (_, i) =>
                h("tr", { style: "height:40px" }, [
                  h("td", { style: "padding:8px" }, `Row ${i + 1}`),
                  h("td", "Example"),
                ]),
              ),
            ),
          ];
          const table = (contain = true) =>
            h(Table, { contain, class: "w-full" }, rows);
          createApp({
            render: () =>
              h(
                "div",
                {
                  style:
                    "display:grid;grid-template-columns:minmax(0,1fr);gap:24px;min-width:0",
                },
                [
                  h("section", { id: "default-table" }, [
                    h("h2", "Default table"),
                    table(),
                  ]),
                  h("section", { id: "nested-table" }, [
                    h("h2", "Log frame"),
                    h(Frame, null, {
                      default: () => table(),
                      footer: () => "Separate actions",
                    }),
                  ]),
                  h("section", [
                    h("h2", "Catalog scroll pane"),
                    h(
                      "div",
                      {
                        id: "catalog-table",
                        "data-slot": "table-scroll",
                        style: "height:220px;overflow:auto",
                      },
                      [table(false)],
                    ),
                  ]),
                  h("section", [
                    h("h2", "Page-scrolling table"),
                    h(
                      "div",
                      { id: "page-owner", style: "height:220px;overflow:auto" },
                      [
                        h("div", { style: "height:40px" }),
                        h(
                          Frame,
                          { dockFooter: true },
                          {
                            default: () => table(false),
                            footer: () => "Dock actions",
                          },
                        ),
                      ],
                    ),
                  ]),
                ],
              ),
          }).mount(host);
        },
        { dark },
      );

      const defaultFrame = page.locator(
        '#default-table [data-slot="table-container"]',
      );
      const nestedFrame = page.locator(
        '#nested-table [data-slot="table-frame"]',
      );
      const catalogFrame = page.locator("#catalog-table");
      const pageFrame = page.locator('#page-owner [data-slot="table-frame"]');
      for (const frame of [
        defaultFrame,
        nestedFrame,
        catalogFrame,
        pageFrame,
      ]) {
        await expect(frame).toHaveCSS("border-top-width", "1px");
        await expect(frame).toHaveCSS("border-top-left-radius", "16px");
        await expect(frame).toHaveCSS("border-bottom-right-radius", "16px");
      }
      await expect(
        nestedFrame.locator('[data-slot="table-container"]'),
      ).toHaveCSS("border-top-width", "0px");
      await expect(
        page.locator('#nested-table [data-slot="table-actions-footer"]'),
      ).toHaveCSS("border-top-width", "0px");
      await expect(
        page.locator('#page-owner [data-slot="page-dock-footer"] > div'),
      ).toHaveCSS("border-top-width", "0px");
      expect(
        await page.locator("#nested-table").evaluate((el) => {
          const frame = el.querySelector('[data-slot="table-frame"]')!;
          return (
            !frame.textContent?.includes("Separate actions") &&
            el.textContent?.includes("Separate actions")
          );
        }),
      ).toBe(true);
      await defaultFrame.evaluate((el) => {
        el.scrollLeft = 200;
      });
      if (width < 640)
        expect(
          await defaultFrame.evaluate((el) => el.scrollLeft),
        ).toBeGreaterThan(0);

      await catalogFrame.scrollIntoViewIfNeeded();
      await catalogFrame.evaluate((el) => {
        el.scrollTop = 240;
        el.scrollLeft = 100;
      });
      await expect
        .poll(async () => {
          const frame = await catalogFrame.boundingBox();
          const header = await catalogFrame.locator("th").first().boundingBox();
          return Math.abs(header!.y - frame!.y - 1);
        })
        .toBeLessThan(2);

      const owner = page.locator("#page-owner");
      await owner.scrollIntoViewIfNeeded();
      await owner.evaluate((el) => {
        el.scrollTop = 160;
      });
      await expect(pageFrame).toHaveCSS("overflow-y", "visible");
      await expect
        .poll(async () => {
          const frame = await owner.boundingBox();
          const header = await owner.locator("th").first().boundingBox();
          return Math.abs(header!.y - frame!.y);
        })
        .toBeLessThan(2);
      await page.screenshot({ path: testInfo.outputPath("table-frames.png") });
    });
  }
}
