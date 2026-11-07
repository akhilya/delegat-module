import { expect, test, type Page } from "@playwright/test";

const products = [
  {
    productId: 1,
    productName: "First",
    categoryId: 10,
    categoryName: "Figures",
    selectedMappingId: 11,
    availableMappings: [
      { id: 11, name: "Toys" },
      { id: 12, name: "Collectibles" },
    ],
  },
  {
    productId: 2,
    productName: "Second",
    categoryId: 10,
    categoryName: "Figures",
    selectedMappingId: 12,
    availableMappings: [
      { id: 11, name: "Toys" },
      { id: 12, name: "Collectibles" },
    ],
  },
  {
    productId: 3,
    productName: "Third",
    categoryId: 20,
    categoryName: "Books",
    selectedMappingId: null,
    availableMappings: [{ id: 21, name: "Reading" }],
  },
  {
    productId: 4,
    productName: "Fourth",
    categoryId: null,
    categoryName: null,
    selectedMappingId: null,
    availableMappings: [],
  },
  {
    productId: 5,
    productName: "Fifth",
    categoryId: 30,
    categoryName: "Unmapped",
    selectedMappingId: null,
    availableMappings: [],
  },
];

async function mountDialog(page: Page, ids = [1, 2, 3, 4, 5]) {
  await page.goto("/signIn");
  await page.evaluate(async (ids) => {
    const { createApp, h, ref } =
      await import("/node_modules/.vite/deps/vue.js");
    const { default: Dialog } =
      await import("/src/components/products/BulkAdapterDialog.vue");
    const { i18n } = await import("/src/i18n/index.js");
    const host = document.createElement("div");
    document.body.append(host);
    (window as any).mappingSaved = 0;
    const open = ref(true);
    createApp({
      render: () =>
        h(Dialog, {
          modelValue: open.value,
          selectedProducts: ids.map((id) => ({ id })),
          "onUpdate:modelValue": (value: boolean) => {
            open.value = value;
          },
          onSaved: () => {
            (window as any).mappingSaved++;
          },
        }),
    })
      .use(i18n)
      .mount(host);
  }, ids);
}

async function selectAdapter(page: Page, name = "Test adapter") {
  await page.getByRole("combobox", { name: "Адаптер", exact: true }).click();
  await page.getByRole("option", { name, exact: true }).click();
}

for (const width of [1280, 390]) {
  test(`bulk mapping assignment, errors and no export at ${width}px`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width, height: 844 });
    const requests: string[] = [];
    const saves: unknown[] = [];
    let attempts = 0;
    let releaseSave!: () => void;
    const saveGate = new Promise<void>((resolve) => {
      releaseSave = resolve;
    });
    // All API traffic is mocked. Never export to a configured marketplace.
    await page.route("**/api/**", async (route) => {
      const path = new URL(route.request().url()).pathname.replace(
        /^\/api/,
        "",
      );
      requests.push(path);
      let body: unknown = {};
      if (path === "/marketplace/connections")
        body = [{ id: 1, name: "Test adapter" }];
      else if (path.endsWith("/capabilities"))
        body = { capabilities: { exportProductBulk: true } };
      else if (path.endsWith("/resolve-mappings")) {
        expect(route.request().postDataJSON()).toEqual({
          productIds: [1, 2, 3, 4, 5],
        });
        body = { products };
      } else if (path.endsWith("/save-mapping-selections")) {
        saves.push(route.request().postDataJSON());
        if (++attempts === 1) {
          await route.fulfill({
            status: 400,
            json: {
              code: "mapping_category_mismatch",
              message: "RAW_ERROR_MUST_NOT_BE_SHOWN",
            },
          });
          return;
        }
        await saveGate;
        body = { updated: 3 };
      }
      await route.fulfill({ json: body });
    });
    await mountDialog(page);
    await selectAdapter(page);
    const dialog = page.getByRole("dialog");
    await expect(
      dialog.getByRole("option", { name: /Экспортировать товары/ }),
    ).toBeVisible();
    expect(requests.some((path) => path.endsWith("/resolve-mappings"))).toBe(
      false,
    );
    await dialog.getByRole("option", { name: /Назначить маппинг/ }).click();
    const run = dialog.getByRole("button", {
      name: "Назначить маппинг",
      exact: true,
    });
    const figures = dialog.getByRole("combobox", {
      name: "Figures",
      exact: true,
    });
    await expect(figures).toBeVisible();
    await expect(run).toBeDisabled();
    await expect(
      dialog.getByRole("combobox", { name: "Books", exact: true }),
    ).toHaveText(/Reading/);
    await expect(
      dialog.getByRole("combobox", { name: "Unmapped", exact: true }),
    ).toBeDisabled();
    await expect(dialog.getByRole("status")).toContainText(
      "будут пропущены: 2",
    );
    await figures.click();
    await page
      .getByRole("option", { name: "Collectibles", exact: true })
      .click();
    await expect(run).toBeEnabled();
    await page.screenshot({ path: testInfo.outputPath("bulk-mapping.png") });
    await run.click();
    await expect(run).toBeEnabled();
    await expect(dialog).toBeVisible();
    await expect(figures).toHaveText(/Collectibles/);
    await expect(page.getByText("RAW_ERROR_MUST_NOT_BE_SHOWN")).toHaveCount(0);
    await expect(
      page.getByText(
        "Категория товара изменилась. Загрузите доступные маппинги заново и повторите выбор.",
      ),
    ).toBeVisible();
    await run.click();
    await expect(run).toBeDisabled();
    await expect(figures).toBeDisabled();
    await expect(
      dialog.getByRole("combobox", { name: "Адаптер", exact: true }),
    ).toBeDisabled();
    await page.keyboard.press("Escape");
    await expect(dialog).toBeVisible();
    releaseSave();
    await expect(dialog).toBeHidden();
    expect(await page.evaluate(() => (window as any).mappingSaved)).toBe(1);
    const expected = {
      selections: [
        { productId: 1, categoryMappingId: 12 },
        { productId: 2, categoryMappingId: 12 },
        { productId: 3, categoryMappingId: 21 },
      ],
    };
    expect(saves).toEqual([expected, expected]);
    expect(
      requests.some((path) => /export|resolve-product-links/.test(path)),
    ).toBe(false);
  });
}

test("mapping assignment is local and ignores a stale response after switching adapters", async ({
  page,
}) => {
  let releaseOld!: () => void;
  const oldGate = new Promise<void>((resolve) => {
    releaseOld = resolve;
  });
  let oldStarted!: () => void;
  const oldRequested = new Promise<void>((resolve) => {
    oldStarted = resolve;
  });
  const saves: { path: string; body: unknown }[] = [];
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname.replace(/^\/api/, "");
    let body: unknown = {};
    if (path === "/marketplace/connections")
      body = [
        { id: 1, name: "Test adapter" },
        { id: 2, name: "Other adapter" },
      ];
    else if (path.endsWith("/capabilities")) body = { capabilities: {} };
    else if (path === "/marketplace/connections/1/resolve-mappings") {
      oldStarted();
      await oldGate;
      body = { products: [products[0]] };
    } else if (path === "/marketplace/connections/2/resolve-mappings")
      body = {
        products: [
          {
            ...products[0],
            availableMappings: [{ id: 99, name: "Other mapping" }],
            selectedMappingId: null,
          },
        ],
      };
    else if (path.endsWith("/save-mapping-selections")) {
      saves.push({ path, body: route.request().postDataJSON() });
      body = { updated: 1 };
    }
    await route.fulfill({ json: body });
  });
  await mountDialog(page, [1]);
  await selectAdapter(page);
  await oldRequested;
  const run = page.getByRole("button", {
    name: "Назначить маппинг",
    exact: true,
  });
  await expect(run).toBeDisabled();
  await selectAdapter(page, "Other adapter");
  const figures = page.getByRole("combobox", { name: "Figures", exact: true });
  await expect(figures).toHaveText(/Other mapping/);
  const oldResponse = page.waitForResponse((response) =>
    response.url().endsWith("/connections/1/resolve-mappings"),
  );
  releaseOld();
  await oldResponse;
  await expect(figures).toHaveText(/Other mapping/);
  await run.click();
  await expect(page.getByRole("dialog")).toBeHidden();
  expect(saves).toEqual([
    {
      path: "/marketplace/connections/2/save-mapping-selections",
      body: { selections: [{ productId: 1, categoryMappingId: 99 }] },
    },
  ]);
});

for (const scenario of ["unmapped", "incomplete", "failed"]) {
  test(`does not assign mappings when resolution is ${scenario}`, async ({
    page,
  }) => {
    let calls = 0;
    let writes = 0;
    await page.route("**/api/**", async (route) => {
      const path = new URL(route.request().url()).pathname.replace(
        /^\/api/,
        "",
      );
      let body: unknown = {};
      if (path === "/marketplace/connections")
        body = [{ id: 1, name: "Test adapter" }];
      else if (path.endsWith("/capabilities")) body = { capabilities: {} };
      else if (path.endsWith("/resolve-mappings")) {
        calls++;
        if (scenario === "failed" && calls === 1) {
          await route.fulfill({
            status: 400,
            json: { code: "mapping_not_found" },
          });
          return;
        }
        body = {
          products:
            scenario === "unmapped"
              ? [{ ...products[0], availableMappings: [] }]
              : scenario === "incomplete"
                ? []
                : [products[2]],
        };
      } else if (path.endsWith("/save-mapping-selections")) writes++;
      await route.fulfill({ json: body });
    });
    await mountDialog(page, scenario === "failed" ? [3] : [1]);
    await selectAdapter(page);
    const run = page.getByRole("button", {
      name: "Назначить маппинг",
      exact: true,
    });
    await expect(run).toBeDisabled();
    if (scenario === "unmapped")
      await expect(
        page.getByRole("combobox", { name: "Figures", exact: true }),
      ).toBeDisabled();
    else {
      await expect(page.getByRole("dialog").getByRole("alert")).toBeVisible();
      if (scenario === "failed") {
        await page
          .getByRole("button", { name: "Повторить", exact: true })
          .click();
        await expect(run).toBeEnabled();
        expect(calls).toBe(2);
      }
    }
    expect(writes).toBe(0);
  });
}
