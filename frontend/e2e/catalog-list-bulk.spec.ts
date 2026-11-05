import { expect, test } from "@playwright/test";

for (const categories of [true, false]) {
  for (const width of [1280, 390]) {
    test.describe(`${categories ? "categories" : "groups"} ${width}px`, () => {
      test.use({ hasTouch: width < 640 });
      test("shares the inset selection/footer pattern and mutates rows in place", async ({
        page,
      }, testInfo) => {
        await page.setViewportSize({ width, height: 844 });
        const endpoint = categories ? "/categories" : "/productgroups";
        let rows = [
          { id: 1, name: "First", productsCount: 0, membersCount: 1 },
          { id: 2, name: "Occupied", productsCount: 1, membersCount: 2 },
          { id: 3, name: "Third", productsCount: 0, membersCount: 3 },
        ];
        const writes: number[][] = [];
        let reads = 0;
        await page.route("**/api/**", async (route) => {
          const request = route.request();
          const path = new URL(request.url()).pathname.replace(/^\/api/, "");
          let body: unknown = [];
          if (path === "/auth/isAuthenticated")
            body = { isAuthenticated: true };
          else if (path === "/auth/me")
            body = {
              id: 1,
              userName: "catalog-list-test",
              permissions: ["catalog.manage"],
              selectedStoreId: 1,
              aiEnabled: false,
            };
          else if (path === "/users/me/memberships")
            body = [{ id: 1, name: "Test store" }];
          else if (path === "/health")
            body = { status: "healthy", version: "1.6.0" };
          else if (path === endpoint && request.method() === "GET") {
            reads++;
            body = rows;
          } else if (categories && path === "/categories/bulk-delete") {
            const ids: number[] = request.postDataJSON().categoryIds;
            writes.push(ids);
            if (ids.includes(2)) {
              await route.fulfill({
                status: 400,
                json: {
                  code: "category_has_products",
                  data: { productCount: 1 },
                },
              });
              return;
            }
            rows = rows.filter((row) => !ids.includes(row.id));
            body = { deletedCount: ids.length };
          } else if (
            !categories &&
            request.method() === "DELETE" &&
            path.startsWith("/productgroups/")
          ) {
            const id = Number(path.split("/").at(-1));
            writes.push([id]);
            rows = rows.filter((row) => row.id !== id);
            body = { success: true };
          }
          await route.fulfill({ json: body });
        });
        // This test never connects to a live API or marketplace.
        await page.goto(categories ? "/categories" : "/product-groups");
        if (width < 640)
          await page
            .getByRole("button", { name: /^(Продолжить|Continue)$/ })
            .click();
        const all = page.getByRole("checkbox", {
          name: /^(Выбрать все|Select all)$/,
        });
        await expect(all).toBeVisible();
        const first = page.getByRole("checkbox", {
          name: "First",
          exact: true,
        });
        const firstRow = page
          .locator('[data-slot="inset-group-item"]')
          .filter({ has: first });
        const trash = firstRow.locator("[data-row-hover-action]");
        await page.mouse.move(0, 0);
        if (width >= 640) {
          await expect(trash).toHaveCSS("opacity", "0");
          await firstRow.hover();
          await expect(trash).toHaveCSS("opacity", "1");
          await page.mouse.move(0, 0);
        } else await expect(trash).toHaveCSS("opacity", "1");
        await first.focus();
        await first.press("Space");
        await expect(first).toBeChecked();
        await expect(page).toHaveURL(
          categories ? /\/categories$/ : /\/product-groups$/,
        );
        await expect(all).toHaveAttribute("data-state", "indeterminate");
        const footer = page.locator('[data-slot="list-bulk-actions"]');
        await expect(footer).toBeVisible();
        const groupBox = await firstRow.locator("..").boundingBox();
        const footerBox = await footer.boundingBox();
        expect(footerBox!.y).toBeGreaterThanOrEqual(
          groupBox!.y + groupBox!.height,
        );
        await all.check();
        await expect(footer.getByRole("status")).toHaveText(/3/);
        await expect(
          footer.getByRole("button", { name: /^(Удалить|Delete)$/ }),
        ).toHaveAttribute("data-variant", "destructive");
        await page.screenshot({ path: testInfo.outputPath("selection.png") });
        const occupied = page.getByRole("checkbox", {
          name: "Occupied",
          exact: true,
        });
        const occupiedNode = await occupied.elementHandle();
        if (categories) {
          await footer
            .getByRole("button", { name: /^(Удалить|Delete)$/ })
            .click();
          const dialog = page.getByRole("alertdialog");
          const rejected = page.waitForResponse(
            (response) =>
              response.url().endsWith("/categories/bulk-delete") &&
              response.status() === 400,
          );
          await dialog
            .getByRole("button", { name: /^(Удалить|Delete)$/ })
            .click();
          await rejected;
          await expect(dialog).toBeVisible();
          await expect(
            dialog.getByRole("button", { name: /^(Удалить|Delete)$/ }),
          ).toBeEnabled();
          await dialog
            .getByRole("button", { name: /^(Отмена|Cancel)$/ })
            .click();
          await expect(first).toBeChecked();
          await occupied.uncheck();
        }
        await footer
          .getByRole("button", { name: /^(Удалить|Delete)$/ })
          .click();
        const dialog = page.getByRole("alertdialog");
        await dialog
          .getByRole("button", { name: /^(Удалить|Delete)$/ })
          .click();
        await expect(dialog).toBeHidden();
        await expect(first).toHaveCount(0);
        await expect(
          page.getByRole("checkbox", { name: "Third", exact: true }),
        ).toHaveCount(0);
        if (categories) {
          await expect(occupied).toBeVisible();
          expect(await occupiedNode!.evaluate((node) => node.isConnected)).toBe(
            true,
          );
          expect(writes).toEqual([
            [1, 2, 3],
            [1, 3],
          ]);
        } else expect(writes).toEqual([[1], [2], [3]]);
        expect(reads).toBe(1);
        expect(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
        ).toBe(true);
      });
    });
  }
}
