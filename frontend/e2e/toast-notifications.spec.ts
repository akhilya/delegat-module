import { expect, test } from "@playwright/test";

for (const { theme, width } of [
  { theme: "light", width: 1280 },
  { theme: "dark", width: 390 },
]) {
  test(`compact notifications in ${theme} at ${width}px`, async ({
    page,
  }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.addInitScript((theme) => {
      localStorage.setItem("vueuse-color-scheme", theme);
      localStorage.setItem("mobile-warning-dismissed", "1");
    }, theme);
    // No real catalog writes or marketplace requests are possible in this fixture.
    await page.route("**/api/**", async (route) => {
      const path = new URL(route.request().url()).pathname.replace(
        /^\/api/,
        "",
      );
      let body: unknown = [];
      if (path === "/health") body = { status: "healthy", version: "1.6.0" };
      else if (path === "/auth/isAuthenticated")
        body = { isAuthenticated: true };
      else if (path === "/auth/me")
        body = {
          id: 1,
          userName: "Test",
          selectedStoreId: 1,
          permissions: ["catalog.manage"],
          aiEnabled: false,
        };
      else if (path === "/users/me/memberships")
        body = [{ id: 1, name: "Test store" }];
      else if (path === "/products/1")
        body = {
          id: 1,
          sku: "TEST-1",
          name: "Test product",
          description: "",
          barcode: "",
          price: 10,
          storageQuantity: 1,
          images: [],
          files: [],
          notes: [],
          attributeValues: [],
          storeAttributeValues: [],
          category: null,
        };
      await route.fulfill({ json: body });
    });
    await page.goto("/products/1/edit");
    await expect(page.locator("#product-name")).toHaveValue("Test product");

    await page.evaluate(async () => {
      const path = "/src/store/sonnerHelper.js";
      const { displaySonnerError } = await import(/* @vite-ignore */ path);
      for (const status of [500, 502, 503]) {
        displaySonnerError({
          response: {
            status,
            data: {
              code: "internal_error",
              title: "PRIVATE diagnostic",
              detail: "PRIVATE SQL stack trace ".repeat(200),
              errors: { name: ["PRIVATE validation"] },
              data: { response: { body: "PRIVATE upstream response" } },
            },
          },
        });
      }
    });
    const errors = page.locator('[data-sonner-toast][data-type="error"]');
    await expect(errors).toHaveCount(1);
    await expect(errors.locator("[data-title]")).toHaveText(
      "На сервере произошла ошибка",
    );
    await expect(errors.locator("[data-description]")).toHaveText(
      "Попробуйте ещё раз чуть позже.",
    );
    await expect(errors).not.toContainText("PRIVATE");
    await expect(page.locator("[data-sonner-toaster]")).toHaveAttribute(
      "data-sonner-theme",
      theme,
    );
    const close = errors.getByRole("button", { name: "Закрыть", exact: true });
    await expect(close).toBeVisible();
    await expect(errors).toHaveAttribute("data-mounted", "true");
    const box = await errors.boundingBox();
    expect(box!.height).toBeLessThan(100);
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(width);
    expect(
      await errors.evaluate((el) => getComputedStyle(el).borderRadius),
    ).toBe("16px");
    const screenshot = testInfo.outputPath(`server-error-${theme}.png`);
      await page.screenshot({ path: screenshot, animations: "disabled" });
    await testInfo.attach(`server-error-${theme}`, {
      path: screenshot,
      contentType: "image/png",
    });
    await close.focus();
    await page.keyboard.press("Enter");
    await expect(errors).toHaveCount(0);

    await page.evaluate(async () => {
      const path = "/src/store/sonnerHelper.js";
      const { displaySonnerError, displaySonnerSuccess } = await import(
        /* @vite-ignore */ path
      );
      displaySonnerError({
        response: {
          status: 400,
          data: {
            code: "product_not_found",
            details: { body: "PRIVATE raw details" },
          },
        },
      });
      // Interceptor-owned errors must not create duplicate notifications here.
      displaySonnerError({ response: { status: 409 } });
      displaySonnerError({ response: { status: 429 } });
      displaySonnerSuccess("Товар сохранён", {
        label: "Отменить",
        onClick: () => document.body.setAttribute("data-toast-action", "done"),
      });
    });
    await expect(errors).toHaveCount(1);
    await expect(errors.locator("[data-title]")).toHaveText("Товар не найден");
    await expect(errors.locator("[data-description]")).toHaveCount(0);
    await page.locator('[data-sonner-toast][data-type="success"]').hover();
    await page.getByRole("button", { name: "Отменить", exact: true }).click();
    await expect(page.locator("body")).toHaveAttribute(
      "data-toast-action",
      "done",
    );
  });
}
