import { expect, test } from "@playwright/test";

test("Ctrl+S saves focused product fields once and notifies", async ({
  page,
}) => {
  const writes: Record<string, unknown>[] = [];
  let product = {
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
  let releaseSave!: () => void;
  const pendingSave = new Promise<void>((resolve) => {
    releaseSave = resolve;
  });
  // All requests are intercepted; no real catalog or marketplace is changed.
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace(/^\/api/, "");
    let body: unknown = [];
    if (path === "/health") body = { status: "healthy", version: "1.6.0" };
    else if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
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
    else if (path === "/products/1") {
      if (request.method() === "PATCH") {
        const data = request.postDataJSON();
        writes.push(data);
        if (writes.length === 1) await pendingSave;
        product = { ...product, ...data };
      }
      body = product;
    }
    await route.fulfill({ json: body });
  });
  await page.goto("/products/1/edit");
  const name = page.locator("#product-name");
  await expect(name).toHaveValue("Test product");
  // Wait for the editor's initial-data guard, not an arbitrary timeout.
  await expect(async () => {
    await name.press("Control+s");
    await expect(
      page.getByText("Нет несохранённых изменений", { exact: true }).last(),
    ).toBeVisible();
  }).toPass({ timeout: 5000 });
  await name.fill("Updated product");
  await name.press("Control+s");
  await expect.poll(() => writes.length).toBe(1);
  await name.press("Control+s");
  expect(writes).toHaveLength(1);
  releaseSave();
  await expect(
    page.getByText("Товар успешно обновлён.", { exact: true }).last(),
  ).toBeVisible();
  expect(writes[0].name).toBe("Updated product");

  const quantity = page.locator('input[role="spinbutton"]').first();
  await quantity.fill("7");
  await quantity.press("Control+s");
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[1].storageQuantity).toBe(7);
  await expect(
    page.getByRole("button", { name: "Сохранить", exact: true }),
  ).toBeDisabled();
  await quantity.press("Control+s");
  await expect(
    page.getByText("Нет несохранённых изменений", { exact: true }).last(),
  ).toBeVisible();
  expect(writes).toHaveLength(2);
});
