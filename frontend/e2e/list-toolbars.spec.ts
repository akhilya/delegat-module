import { expect, test, type Page } from "@playwright/test";

const connection = {
  id: 8,
  name: "Marketplace connection",
  adapterKey: "ozon",
  isActive: true,
};
const routes = [
  "/notes",
  "/barcodes",
  "/products",
  "/orders",
  "/orders/8/pack",
  "/settings/adapter-logs",
  "/settings/admin/error-logs",
  "/settings/admin/api?tab=logs",
  "/adapter/8?tab=products",
];

async function mockCabinet(page: Page, dark = false, locale = "ru") {
  const requests: URL[] = [];
  await page.addInitScript(
    ({ dark, locale }) => {
      localStorage.setItem("mobile-warning-dismissed", "1");
      localStorage.setItem("vueuse-color-scheme", dark ? "dark" : "light");
      localStorage.setItem("delegat.locale", locale);
    },
    { dark, locale },
  );
  // All API calls, including order refreshes, are local fixtures. No live instance is used.
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    requests.push(url);
    const path = url.pathname.replace(/^\/api/, "");
    let body: unknown = [];
    if (path === "/health") body = { status: "healthy", version: "1.6.0" };
    else if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (path === "/auth/me")
      body = {
        id: 1,
        userName: "Long account display name",
        selectedStoreId: 1,
        permissions: [
          "catalog.manage",
          "marketplace.manage",
          "orders.manage",
          "settings.platform",
          "api.access",
        ],
        aiEnabled: false,
        logErrorsEnabled: true,
        logAdapterHttpEnabled: true,
        logApiRequestsEnabled: true,
        publicApiEnabled: true,
      };
    else if (path === "/users/me/memberships")
      body = [{ id: 1, name: "Test store" }];
    else if (path === "/docs/catalog")
      body = {
        groups: [
          {
            text: "Workspace",
            items: [
              "notes",
              "barcodes",
              "products",
              "orders",
              "settings",
              "public-api",
              "adapters",
            ].map((slug) => ({ text: slug, slug: `workspace/${slug}` })),
          },
        ],
      };
    else if (path === "/marketplace/connections") body = [connection];
    else if (path === "/marketplace/adapters")
      body = [
        {
          key: "ozon",
          name: "Ozon",
          capabilities: "GetOrders, ImportAllProducts, ExportProduct",
        },
      ];
    else if (path === "/marketplace/connections/8") body = connection;
    else if (path === "/marketplace/connections/8/capabilities")
      body = { getOrders: true, importAllProducts: true, exportProduct: true };
    else if (path === "/error-logs") body = [];
    else if (
      path === "/products" ||
      path === "/products/barcodes" ||
      path === "/notes" ||
      path.includes("logs") ||
      path === "/marketplace/orders" ||
      path.endsWith("/orders") ||
      path.endsWith("/products")
    ) {
      body = {
        items: [],
        totalCount: 0,
        total: 0,
        hasMore: false,
        lastPage: true,
      };
    }
    await route.fulfill({ json: body });
  });
  return requests;
}

for (const width of [390, 768, 1280, 1920]) {
  test(`all list tools live in the navbar without collisions at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    await mockCabinet(page);
    const errors: string[] = [];
    page.on("pageerror", (error) => errors.push(error.message));
    for (const path of routes) {
      await page.goto(path);
      const nav = page.locator('[data-slot="app-navbar"]');
      const searchOwner = path.startsWith("/adapter/")
        ? page.locator('[data-slot="page-scroll"]')
        : nav;
      const search = searchOwner.getByRole("searchbox");
      await expect(search, path).toBeVisible();
      await expect(
        searchOwner.locator('[data-slot="page-list-toolbar"]'),
        path,
      ).toHaveCount(1);
      await expect(
        page.locator('[data-slot="page-scroll"]').getByRole("searchbox"),
        path,
      ).toHaveCount(path.startsWith("/adapter/") ? 1 : 0);
      const tools = await nav
        .locator('[data-slot="navbar-tools"]')
        .boundingBox();
      const actions = await nav
        .locator('[data-slot="navbar-actions"]')
        .boundingBox();
      const navbar = await nav.boundingBox();
      expect(tools!.width, path).toBeGreaterThan(200);
      const overlapX =
        Math.min(tools!.x + tools!.width, actions!.x + actions!.width) -
        Math.max(tools!.x, actions!.x);
      const overlapY =
        Math.min(tools!.y + tools!.height, actions!.y + actions!.height) -
        Math.max(tools!.y, actions!.y);
      expect(overlapX <= 1 || overlapY <= 1, path).toBe(true);
      expect(tools!.x + tools!.width, path).toBeLessThanOrEqual(
        navbar!.x + navbar!.width + 1,
      );
      const measuredHeight = await page
        .locator('[data-slot="page-scroll"] > div')
        .first()
        .evaluate((element) =>
          parseFloat(
            getComputedStyle(element).getPropertyValue("--app-navbar-height"),
          ),
        );
      expect(Math.abs(measuredHeight - navbar!.height), path).toBeLessThan(2);
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
        path,
      ).toBe(true);
    }
    expect(errors).toEqual([]);
  });
}

for (const dark of [false, true]) {
  test(`notes filters support keyboard, reset and narrow panels (${dark ? "dark" : "light"})`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    const requests = await mockCabinet(page, dark);
    await page.goto("/notes");
    const nav = page.locator('[data-slot="app-navbar"]');
    const search = nav.getByRole("searchbox");
    await search.fill("catalog");
    await search.press("Enter");
    const filters = nav.getByRole("button", { name: /^Фильтры/ });
    await filters.focus();
    await filters.press("Enter");
    const panel = page.locator('[data-slot="dropdown-menu-content"]');
    await expect(panel).toBeVisible();
    const importance = panel.getByRole("menuitem", {
      name: "Важность",
      exact: true,
    });
    await importance.focus();
    await importance.press("ArrowRight");
    await page
      .getByRole("menuitemradio", { name: "Высокая важность", exact: true })
      .click();
    await expect(filters).toHaveAttribute("data-active", "true");
    await page.screenshot({ path: testInfo.outputPath("filters.png") });
    await expect
      .poll(() =>
        requests.some(
          (url) =>
            url.pathname.endsWith("/notes") &&
            url.searchParams.get("importance") === "High" &&
            url.searchParams.get("search") === "catalog",
        ),
      )
      .toBe(true);
    await filters.click();
    await panel
      .getByRole("menuitem", { name: "Сбросить", exact: true })
      .click();
    await expect(filters).not.toHaveAttribute("data-active", "true");
    await expect(search).toHaveValue("catalog");
    await filters.click();
    await page.keyboard.press("Escape");
    await expect(panel).toBeHidden();
    await expect(filters).toBeFocused();
    await page.screenshot({ path: testInfo.outputPath("navbar.png") });
  });
}

test("settings navigation removes hidden API tools and retains log search", async ({
  page,
}) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await mockCabinet(page, true, "en");
  await page.goto("/settings/admin/api?tab=logs");
  const nav = page.locator('[data-slot="app-navbar"]');
  const search = nav.getByRole("searchbox");
  await expect(search).toBeVisible();
  await search.fill("/products");
  await nav.getByRole("tab").first().click();
  await expect(nav.getByRole("searchbox")).toHaveCount(0);
  await nav.getByRole("tab").last().click();
  await expect(search).toHaveValue("/products");
  const errorLogs = page.locator('a[href="/settings/admin/error-logs"]');
  if (!(await errorLogs.isVisible()))
    await page.getByRole("button", { name: "Settings", exact: true }).click();
  await errorLogs.click();
  await expect(nav.getByRole("searchbox")).toHaveCount(1);
  await expect(nav.getByRole("searchbox")).toHaveValue("");
  expect(errors).toEqual([]);
});

test("catalog category filters keep their controlled open state and query", async ({
  page,
}) => {
  const requests = await mockCabinet(page, false, "en");
  await page.goto("/products");
  const nav = page.locator('[data-slot="app-navbar"]');
  await nav.getByRole("button", { name: "Filters", exact: true }).click();
  const panel = page.locator('[data-slot="dropdown-menu-content"]');
  await panel
    .getByRole("menuitemradio", { name: "No category", exact: true })
    .click();
  await expect(panel).toBeHidden();
  await expect(nav.getByRole("button", { name: /^Filters/ })).toHaveAttribute(
    "data-active",
    "true",
  );
  await expect
    .poll(() =>
      requests.some(
        (url) =>
          url.pathname === "/api/products" &&
          url.searchParams.get("categoryId") === "-1",
      ),
    )
    .toBe(true);
});

test("connection keeps primary tabs in the navbar and product tools left-aligned", async ({
  page,
}) => {
  await mockCabinet(page, false, "en");
  await page.goto("/adapter/8?tab=products");
  const nav = page.locator('[data-slot="app-navbar"]');
  await expect(nav.getByRole("tab")).toHaveCount(3);
  await expect(nav.getByRole("searchbox")).toHaveCount(0);
  const content = page.locator('[data-slot="page-scroll"]');
  const search = content.getByRole("searchbox");
  await expect(search).toBeVisible();
  await search.fill("ADAPTER-SKU");
  await content
    .getByRole("tab", { name: "Products to publish", exact: true })
    .click();
  await expect(search).toHaveValue("");
  await search.fill("LOCAL-SKU");
  await content
    .getByRole("tab", { name: "Adapter products", exact: true })
    .click();
  await expect(search).toHaveValue("ADAPTER-SKU");
});

test("filters and Help remain contained after panel and viewport resizing", async ({
  page,
}, testInfo) => {
  await mockCabinet(page);
  await page.setViewportSize({ width: 1920, height: 900 });
  await page.goto("/notes");
  const nav = page.locator('[data-slot="app-navbar"]');
  await expect(
    nav.getByRole("button", { name: "Нужна помощь?", exact: true }),
  ).toBeVisible();
  const tools = nav.locator('[data-slot="navbar-tools"]');
  const title = nav.locator('[data-slot="navbar-title"]');
  const navbarBox = (await nav.boundingBox())!;
  const toolbarBox = (await nav
    .locator('[data-slot="page-list-toolbar"]')
    .boundingBox())!;
  const gutters = await nav.evaluate((element) => ({
    left: parseFloat(getComputedStyle(element).paddingLeft),
    right: parseFloat(getComputedStyle(element).paddingRight),
  }));
  expect(
    Math.abs(
      toolbarBox.x +
        toolbarBox.width / 2 -
        navbarBox.x -
        (navbarBox.width + gutters.left - gutters.right) / 2,
    ),
  ).toBeLessThan(2);
  expect(toolbarBox.width).toBeLessThanOrEqual(448);
  await expect
    .poll(async () =>
      Math.abs((await tools.boundingBox())!.y - (await title.boundingBox())!.y),
    )
    .toBeLessThan(20);
  // Resize the actual panel at a fixed viewport: container queries, not media queries.
  await nav.evaluate((element) => {
    element.style.width = "750px";
  });
  await expect
    .poll(
      async () =>
        (await tools.boundingBox())!.y - (await title.boundingBox())!.y,
    )
    .toBeGreaterThan(50);
  await expect(nav.locator(".navbar-help-label")).toBeHidden();
  await nav.evaluate((element) => {
    element.style.width = "";
  });
  await page.setViewportSize({ width: 320, height: 700 });
  await nav.getByRole("button", { name: "Фильтры", exact: true }).click();
  const panel = page.locator('[data-slot="dropdown-menu-content"]');
  await expect(panel).toBeVisible();
  const box = await panel.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(320);
  await page.screenshot({ path: testInfo.outputPath("mobile-filters.png") });
});
