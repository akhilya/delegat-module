import { expect, test, type Page } from "@playwright/test";

const status = {
  configured: true,
  entitled: true,
  available: true,
  usagePercent: 1.2,
  quotaExhausted: false,
  usageUnavailable: false,
};

async function openChat(
  page: Page,
  overrides = {},
  options = { administration: false, hostedOnly: false, failUsageOnce: false },
) {
  let usageCalls = 0;
  await page.addInitScript(() => {
    localStorage.setItem("delegat.locale", "ru");
    localStorage.setItem("mobile-warning-dismissed", "1");
  });
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname
      .replace(/^\/api/, "")
      .replace(/\/+$/, "");
    let body: unknown = [];
    if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (path === "/auth/me")
      body = {
        id: 1,
        userName: "usage-test",
        isAdmin: true,
        selectedStoreId: 1,
        aiEnabled: true,
        permissions: [
          "ai.use",
          "settings.platform",
          "catalog.manage",
          "files.manage",
        ],
      };
    else if (path === "/health") body = { status: "healthy", version: "1.6.0" };
    else if (path === "/users/me/memberships")
      body = [
        {
          id: 1,
          name: "Test store",
          onboardingCompletedAt: "2026-01-01T00:00:00Z",
        },
      ];
    else if (path === "/stores")
      body = {
        items: [
          {
            id: 1,
            name: "Test store",
            onboardingCompletedAt: "2026-01-01T00:00:00Z",
          },
        ],
        totalCount: 1,
      };
    else if (path === "/products") body = { items: [], totalCount: 0 };
    else if (path === "/ai/models/picker")
      body = [
        {
          id: -1,
          name: "Platform AI",
          isPlatformHosted: true,
          isEnabled: true,
          isDefault: true,
        },
        { id: 1, name: "My model", isPlatformHosted: false, isEnabled: true },
      ];
    else if (path === "/ai/models")
      body = [
        ...(!options.hostedOnly
          ? [
              {
                id: 1,
                name: "My model",
                modelId: "my-model",
                provider: "OpenAiCompatible",
                isEnabled: true,
                isDefault: true,
                apiUrl: "https://my-model.test",
                isPlatformHosted: false,
              },
            ]
          : []),
        {
          id: -1,
          name: "Platform AI",
          modelId: "deepseek-flash",
          provider: "Anthropic",
          isPlatformHosted: true,
          isEnabled: true,
          isDefault: false,
          apiUrl: "https://cp.test/platform/v1/ai/anthropic",
        },
      ];
    else if (path === "/ai/hosted-ai/admin") {
      usageCalls++;
      if (options.failUsageOnce && usageCalls === 1) {
        await route.fulfill({
          status: 400,
          json: { code: "test_usage_unavailable" },
        });
        return;
      }
      body = { ...status, ...overrides };
    } else if (path === "/ai/hosted-ai") body = { ...status, ...overrides };
    else if (path === "/ai/store-settings") body = {};
    else if (path === "/ai/conversations/latest") body = null;
    else if (path === "/settings/branding") body = {};
    await route.fulfill({ json: body });
  });
  await page.goto(options.administration ? "/settings/admin/ai" : "/products");
  await page.waitForFunction(
    () =>
      !!(document.querySelector("#app") as any)?.__vue_app__?.config
        .globalProperties.$store,
  );
  if (!options.administration)
    await page.evaluate(() => {
      const store = (document.querySelector("#app") as any).__vue_app__.config
        .globalProperties.$store;
      store.commit("aiChat/setOpen", true);
    });
}

test("usage stays in a disclosure beside the model, with keyboard dismissal", async ({
  page,
}, testInfo) => {
  await openChat(page);
  const trigger = page.getByTestId("hosted-ai-usage-trigger");
  await expect(trigger).toHaveText("1,2 %");
  await expect(page.getByText(/24\s?221 из/)).not.toBeVisible();
  await trigger.click();
  await expect(
    page.getByRole("heading", { name: "Использование ИИ" }),
  ).toBeVisible();
  await expect(page.getByRole("progressbar")).toHaveAttribute(
    "aria-valuenow",
    "1.2",
  );
  await expect(
    page.getByText(/токенов|Обновится|кешированные/),
  ).not.toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath("ai-hosted-usage-desktop.png"),
    animations: "disabled",
  });
  await page.keyboard.press("Escape");
  await expect(
    page.getByRole("heading", { name: "Использование ИИ" }),
  ).not.toBeVisible();
  await expect(trigger).toBeFocused();

  await page.evaluate(() => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.commit("aiChat/setSelectedModel", "1");
  });
  await expect(trigger).not.toBeVisible();
});

test("administration lists the provided model as read-only and shows monthly usage inline", async ({
  page,
}, testInfo) => {
  await openChat(
    page,
    {},
    { administration: true, hostedOnly: false, failUsageOnce: false },
  );
  const usage = page.getByTestId("admin-hosted-ai-usage");
  await expect(
    usage.getByRole("heading", { name: "Использование ИИ" }),
  ).toBeVisible();
  await expect(usage.getByRole("progressbar")).toHaveAttribute(
    "aria-valuenow",
    "1.2",
  );
  await expect(
    usage.getByText(/токенов|Обновится|кешированные/),
  ).not.toBeVisible();
  await expect(usage.locator("p")).toHaveCount(1);
  const model = page
    .locator('[data-slot="inset-group-item"]')
    .filter({ hasText: "deepseek-flash" });
  await expect(model.getByText("От платформы", { exact: true })).toBeVisible();
  await model.click();
  await expect(
    page.getByRole("dialog", { name: "Изменить модель" }),
  ).not.toBeVisible();
  await usage.scrollIntoViewIfNeeded();
  await page.screenshot({
    path: testInfo.outputPath("ai-hosted-usage-admin.png"),
    animations: "disabled",
  });

  await page.getByRole("button", { name: "Управление моделями" }).click();
  const management = page.getByRole("dialog", {
    name: "Языковые модели",
    exact: true,
  });
  const provided = management
    .getByRole("row")
    .filter({ hasText: "deepseek-flash" });
  await expect(
    provided.getByText("От платформы", { exact: true }),
  ).toBeVisible();
  await expect(provided.getByRole("switch")).toHaveCount(0);
  await expect(provided.getByRole("button")).toHaveCount(0);
  const own = management.getByRole("row").filter({ hasText: "my-model" });
  await expect(own.getByRole("switch")).toBeVisible();
  await expect(
    own.getByRole("button", { name: "Редактировать", exact: true }),
  ).toBeVisible();
  await page.keyboard.press("Escape");
  await page
    .getByRole("button", { name: "Персональные настройки", exact: true })
    .click();
  const settings = page.getByRole("dialog", {
    name: "Персональные настройки",
    exact: true,
  });
  await settings.getByRole("combobox").first().click();
  await expect(page.getByRole("option", { name: /My model/ })).toBeVisible();
  await expect(
    page.getByRole("option", { name: /Platform AI|Платформенный ИИ/ }),
  ).toHaveCount(0);
});

test("a provided model alone prevents the empty-model state and fits mobile settings", async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await openChat(
    page,
    {},
    { administration: true, hostedOnly: true, failUsageOnce: false },
  );
  await expect(
    page
      .getByTestId("admin-hosted-ai-usage")
      .getByRole("heading", { name: "Использование ИИ" }),
  ).toBeVisible();
  await expect(
    page.getByText("Нет AI-моделей", { exact: true }),
  ).not.toBeVisible();
  const usage = await page.getByTestId("admin-hosted-ai-usage").boundingBox();
  expect(usage!.x).toBeGreaterThanOrEqual(0);
  expect(usage!.x + usage!.width).toBeLessThanOrEqual(375);
});

test("administration can retry a failed usage read", async ({ page }) => {
  await openChat(
    page,
    {},
    { administration: true, hostedOnly: true, failUsageOnce: true },
  );
  const usage = page.getByTestId("admin-hosted-ai-usage");
  await expect(
    usage.getByText("Не удалось загрузить расход платформенного ИИ."),
  ).toBeVisible();
  await usage.getByRole("button", { name: "Загрузить расход" }).click();
  await expect(
    usage.getByRole("heading", { name: "Использование ИИ" }),
  ).toBeVisible();
});

test("exhaustion and unavailable usage remain discoverable", async ({
  page,
}) => {
  await openChat(page, { usagePercent: 100, quotaExhausted: true });
  const trigger = page.getByTestId("hosted-ai-usage-trigger");
  await expect(trigger).toHaveText("100 %");
  await trigger.click();
  await expect(page.getByText(/Месячный лимит исчерпан/)).toBeVisible();
  await page.keyboard.press("Escape");
  await page.evaluate((initial) => {
    const store = (document.querySelector("#app") as any).__vue_app__.config
      .globalProperties.$store;
    store.commit("aiModels/setHostedAiStatus", {
      ...initial,
      usageUnavailable: true,
    });
  }, status);
  await expect(trigger).toHaveText("Пауза");
  await trigger.click();
  await expect(
    page.getByText("Платформенный ИИ приостановлен, пока проверяется расход."),
  ).toBeVisible();
});

test("usage disclosure fits a narrow mobile sheet", async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await openChat(page);
  await page.getByTestId("hosted-ai-usage-trigger").click();
  const popover = page.getByRole("dialog").filter({
    has: page.getByRole("heading", { name: "Использование ИИ" }),
  });
  await expect(popover).toBeVisible();
  const box = await popover.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(375);
  await page.screenshot({
    path: testInfo.outputPath("ai-hosted-usage-mobile.png"),
    animations: "disabled",
  });
});
