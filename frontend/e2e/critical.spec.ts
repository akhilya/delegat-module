import { expect, test } from "@playwright/test";

test("login page loads", async ({ page }) => {
  await page.goto("/signIn");

  await expect(page.getByRole("heading", { name: "Вход" })).toBeVisible();
  await expect(page.getByLabel("Имя пользователя")).toBeVisible();
  await expect(page.getByLabel("Пароль")).toBeVisible();
  await expect(page.getByRole("button", { name: "Войти" })).toBeVisible();
});

test("404 page has recovery buttons", async ({ page }) => {
  await page.goto("/this-page-does-not-exist");

  await expect(page.getByRole("heading", { name: "Страница не найдена" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Вернуться" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Главная" })).toBeVisible();
});

// Remaining critical flows need authenticated app + seeded DB — scaffolded for a follow-up run.
test.skip("create product then trash then restore", async () => {});
test.skip("create category with attribute", async () => {});
test.skip("create connection and open mappings", async () => {});
test.skip("start sync operation appears in UI", async () => {});
test.skip("settings create and revoke API key", async () => {});
test.skip("mutating request without CSRF fails", async () => {});
