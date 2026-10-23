// Live acceptance against a REAL GitHub repository (EXT-DATA-009/010, G6). Never mocked.
// Required environment: CAUSA_LIVE_TOKEN (short-lived, repository-scoped; never logged or written), CAUSA_LIVE_OWNER,
// CAUSA_LIVE_REPO, CAUSA_LIVE_BRANCH. Optional CAUSA_BASE_URL to test a deployed site instead of a local build.
import { existsSync } from "node:fs";
import { defineConfig, devices } from "@playwright/test";

const port = 4322;
const origin = process.env.CAUSA_BASE_URL ?? `http://127.0.0.1:${port}`;
const preinstalledChromium = "/opt/pw-browsers/chromium-1194/chrome-linux/chrome";
const launchOptions = existsSync(preinstalledChromium) && !process.env.CI ? { executablePath: preinstalledChromium } : {};

export default defineConfig({
  testDir: "./tests/live",
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 240_000,
  expect: { timeout: 60_000 },
  outputDir: "test-results/live",
  reporter: [["list"], ["json", { outputFile: "test-results/live-results.json" }]],
  use: { baseURL: origin, trace: "off", screenshot: "only-on-failure", launchOptions },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], launchOptions } }],
  webServer: process.env.CAUSA_BASE_URL
    ? undefined
    : { command: `node scripts/serve.mjs dist/site ${port}`, url: `${origin}/index.html`, reuseExistingServer: false, timeout: 60_000 },
});
