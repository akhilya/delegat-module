import { expect, test, type Page } from "@playwright/test";

async function mountEditor(page: Page, canManage = true, sameCategory = false) {
  await page.goto("/signIn");
  await page.evaluate(
    async ({ canManage, sameCategory }) => {
      const { createApp, h, ref } =
        await import("/node_modules/.vite/deps/vue.js");
      const { default: Editor } =
        await import("/src/components/products/bulkEditDialog.vue");
      const { createStore } = await import("/src/store/index.js");
      const { i18n } = await import("/src/i18n/index.js");
      const host = document.createElement("div");
      document.body.append(host);
      (window as any).bulkEditEvents = { saved: 0, mappingsSaved: 0 };
      const open = ref(true);
      createApp({
        render: () =>
          h(Editor, {
            modelValue: open.value,
            // Explicit IDs include products beyond the loaded page.
            productIds: [1, 2, 3],
            selectedProducts: [
              { id: 1, categoryId: 10 },
              { id: 2, categoryId: sameCategory ? 10 : 20 },
            ],
            connections: [{ id: 1, name: "Test adapter" }],
            canManageAdapters: canManage,
            categoryAttributes: [{ id: 8, name: "Material", type: "Text" }],
            storeAttributes: [{ id: 9, name: "Global note", type: "Text" }],
            "onUpdate:modelValue": (value: boolean) => {
              open.value = value;
            },
            onSaved: () => {
              (window as any).bulkEditEvents.saved++;
            },
            onMappingsSaved: () => {
              (window as any).bulkEditEvents.mappingsSaved++;
            },
          }),
      })
        .use(createStore())
        .use(i18n)
        .mount(host);
    },
    { canManage, sameCategory },
  );
}

for (const width of [1280, 390]) {
  test(`bulk edit assigns mappings and preserves field drafts at ${width}px`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width, height: 844 });
    const requests: string[] = [];
    const mappingWrites: unknown[] = [];
    const fieldWrites: unknown[] = [];
    let attempts = 0;
    let releaseSave!: () => void;
    const saveGate = new Promise<void>((resolve) => {
      releaseSave = resolve;
    });
    // No live catalog or configured marketplace is contacted.
    await page.route("**/api/**", async (route) => {
      const path = new URL(route.request().url()).pathname.replace(
        /^\/api/,
        "",
      );
      requests.push(path);
      let body: unknown = {};
      if (path.endsWith("/resolve-mappings")) {
        expect(route.request().postDataJSON()).toEqual({
          productIds: [1, 2, 3],
        });
        body = {
          products: [
            {
              productId: 1,
              categoryId: 10,
              categoryName: "Figures",
              availableMappings: [
                { id: 11, name: "Toys" },
                { id: 12, name: "Collectibles" },
              ],
            },
            {
              productId: 2,
              categoryId: 20,
              categoryName: "Books",
              availableMappings: [{ id: 21, name: "Reading" }],
            },
            {
              productId: 3,
              categoryId: null,
              categoryName: null,
              availableMappings: [],
            },
          ],
        };
      } else if (path.endsWith("/save-mapping-selections")) {
        mappingWrites.push(route.request().postDataJSON());
        if (++attempts === 1) {
          await route.fulfill({
            status: 400,
            json: {
              code: "mapping_not_found",
              message: "RAW_PRIVATE_DIAGNOSTIC",
            },
          });
          return;
        }
        await saveGate;
        body = { updated: 2 };
      } else if (path === "/products/bulk") {
        fieldWrites.push(route.request().postDataJSON());
        body = { updatedCount: 3 };
      }
      await route.fulfill({ json: body });
    });
    await mountEditor(page);
    const dialog = page.getByRole("dialog");
    await page.locator("#bulk-enable-name").check();
    await dialog
      .getByRole("textbox", { name: "Название", exact: true })
      .fill("Unsaved title");
    await dialog
      .getByRole("tab", { name: "Глобальные атрибуты", exact: true })
      .click();
    await page.locator("#bulk-enable-store-9").check();
    await dialog
      .getByRole("textbox", { name: "Global note", exact: true })
      .fill("Unsaved attribute");
    expect(requests.some((path) => path.includes("resolve-mappings"))).toBe(
      false,
    );

    await dialog.getByRole("tab", { name: "Маппинги", exact: true }).click();
    const run = dialog.getByRole("button", {
      name: "Назначить маппинг",
      exact: true,
    });
    await expect(run).toBeDisabled();
    await dialog
      .getByRole("combobox", { name: "Адаптер", exact: true })
      .click();
    await page
      .getByRole("option", { name: "Test adapter", exact: true })
      .click();
    const figures = dialog.getByRole("combobox", {
      name: "Figures",
      exact: true,
    });
    await figures.click();
    await page
      .getByRole("option", { name: "Collectibles", exact: true })
      .click();
    await expect(
      dialog.getByRole("combobox", { name: "Books", exact: true }),
    ).toHaveText(/Reading/);
    // Tab switches retain the mapping draft without a second resolver request.
    await dialog.getByRole("tab", { name: "Поля товара", exact: true }).click();
    await expect(
      dialog.getByRole("textbox", { name: "Название", exact: true }),
    ).toHaveValue("Unsaved title");
    await dialog.getByRole("tab", { name: "Маппинги", exact: true }).click();
    await expect(figures).toHaveText(/Collectibles/);
    expect(
      requests.filter((path) => path.endsWith("/resolve-mappings")),
    ).toHaveLength(1);
    await page.screenshot({
      path: testInfo.outputPath("bulk-edit-mappings.png"),
    });

    await run.click();
    await expect(
      page.getByText("Сопоставление категории не найдено", { exact: true }),
    ).toBeVisible();
    await expect(page.getByText("RAW_PRIVATE_DIAGNOSTIC")).toHaveCount(0);
    await expect(run).toBeEnabled();
    await expect(figures).toHaveText(/Collectibles/);
    await run.click();
    await expect(run).toBeDisabled();
    await expect(
      dialog.getByRole("tab", { name: "Поля товара", exact: true }),
    ).toBeDisabled();
    await page.keyboard.press("Escape");
    await expect(dialog).toBeVisible();
    releaseSave();
    await expect(run).toBeEnabled();
    expect(await page.evaluate(() => (window as any).bulkEditEvents)).toEqual({
      saved: 0,
      mappingsSaved: 1,
    });
    expect(fieldWrites).toEqual([]);
    const expected = {
      selections: [
        { productId: 1, categoryMappingId: 12 },
        { productId: 2, categoryMappingId: 21 },
      ],
    };
    expect(mappingWrites).toEqual([expected, expected]);

    await dialog
      .getByRole("tab", { name: "Глобальные атрибуты", exact: true })
      .click();
    await expect(
      dialog.getByRole("textbox", { name: "Global note", exact: true }),
    ).toHaveValue("Unsaved attribute");
    await dialog.getByRole("tab", { name: "Поля товара", exact: true }).click();
    await expect(
      dialog.getByRole("textbox", { name: "Название", exact: true }),
    ).toHaveValue("Unsaved title");
    await dialog
      .getByRole("button", { name: "Сохранить", exact: true })
      .click();
    await expect(dialog).toBeHidden();
    expect(fieldWrites).toEqual([
      {
        productIds: [1, 2, 3],
        updates: {
          name: "Unsaved title",
          storeAttributeValues: [
            { storeAttributeId: 9, value: "Unsaved attribute" },
          ],
        },
      },
    ]);
    expect(
      requests.some((path) =>
        /export|capabilities|resolve-product-links/.test(path),
      ),
    ).toBe(false);
  });
}

test("bulk edit hides mapping assignment without marketplace permission", async ({
  page,
}) => {
  const requests: string[] = [];
  await page.route("**/api/**", (route) => {
    requests.push(new URL(route.request().url()).pathname);
    return route.fulfill({ json: {} });
  });
  await mountEditor(page, false, true);
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(
    page.getByRole("tab", { name: "Маппинги", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("tab", { name: "Атрибуты категории", exact: true }),
  ).toBeVisible();
  expect(requests.some((path) => path.includes("/marketplace/"))).toBe(false);
});
