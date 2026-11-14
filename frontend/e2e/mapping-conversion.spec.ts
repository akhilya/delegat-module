import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.route("**/api/**", (route) => route.fulfill({ json: {} }));
  await page.goto("/signIn");
});

test("collapsed division connects to the value input, not the overlapping divisor", async ({
  page,
}) => {
  await page.evaluate(async () => {
    const { createApp, h, markRaw } =
      await import("/node_modules/.vite/deps/vue.js");
    const { VueFlow } =
      await import("/node_modules/.vite/deps/@vue-flow_core.js");
    const { default: Modifier } =
      await import("/src/components/marketplace/graph/nodes/ModifierNode.vue");
    const { i18n } = await import("/src/i18n/index.js");
    await import("/node_modules/@vue-flow/core/dist/style.css");
    const host = document.createElement("div");
    host.style.cssText = "position:fixed;inset:0;background:white;z-index:9999";
    document.body.append(host);
    createApp({
      render: () =>
        h(VueFlow, {
          nodes: [
            {
              id: "source",
              type: "input",
              position: { x: 100, y: 100 },
              data: { label: "110" },
            },
            {
              id: "divide",
              type: "modifier",
              position: { x: 400, y: 100 },
              data: {
                modifierType: "divide",
                params: { divisor: 10 },
                collapsed: true,
              },
            },
          ],
          nodeTypes: { modifier: markRaw(Modifier) },
          onConnect: (connection: unknown) => {
            (window as any).testConnection = connection;
          },
        }),
    })
      .use(i18n)
      .mount(host);
  });
  const source = page.locator('.vue-flow__node[data-id="source"] .source');
  const value = page.locator(
    '.vue-flow__node[data-id="divide"] [data-handleid="in"]',
  );
  const divisor = page.locator(
    '.vue-flow__node[data-id="divide"] [data-handleid="p:divisor"]',
  );
  await expect(value).toBeVisible();
  await expect(divisor).toBeHidden();
  const a = (await source.boundingBox())!;
  const b = (await value.boundingBox())!;
  await page.mouse.move(a.x + a.width / 2, a.y + a.height / 2);
  await page.mouse.down();
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 20 });
  await page.mouse.up();
  await expect
    .poll(() =>
      page.evaluate(() => (window as any).testConnection?.targetHandle),
    )
    .toBe("in");
});

test("unit conversion defaults to automatic and allows manual unit selection", async ({
  page,
}) => {
  await page.evaluate(async () => {
    const { createApp, h, ref } =
      await import("/node_modules/.vite/deps/vue.js");
    const { default: Inspector } =
      await import("/src/components/marketplace/graph/NodeInspector.vue");
    const { i18n } = await import("/src/i18n/index.js");
    const host = document.createElement("div");
    host.style.cssText =
      "position:fixed;inset:0;background:white;z-index:9999;width:380px";
    document.body.append(host);
    const node = ref({
      id: "convert",
      type: "modifier",
      data: {
        modifierType: "unitConversion",
        params: { fromUnit: "kg", toUnit: "g" },
      },
    });
    (window as any).conversionNode = node;
    createApp({
      render: () =>
        h(Inspector, {
          node: node.value,
          onUpdate: (patch: object) => {
            node.value.data = { ...node.value.data, ...patch };
          },
        }),
    })
      .use(i18n)
      .mount(host);
  });
  const checkbox = page.getByRole("checkbox", {
    name: "Определять автоматически",
  });
  await expect(checkbox).toBeChecked();
  await expect(page.locator("#conversion-fromUnit")).toBeDisabled();
  await checkbox.click();
  await expect(checkbox).not.toBeChecked();
  await expect(page.locator("#conversion-fromUnit")).toBeEnabled();
  await page.locator("#conversion-fromUnit").click();
  await page.getByRole("option", { name: "Миллиметр mm", exact: true }).click();
  await expect
    .poll(() =>
      page.evaluate(
        () => (window as any).conversionNode.value.data.params.fromUnit,
      ),
    )
    .toBe("mm");
  await expect
    .poll(() =>
      page.evaluate(
        () => (window as any).conversionNode.value.data.params.toUnit,
      ),
    )
    .toBe("g");
  await page.locator("#conversion-toUnit").click();
  await page.getByRole("option", { name: "Сантиметр cm", exact: true }).click();
  await expect
    .poll(() =>
      page.evaluate(
        () => (window as any).conversionNode.value.data.params.toUnit,
      ),
    )
    .toBe("cm");
  await checkbox.click();
  await expect(page.locator("#conversion-fromUnit")).toBeDisabled();
  await expect(page.locator("#conversion-toUnit")).toBeDisabled();
});
