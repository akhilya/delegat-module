import { expect, test } from "@playwright/test";

for (const global of [false, true]) {
  for (const width of [1280, 390]) {
    test.describe(`${global ? "global" : "category"} attributes ${width}px`, () => {
      test.use({ hasTouch: width < 640 });
      test(`${global ? "global" : "category"} attribute bulk actions at ${width}px`, async ({
        page,
      }, testInfo) => {
        await page.setViewportSize({ width, height: 844 });
        let attributes = [
          {
            id: 1,
            name: "Color",
            key: "color",
            type: "Text",
            isRequired: false,
            isUnique: false,
            showOnProductPage: true,
          },
          {
            id: 2,
            name: "Material",
            key: "material",
            type: "Text",
            isRequired: true,
            isUnique: false,
            showOnProductPage: true,
          },
        ];
        const endpoint = global
          ? "/store-attributes"
          : "/categories/1/attributes";
        const writes: { path: string; data: Record<string, unknown> }[] = [];
        const reads: string[] = [];
        let releaseDetail!: () => void;
        const detailReady = new Promise<void>((resolve) => {
          releaseDetail = resolve;
        });
        let failDelete = true;
        await page.route("**/api/**", async (route) => {
          const request = route.request();
          const path = new URL(request.url()).pathname.replace(/^\/api/, "");
          if (request.method() === "GET") reads.push(path);
          let body: unknown = [];
          if (path === "/auth/isAuthenticated")
            body = { isAuthenticated: true };
          else if (path === "/auth/me")
            body = {
              id: 1,
              userName: "attribute-test",
              permissions: ["catalog.manage"],
              selectedStoreId: 1,
              aiEnabled: false,
            };
          else if (path === "/users/me/memberships")
            body = [{ id: 1, name: "Test store" }];
          else if (path === "/health")
            body = { status: "healthy", version: "1.6.0" };
          else if (path === "/categories/1/summary")
            body = { id: 1, name: "Category", description: "", attributes };
          else if (path === `${endpoint}/summary`) body = attributes;
          else if (path === "/categories/1/attributes/1") {
            await detailReady;
            body = {
              ...attributes[0],
              type: "Select",
              options: '["Red","Blue"]',
              description: "Full definition loaded on demand",
              maxValues: 2,
            };
          } else if (
            path === `${endpoint}/bulk` ||
            path === `${endpoint}/bulk-delete`
          ) {
            const data = request.postDataJSON();
            writes.push({ path, data });
            if (path.endsWith("bulk-delete")) {
              if (failDelete) {
                failDelete = false;
                await route.fulfill({
                  status: 400,
                  json: { code: "bad_request" },
                });
                return;
              }
              attributes = attributes.filter(
                (attribute) => !data.attributeIds.includes(attribute.id),
              );
            } else {
              attributes = attributes.map((attribute) =>
                data.attributeIds.includes(attribute.id)
                  ? { ...attribute, isUnique: data.isUnique }
                  : attribute,
              );
            }
            body = { success: true };
          }
          await route.fulfill({ json: body });
        });

        // All API calls are mocked; no live instance or marketplace is touched.
        await page.goto(global ? "/store-attributes" : "/categories/1/edit");
        if (width < 640)
          await page
            .getByRole("button", { name: /Продолжить|Continue/i, exact: true })
            .click();
        const all = page.getByRole("checkbox", {
          name: /Выбрать все|Select all/,
        });
        await expect(all).toBeVisible();
        const firstRow = page.locator(".attribute-row").first();
        const deleteControl = firstRow.locator(".attribute-delete");
        await page.mouse.move(0, 0);
        if (width >= 640) {
          await expect(deleteControl).toHaveCSS("opacity", "0");
          await firstRow.hover();
          await expect(deleteControl).toHaveCSS("opacity", "1");
          await page.mouse.move(0, 0);
          await firstRow.getByRole("checkbox").focus();
          await expect(deleteControl).toHaveCSS("opacity", "1");
          await all.focus();
          await expect(deleteControl).toHaveCSS("opacity", "0");
        } else {
          await expect(deleteControl).toHaveCSS("opacity", "1");
        }
        expect(reads).not.toContain("/categories/1");
        expect(reads).not.toContain(endpoint);
        expect(reads).not.toContain("/categories/1/attributes/1");
        const formNode = !global
          ? await page.locator("#category-name").elementHandle()
          : null;
        if (!global) {
          await page
            .locator(".attribute-row")
            .first()
            .getByRole("button", { name: /^Color/ })
            .click();
          const editor = page.getByRole("dialog");
          await expect(editor.getByRole("status")).toBeVisible();
          await expect(
            editor.getByRole("button", { name: /^(Сохранить|Save)$/ }),
          ).toBeDisabled();
          releaseDetail();
          await expect(editor.locator("#attr-name")).toHaveValue("Color");
          await expect(editor.locator("textarea").first()).toHaveValue(
            "Red, Blue",
          );
          await expect(editor.locator("#attr-description")).toHaveValue(
            "Full definition loaded on demand",
          );
          await editor
            .getByRole("button", { name: /^(Отмена|Cancel)$/ })
            .click();
        }
        await page
          .getByRole("checkbox", { name: /Выбрать Color|Select Color/ })
          .check();
        await expect(all).toHaveAttribute("data-state", "indeterminate");
        await all.check();
        await expect(
          page
            .getByRole("status")
            .filter({ hasText: /Выбрано: 2|Selected: 2/ }),
        ).toBeVisible();
        if (!global)
          await page.locator("#category-name").fill("Unsaved category name");
        await page.screenshot({ path: testInfo.outputPath("selection.png") });
        await page
          .getByRole("button", { name: /Редактировать|Edit/, exact: true })
          .click();
        const dialog = page.getByRole("dialog");
        await expect(dialog).toBeVisible();
        await dialog.evaluate((element) =>
          Promise.all(
            element.getAnimations().map((animation) => animation.finished),
          ),
        );
        await page.screenshot({ path: testInfo.outputPath("edit-dialog.png") });
        await expect(
          dialog.getByRole("button", { name: /Сохранить|Save/, exact: true }),
        ).toBeDisabled();
        await dialog
          .getByRole("combobox", { name: /Уникальные значения|Unique values/ })
          .click();
        await page.getByRole("option", { name: /Да|Yes/, exact: true }).click();
        await dialog
          .getByRole("button", { name: /Сохранить|Save/, exact: true })
          .click();
        await expect(dialog).toBeHidden();
        expect(writes[0]).toEqual({
          path: `${endpoint}/bulk`,
          data: { attributeIds: [1, 2], isUnique: true },
        });
        if (!global)
          await expect(page.locator("#category-name")).toHaveValue(
            "Unsaved category name",
          );
        const materialNode = await page
          .locator(".attribute-row")
          .filter({ hasText: "Material" })
          .elementHandle();
        const readsBeforeDelete = reads.filter(
          (path) =>
            path === `${endpoint}/summary` || path === "/categories/1/summary",
        ).length;
        await page
          .locator(".attribute-row")
          .filter({ hasText: "Color" })
          .hover();
        await page
          .getByRole("button", { name: /^(Удалить|Delete) Color$/ })
          .click();
        const singleConfirm = page.getByRole("alertdialog");
        await singleConfirm
          .getByRole("button", { name: /^(Удалить|Delete)$/ })
          .click();
        await expect(singleConfirm).toBeVisible();
        await expect(
          singleConfirm.getByRole("button", { name: /^(Удалить|Delete)$/ }),
        ).toBeEnabled();
        await singleConfirm
          .getByRole("button", { name: /^(Удалить|Delete)$/ })
          .click();
        await expect(singleConfirm).toBeHidden();
        await expect(
          page.getByRole("checkbox", { name: /Выбрать Color|Select Color/ }),
        ).toHaveCount(0);
        expect(await materialNode!.evaluate((node) => node.isConnected)).toBe(
          true,
        );
        if (formNode)
          expect(await formNode.evaluate((node) => node.isConnected)).toBe(
            true,
          );
        expect(
          reads.filter(
            (path) =>
              path === `${endpoint}/summary` ||
              path === "/categories/1/summary",
          ),
        ).toHaveLength(readsBeforeDelete);
        await all.check();
        await page
          .getByRole("button", { name: /^(Удалить|Delete)$/, exact: true })
          .click();
        const confirm = page.getByRole("alertdialog");
        await confirm
          .getByRole("button", { name: /Отмена|Cancel/, exact: true })
          .click();
        expect(writes).toHaveLength(3);
        await expect(
          page.getByRole("button", { name: /^(Удалить|Delete)$/ }),
        ).toBeFocused();
        await page
          .getByRole("button", { name: /^(Удалить|Delete)$/, exact: true })
          .click();
        await confirm
          .getByRole("button", { name: /^(Удалить|Delete)$/, exact: true })
          .click();
        await expect(confirm).toBeHidden();
        await expect(
          page.getByRole("checkbox", { name: /Выбрать Color|Select Color/ }),
        ).toHaveCount(0);
        expect(writes.at(-1)?.data).toEqual({ attributeIds: [2] });
        expect(
          reads.filter(
            (path) =>
              path === `${endpoint}/summary` ||
              path === "/categories/1/summary",
          ),
        ).toHaveLength(readsBeforeDelete);
        expect(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= window.innerWidth,
          ),
        ).toBe(true);
      });
    });
  }
}
