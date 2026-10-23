// WASM-LOAD: the static loading shell, the handoff to the engine, a recoverable failure, and reduced motion.
import { test, expect, boot } from "./support/app.js";

const DOTNET = "**/wasm/_framework/dotnet.js";
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Delay dotnet.js by `ms` and resolve `seen` when the request has been made. */
const delayDotnet = async (page, ms) => {
  const state = { requested: false };
  await page.route(DOTNET, async (route) => {
    state.requested = true;
    await sleep(ms);
    await route.continue();
  });
  return state;
};

test.describe("WASM-LOAD: loading shell and handoff", () => {
  test("static shell is shown while dotnet.js is delayed, then hands off to the ready app", async ({ page }) => {
    const dotnet = await delayDotnet(page, 3000);
    await page.goto("./");

    const shell = page.locator("#causa-boot");
    const status = shell.getByRole("status");
    await expect(shell).toBeVisible();
    await expect(status).toHaveText("Starting Causa…");
    await expect(shell.getByRole("img", { name: "Causa" })).toBeVisible();
    await expect(page.locator("#causa-app")).toBeHidden();
    await expect(page.locator("html")).not.toHaveAttribute("data-boot", /.+/);
    expect(dotnet.requested, "the shell is visible while the engine runtime is still being fetched").toBe(true);
    await expect(shell).not.toContainText(/\d+\s*%/);
    expect(await shell.innerText()).not.toMatch(/%/);
    await expect(page.locator("#causa-boot-failure")).toBeHidden();

    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
    await expect(shell).toBeHidden();
    await expect(page.locator("#causa-app")).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.activeElement?.id)).toBe("causa-main");

    // No duplicated controls after the handoff: unique ids, one connect form, one connect button, one main.
    const duplicateIds = await page.evaluate(() => {
      const ids = [...document.querySelectorAll("[id]")].map((e) => e.id);
      return ids.filter((id, i) => ids.indexOf(id) !== i);
    });
    expect(duplicateIds).toEqual([]);
    await expect(page.locator("form.causa-form")).toHaveCount(1);
    await expect(page.getByRole("button", { name: "Connect data repository" })).toHaveCount(1);
    await expect(page.locator("main")).toHaveCount(1);
    await expect(page.getByRole("link", { name: "Skip to content" })).toHaveCount(1);
  });
});

test.describe("WASM-LOAD: failure and retry", () => {
  test.use({ allowedConsoleErrors: /Failed to load resource|dotnet\.js|Failed to fetch dynamically imported module/i });

  test("an aborted dotnet.js shows a recoverable failure; Retry (Enter) reloads to ready", async ({ page }) => {
    await page.route(DOTNET, (route) => route.abort("failed"));
    await page.goto("./");

    await expect(page.locator("html")).toHaveAttribute("data-boot", "failed", { timeout: 60_000 });
    const alert = page.locator("#causa-boot").getByRole("alert");
    await expect(alert).toBeVisible();
    const message = (await alert.innerText()).trim();
    expect(message.length).toBeGreaterThan(20);
    expect(message).toMatch(/[A-Z][a-z].*\./); // a sentence, not a code or stack trace
    expect(message).not.toMatch(/Error:|TypeError|at\s+\S+\s+\(|undefined/);
    await expect(page.locator("#causa-boot-status")).toHaveText("Causa could not start.");
    await expect(page.locator("#causa-app")).toBeHidden();

    const retry = page.getByRole("button", { name: "Retry" });
    await expect(retry).toBeVisible();
    await expect(retry).toBeFocused();
    // The indicator is not left spinning on a failed start.
    await expect(page.locator("#causa-boot .causa-boot__indicator")).toBeHidden();

    await page.unroute(DOTNET);
    const reloaded = page.waitForEvent("load");
    await page.keyboard.press("Enter");
    await reloaded;
    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
    await expect(page.locator("#causa-app")).toBeVisible();
    await expect(page.locator("#causa-boot")).toBeHidden();
  });
});

test.describe("WASM-LOAD: a framework file fails mid-boot", () => {
  // What the deployed site met right after a Pages deploy: one framework file answered 503, its body failed the
  // integrity check, and the .NET loader rejected. The user must still get the recoverable failure, not a dead page.
  // The loader logs its own final "download ... failed" after its retries: expected here.
  test.use({ allowedConsoleErrors: /Failed to load resource|integrity|SRI|Fetch API cannot load|mono_download_assets/i });

  test("a 503 on one framework .wasm shows the recoverable failure; Retry reloads to ready", async ({ page }) => {
    let failed = false;
    const one = /\/wasm\/_framework\/System\.ComponentModel\.EventBasedAsync\.[^/]*\.wasm$/;
    await page.route(one, (route) => {
      failed = true; // every attempt, including the loader's own retries
      return route.fulfill({ status: 503, contentType: "text/html", body: "<h1>503 Service Unavailable</h1>" });
    });
    await page.goto("./");
    // Well before the 90 s start timeout: after the loader's own retries and a short grace period.
    await expect(page.locator("html")).toHaveAttribute("data-boot", "failed", { timeout: 45_000 });
    expect(failed).toBe(true);
    await expect(page.locator("#causa-boot").getByRole("alert")).toContainText("could not be downloaded");
    await expect(page.getByRole("button", { name: "Retry" })).toBeFocused();
    await page.unroute(one);
    const reloaded = page.waitForEvent("load");
    await page.getByRole("button", { name: "Retry" }).click();
    await reloaded;
    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 90_000 });
  });
});

test.describe("WASM-LOAD: a framework file fails once", () => {
  // A temporary failure (the CDN mid-propagation) must not fail the start: the loader retries on its own.
  test.use({ allowedConsoleErrors: /Failed to load resource|integrity|SRI|Fetch API cannot load/i });

  test("a single 503 on one framework .wasm is retried by the loader and the start reaches ready", async ({ page }) => {
    let attempts = 0;
    const one = /\/wasm\/_framework\/System\.ComponentModel\.EventBasedAsync\.[^/]*\.wasm$/;
    await page.route(one, (route) => {
      attempts += 1;
      return attempts === 1
        ? route.fulfill({ status: 503, contentType: "text/html", body: "<h1>503 Service Unavailable</h1>" })
        : route.continue();
    });
    await page.goto("./");
    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
    expect(attempts).toBeGreaterThanOrEqual(2);
    await expect(page.getByRole("button", { name: "Retry" })).toBeHidden();
  });
});

test.describe("WASM-LOAD: a retried download on a slow connection", () => {
  // Review 4, B1: one framework file fails once while another download is still slow; the loader's retry
  // succeeds, so the start must reach ready even though more than the grace period passes after the failure.
  test.use({ allowedConsoleErrors: /Failed to load resource|integrity|SRI|Fetch API cannot load/i });

  test("a single 503 while a slow download is still in progress still reaches ready", async ({ page }) => {
    let attempts = 0;
    const one = /\/wasm\/_framework\/System\.ComponentModel\.EventBasedAsync\.[^/]*\.wasm$/;
    const slow = /\/wasm\/_framework\/dotnet\.native\.[^/]*\.wasm$/;
    await page.route(one, (route) => {
      attempts += 1;
      return attempts === 1
        ? route.fulfill({ status: 503, contentType: "text/html", body: "<h1>503 Service Unavailable</h1>" })
        : route.continue();
    });
    await page.route(slow, async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 15_000));
      await route.continue();
    });
    await page.goto("./");
    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
    expect(attempts).toBeGreaterThanOrEqual(2);
  });
});

test.describe("WASM-LOAD: reduced motion", () => {
  test.use({ reducedMotion: "reduce" });

  test("the boot indicator does not animate when reduced motion is requested", async ({ page }) => {
    await delayDotnet(page, 2000);
    await page.goto("./");
    const bar = page.locator("#causa-boot .causa-boot__indicator span");
    await expect(page.locator("#causa-boot")).toBeVisible();
    expect(await bar.evaluate((el) => getComputedStyle(el).animationName)).toBe("none");
    expect(await page.locator("#causa-boot .causa-boot__indicator").evaluate((el) => getComputedStyle(el).animationName)).toBe("none");
    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
  });

  test("control: without the preference the indicator animates", async ({ page, browser, baseURL }) => {
    const context = await browser.newContext({ reducedMotion: "no-preference" });
    const other = await context.newPage();
    await other.route(DOTNET, async (route) => { await sleep(1500); await route.continue(); });
    await other.goto(new URL("./", baseURL).href);
    expect(await other.locator("#causa-boot .causa-boot__indicator span").evaluate((el) => getComputedStyle(el).animationName)).toBe("causa-indeterminate");
    await context.close();
    await boot(page);
  });
});
