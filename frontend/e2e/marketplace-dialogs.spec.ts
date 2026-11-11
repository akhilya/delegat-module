import { expect, test } from "@playwright/test";

async function mockMarketplaceApi(page, { localCategories = [] } = {}) {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.addInitScript(() => {
    localStorage.setItem("mobile-warning-dismissed", "1");
    localStorage.setItem("delegat.locale", "ru");
  });

  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname.replace(/^\/api/, "");
    let body: unknown = [];

    if (path === "/health") body = { status: "healthy", version: "1.6.0" };
    else if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
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
    else if (path === "/marketplace/adapters")
      body = [
        {
          key: "test-adapter",
          displayName: "Test adapter",
          credentialsSchema: [],
        },
      ];
    else if (path === "/marketplace/connections") body = [];
    else if (path === "/marketplace/connections/1")
      body = {
        id: 1,
        name: "Test connection",
        adapterKey: "test-adapter",
        isActive: true,
        publicCredentials: {},
      };
    else if (path === "/marketplace/connections/1/local-categories")
      body = localCategories;
    else if (
      path.startsWith("/marketplace/connections/1/local-products-for-export")
    )
      body = { items: [], totalCount: 0 };

    await route.fulfill({ json: body });
  });
}

test("adapter creation dialog omits the top step progress", async ({
  page,
}) => {
  await mockMarketplaceApi(page);
  await page.goto("/adapter");

  await page
    .getByRole("button", { name: "Добавить подключение", exact: true })
    .click();

  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await expect(dialog.locator("ol")).toHaveCount(0);
  await expect(
    dialog.getByText(
      "Выберите маркетплейс или сервис, который хотите подключить.",
    ),
  ).toBeVisible();

  await dialog.getByRole("radio", { name: /Test adapter/ }).click();
  await dialog.getByRole("button", { name: "Далее", exact: true }).click();
  await expect(dialog.locator("ol")).toHaveCount(0);
  await expect(
    dialog.getByText(
      "Задайте название подключения и укажите данные для безопасной проверки доступа.",
    ),
  ).toBeVisible();
  await dialog.getByRole("button", { name: "Назад", exact: true }).click();
  await expect(
    dialog.getByText(
      "Выберите маркетплейс или сервис, который хотите подключить.",
    ),
  ).toBeVisible();
});

test("publishing dialog omits step progress and split actions share sheen and press", async ({
  page,
}) => {
  await mockMarketplaceApi(page, {
    localCategories: [{ id: 11, name: "Test category" }],
  });
  await page.goto("/adapter/1?startPublish=1");

  const wizard = page.getByRole("dialog");
  await expect(wizard).toBeVisible();
  await expect(wizard.locator('nav[aria-label="Steps"]')).toHaveCount(0);
  await expect(
    wizard.getByRole("heading", { name: "Категории", exact: true }),
  ).toBeVisible();
  await wizard
    .getByRole("button", { name: "К выбору товаров", exact: true })
    .click();
  await expect(
    wizard.getByRole("heading", { name: "Товары", exact: true }),
  ).toBeVisible();
  await expect(wizard.locator('nav[aria-label="Steps"]')).toHaveCount(0);
  await wizard
    .locator('[data-slot="dialog-footer"]')
    .getByRole("button", { name: "Назад", exact: true })
    .click();
  await expect(
    wizard.getByRole("heading", { name: "Категории", exact: true }),
  ).toBeVisible();
  await wizard.getByRole("button", { name: "Отмена", exact: true }).click();

  const group = page.locator(
    '[data-slot="button-group"][data-interaction="joined"]',
  );
  const moreActions = group.locator("button").nth(1);
  await expect(moreActions).toHaveAccessibleName("Дополнительные действия");
  await expect(moreActions).toHaveAttribute("data-slot", "button");
  await expect
    .poll(() =>
      moreActions.evaluate(
        (button) => getComputedStyle(button).backgroundImage,
      ),
    )
    .not.toBe("none");

  await moreActions.hover();
  await page.mouse.down();
  const pressStyles = await group.evaluate((container) => {
    const buttons = container.querySelectorAll("button");
    return {
      groupTransform: getComputedStyle(container).transform,
      segmentScales: [...buttons].map(
        (button) => getComputedStyle(button).scale,
      ),
    };
  });
  expect(pressStyles.groupTransform).not.toBe("none");
  expect(pressStyles.segmentScales).toEqual(["none", "1"]);
  await page.mouse.up();
});
