import { expect, test } from "@playwright/test";

for (const width of [1280, 390]) {
  test(`tools hub and full-width schedule form at ${width}px`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.addInitScript(() =>
      localStorage.setItem("mobile-warning-dismissed", "1"),
    );
    let tasks: Record<string, unknown>[] = [];
    let saved: Record<string, unknown> | null = null;
    // Every API call is mocked; creating this fixture cannot schedule real marketplace work.
    await page.route("**/api/**", async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname.replace(/^\/api/, "");
      let body: unknown = [];
      if (path === "/health") body = { status: "healthy", version: "1.6.0" };
      else if (path === "/auth/isAuthenticated")
        body = { isAuthenticated: true };
      else if (path === "/auth/me")
        body = {
          id: 1,
          userName: "Test manager",
          selectedStoreId: 1,
          permissions: ["marketplace.manage"],
          aiEnabled: false,
        };
      else if (path === "/users/me/memberships")
        body = [{ id: 1, name: "Test store" }];
      else if (path === "/tools")
        body = [
          {
            id: "catalog-connect",
            mode: "page",
            titleKey: "pages.toolsPage.catalogConnectTitle",
            path: "/tools/catalog-connect",
          },
          {
            id: "catalog-scrape",
            mode: "page",
            titleKey: "pages.toolsPage.catalogScrapeTitle",
            path: "/tools/catalog-scrape",
          },
        ];
      else if (path === "/scheduled-tasks/actions")
        body = [{ id: "import-products", name: "Import products" }];
      else if (path === "/scheduled-tasks/connections")
        body = [{ id: 8, name: "Test connection" }];
      else if (path === "/reference/timezones")
        body = [{ value: "UTC", label: "UTC" }];
      else if (path === "/scheduled-tasks") {
        if (request.method() === "POST") {
          saved = request.postDataJSON();
          tasks = [{ id: 1, ...saved }];
          body = tasks[0];
        } else body = tasks;
      }
      await route.fulfill({ json: body });
    });
    await page.goto("/tools/scheduled-tasks");
    await expect(page).toHaveURL(/\/tools\?tab=schedules$/);
    const nav = page.locator('[data-slot="app-navbar"]');
    await expect(
      nav.getByRole("tab", { name: "Расписания", exact: true }),
    ).toHaveAttribute("data-state", "active");
    await expect(page.locator('[data-slot="app-navbar"]')).toHaveCount(1);
    await page
      .getByRole("button", { name: "Создать расписание", exact: true })
      .click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog.locator('[data-slot="inset-group"]')).toHaveCount(0);
    const fields = dialog.locator(
      'input:not([type="hidden"]):not([type="checkbox"]), [data-slot="select-trigger"], textarea',
    );
    for (const field of await fields.all()) {
      await expect
        .poll(() =>
          field.evaluate((element) =>
            Math.abs(
              element.getBoundingClientRect().width -
                element.parentElement!.getBoundingClientRect().width,
            ),
          ),
        )
        .toBeLessThan(2);
    }
    await dialog.locator("#scheduled-task-name").fill("Catalog import");
    await dialog.locator("#scheduled-task-connection").click();
    await page
      .getByRole("option", { name: "Test connection", exact: true })
      .click();
    await dialog.getByRole("switch").uncheck();
    await page.screenshot({ path: testInfo.outputPath("schedule-dialog.png") });
    await dialog
      .getByRole("button", { name: "Сохранить", exact: true })
      .click();
    await expect(dialog).toBeHidden();
    await expect(
      page.getByText("Catalog import", { exact: true }),
    ).toBeVisible();
    expect(saved).toMatchObject({
      actionId: "import-products",
      isEnabled: false,
      parameters: { connectionId: 8, maxProducts: 50 },
    });
    await nav.getByRole("tab", { name: "Инструменты", exact: true }).click();
    await expect(page).toHaveURL(/\/tools$/);
    await expect(
      page.getByText("Связать каталоги", { exact: true }),
    ).toBeVisible();
    await expect(
      page.getByText("Собрать с витрины", { exact: true }),
    ).toHaveCount(0);
    await page.goto("/tools/catalog-scrape");
    await expect(page).toHaveURL(/\/tools$/);
  });
}
