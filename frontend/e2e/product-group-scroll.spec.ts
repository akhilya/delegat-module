import { expect, test } from "@playwright/test";

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`group product picker scrolls at ${viewport.width}px`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const products = Array.from({ length: 25 }, (_, index) => ({
      id: index + 1,
      name: `Scroll test product ${index + 1}`,
      sku: `SCROLL-${index + 1}`,
    }));

    // Keep the regression independent of a running API and never mutate a real catalog.
    await page.route("**/api/**", async (route) => {
      const path = new URL(route.request().url()).pathname.replace(/^\/api/, "");
      let body: unknown = [];
      if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
      else if (path === "/auth/me") body = {
        id: 1, userName: "scroll-test", permissions: ["catalog.manage"],
        selectedStoreId: 1, aiEnabled: false,
      };
      else if (path === "/productgroups/1") body = {
        id: 1, name: "Scroll test group", members: [], marketplaceLinks: [],
      };
      else if (path === "/users/me/memberships") body = [{ id: 1, name: "Scroll test store" }];
      else if (path === "/products") body = { items: products, totalCount: products.length };
      else if (path === "/health") body = { status: "healthy", version: "1.6.0" };
      await route.fulfill({ json: body });
    });

    await page.goto("/product-groups/1/edit");
    if (viewport.width < 640)
      await page.getByRole("button", { name: /Продолжить|Continue/i, exact: true }).click();
    await page.getByRole("button", { name: /Добавить товары|Add products/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog.getByText(products[0].name, { exact: true })).toBeVisible();
    const scroller = dialog.locator('[data-slot="inset-group"] > div');
    await expect.poll(() => scroller.evaluate((el) => el.scrollHeight - el.clientHeight)).toBeGreaterThan(0);
    const footer = dialog.locator('[data-slot="dialog-footer"]');
    await dialog.evaluate((el) => Promise.all(el.getAnimations().map((animation) => animation.finished)));
    const footerBefore = await footer.boundingBox();

    await scroller.hover();
    await page.mouse.wheel(0, 2400);
    await expect.poll(() => scroller.evaluate((el) => el.scrollTop)).toBeGreaterThan(0);
    await expect(dialog.getByText(products.at(-1)!.name, { exact: true })).toBeInViewport();
    expect((await footer.boundingBox())!.y).toBeCloseTo(footerBefore!.y, 0);
    await expect(footer).toBeInViewport();
  });
}
