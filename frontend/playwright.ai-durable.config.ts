import { defineConfig, devices } from "@playwright/test";
import os from "node:os";
import path from "node:path";
import fs from "node:fs";

export default defineConfig({
  testDir: "./integration",
  testMatch: "ai-durable-process.spec.ts",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 10_000 },
  reporter: "list",
  outputDir: fs.mkdtempSync(
    path.join(os.tmpdir(), "delegat-ai-process-results-"),
  ),
  use: {
    ...devices["Desktop Chrome"],
    trace: "off",
    serviceWorkers: "block",
  },
});
