import { expect, test } from "@playwright/test";

test("deleting a shared file tag sends the confirmed request", async ({
  page,
}) => {
  await page.addInitScript(() => {
    localStorage.setItem("mobile-warning-dismissed", "1");
    localStorage.setItem("delegat.locale", "ru");
  });

  const deletedTagIds: string[] = [];
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace(/^\/api/, "");
    let body: unknown = [];

    if (path === "/health") body = { status: "healthy", version: "1.6.0" };
    else if (path === "/auth/isAuthenticated") body = { isAuthenticated: true };
    else if (path === "/auth/me")
      body = {
        id: 1,
        userName: "Test manager",
        selectedStoreId: 1,
        permissions: ["files.manage"],
        aiEnabled: false,
      };
    else if (path === "/users/me/memberships")
      body = [{ id: 1, name: "Test store" }];
    else if (path === "/store-storage/files")
      body = {
        items: [
          {
            id: 12,
            fileName: "manual.pdf",
            contentType: "application/pdf",
            length: 1024,
            tags: [{ id: 7, name: "Shared", color: "Red" }],
          },
        ],
        totalCount: 1,
        lastPage: true,
      };
    else if (path === "/store-storage/quota")
      body = {
        files: { used: 1, limit: 10 },
        storageBytes: { used: 1024, limit: 10240 },
      };
    else if (path === "/store-storage/folders") body = [];
    else if (path === "/tags")
      body = [{ id: 7, name: "Shared", color: "Red", usageCount: 1 }];
    else if (path === "/tags/7" && request.method() === "DELETE") {
      deletedTagIds.push("7");
      body = {};
    }

    await route.fulfill({ json: body });
  });

  await page.goto("/storage");
  const fileRow = page.getByRole("row").filter({ hasText: "manual.pdf" });
  await expect(fileRow).toBeVisible();
  await fileRow.getByRole("button", { name: "Метка", exact: true }).click();

  const removeButton = page.getByRole("button", {
    name: "Удалить метку «Shared»",
    exact: true,
  });
  await removeButton.click();

  const dialog = page.getByRole("alertdialog");
  await expect(dialog).toBeVisible();
  expect(deletedTagIds).toEqual([]);
  await dialog.getByRole("button", { name: "Удалить", exact: true }).click();

  await expect.poll(() => deletedTagIds).toEqual(["7"]);
  await expect(dialog).toBeHidden();
});
