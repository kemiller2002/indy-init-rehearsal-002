// Real-browser checks of the production site (dist/site, built by scripts/build-site.mjs).
//
// The .NET tests prove the engine decides correctly; these prove the page boots the WASM engine, renders the
// projection, and meets the startup, accessibility, responsive and color requirements. Tests that need GitHub
// answers use a recorded fake behind page.route() and are labelled MOCKED: they verify UI states only and are
// never evidence of live persistence (EXT-DATA-010). Live GitHub acceptance is tests/live (playwright.live.config.js).
import { existsSync } from "node:fs";
import { defineConfig, devices } from "@playwright/test";

const port = Number(process.env.CAUSA_TEST_PORT ?? 4321);
const origin = process.env.CAUSA_BASE_URL ?? `http://127.0.0.1:${port}`;

// Some environments ship a Chromium that Playwright did not download itself. Where that binary exists it is used
// as-is; everywhere else Playwright resolves its own, so CI needs no special case.
const preinstalledChromium = "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const launchOptions = existsSync(preinstalledChromium) && !process.env.CI ? { executablePath: preinstalledChromium } : {};

export default defineConfig({
  testDir: "./tests/browser",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  outputDir: "test-results/browser",
  reporter: process.env.CI ? [["github"], ["list"], ["json", { outputFile: "test-results/browser-results.json" }]] : [["list"], ["json", { outputFile: "test-results/browser-results.json" }]],
  use: { baseURL: origin, trace: "retain-on-failure", launchOptions },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], launchOptions } }],
  webServer: process.env.CAUSA_BASE_URL
    ? undefined
    : {
        command: `node scripts/serve.mjs dist/site ${port}`,
        url: `${origin}/index.html`,
        reuseExistingServer: !process.env.CI,
        timeout: 60_000,
      },
});
