// Shared test fixture and small, pure-ish helpers for the Causa browser suite.
//
// Every test gets a `diagnostics` collector (requirement 15): console errors, uncaught page errors and CSP
// violations (securitypolicyviolation events, captured by an init script). After the test, the collector asserts
// there were none. A test that provokes an expected browser error (for example an aborted dotnet.js) declares
// the exact message patterns it tolerates with `test.use({ allowedConsoleErrors: /.../ })`; CSP violations and
// page errors are never tolerated.
import { test as base, expect } from "@playwright/test";
import { mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fixtureRepoFiles, investigationFiles } from "./causa-data.js";
import { installFakeGitHub } from "./fake-github.js";

export { expect };

const cspProbe = () => {
  window.__causaCsp = [];
  document.addEventListener("securitypolicyviolation", (e) => {
    window.__causaCsp.push({ directive: e.violatedDirective, blocked: e.blockedURI, source: e.sourceFile, line: e.lineNumber });
  });
};

export const test = base.extend({
  // A single RegExp (or null). Not an array: test.use() reads an array as a [value, options] tuple.
  allowedConsoleErrors: [null, { option: true }],

  diagnostics: [
    async ({ page, context, allowedConsoleErrors }, use, testInfo) => {
      const consoleErrors = [];
      const pageErrors = [];
      const cspFromConsole = [];
      const pages = new Set();
      // Every page of the test's context is watched, including extra pages a test opens (a cold deep link, etc.).
      await context.addInitScript(cspProbe);
      const watch = (p) => {
        if (pages.has(p)) return;
        pages.add(p);
        p.on("console", (msg) => {
          if (msg.type() !== "error") return;
          const text = msg.text();
          if (/Content Security Policy/i.test(text)) cspFromConsole.push(text);
          else if (!(allowedConsoleErrors instanceof RegExp && allowedConsoleErrors.test(text))) consoleErrors.push(text);
        });
        p.on("pageerror", (err) => pageErrors.push(String(err?.stack ?? err)));
      };
      watch(page);
      context.on("page", watch);
      const collector = { consoleErrors, pageErrors, cspFromConsole };
      await use(collector);
      const cspEvents = (
        await Promise.all([...pages].filter((p) => !p.isClosed()).map((p) => p.evaluate(() => window.__causaCsp ?? []).catch(() => [])))
      ).flat();
      const report = { consoleErrors, pageErrors, cspViolations: [...cspFromConsole, ...cspEvents] };
      if (report.consoleErrors.length + report.pageErrors.length + report.cspViolations.length > 0)
        await testInfo.attach("browser-diagnostics.json", { body: JSON.stringify(report, null, 2), contentType: "application/json" });
      expect(report.cspViolations, "CSP violations during the test").toEqual([]);
      expect(report.pageErrors, "uncaught page errors during the test").toEqual([]);
      expect(report.consoleErrors, "console errors during the test").toEqual([]);
    },
    { auto: true },
  ],
});

// ------------------------------------------------------------------------------------------------ navigation

/** Load the app (optionally at a hash route) and wait for the engine handoff. */
export const boot = async (page, hash = "") => {
  await page.goto(`./${hash}`);
  await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
};

export const FIXTURE_TARGET = { owner: "causa-test", repo: "causa-data", branch: "main", root: "fixtures", token: "github_pat_test_only_not_a_real_token" };
export const WORKSPACE_TARGET = { ...FIXTURE_TARGET, root: "r002-test" };

/** One fixture investigation placed in the writable test workspace (valid: its directory name is its own id). */
export const seededWorkspace = (id, root = WORKSPACE_TARGET.root) =>
  Object.fromEntries(Object.entries(investigationFiles(id)).map(([name, bytes]) => [`workspaces/demo/${root}/investigations/${id}/${name}`, bytes]));

/** Fake (writable workspace, optionally seeded) + boot + connect. Returns the fake. */
export const openWorkspace = async (page, { seed = [], ...fakeOptions } = {}) => {
  const files = Object.assign({}, ...seed.map((id) => seededWorkspace(id)));
  const fake = await installFakeGitHub(page, { files, push: true, ...fakeOptions });
  await boot(page);
  await connect(page, WORKSPACE_TARGET, { expectCards: seed.length });
  return fake;
};

/** Fill the connect form (only the fields given) without submitting. */
export const fillConnect = async (page, fields) => {
  const ids = { owner: "#cn-owner", repo: "#cn-repo", branch: "#cn-branch", root: "#cn-root", token: "#cn-token" };
  for (const [key, value] of Object.entries(fields)) if (ids[key] !== undefined) await page.locator(ids[key]).fill(value);
};

export const submitConnect = (page) => page.getByRole("button", { name: "Connect data repository" }).click();

/** Fill and submit the connect form; wait until connected and every directory has settled (none loading). */
export const connect = async (page, target = FIXTURE_TARGET, { expectCards } = {}) => {
  await fillConnect(page, target);
  await submitConnect(page);
  await expect(page.locator(".causa-repo")).toBeVisible();
  await expect(page.locator('.causa-card[data-status="loading"]')).toHaveCount(0);
  if (expectCards !== undefined) await expect(page.locator(".causa-card")).toHaveCount(expectCards);
};

/** Fake + boot + connect to the five frozen fixtures. Returns the fake. */
export const openFixtureDashboard = async (page, { extraFiles = {}, hash = "", ...fakeOptions } = {}) => {
  const fake = await installFakeGitHub(page, { files: { ...fixtureRepoFiles(), ...extraFiles }, ...fakeOptions });
  await boot(page, hash);
  await connect(page, FIXTURE_TARGET);
  return fake;
};

/** Navigate by hash within the running app (no reload, so the in-memory connection survives). */
export const goHash = async (page, hash) => {
  await page.evaluate((h) => { window.location.hash = h; }, hash);
};

export const openInvestigation = async (page, id, tab = "overview") => {
  await goHash(page, `#/i/${id}/${tab}`);
  await expect(page.locator("#inv-title")).toBeVisible();
};

// ------------------------------------------------------------------------------------------------ evidence

export const screenshotPath = (name) => {
  const path = resolve(import.meta.dirname, "../../../test-results/screenshots", `${name}.png`);
  mkdirSync(dirname(path), { recursive: true });
  return path;
};

/** Horizontal overflow facts for the page. */
export const overflow = (page) =>
  page.evaluate(() => ({ scrollWidth: document.scrollingElement.scrollWidth, innerWidth: window.innerWidth }));

// ------------------------------------------------------------------------------------------------ color (pure)

/** Parse "rgb(r, g, b)" / "rgba(r, g, b, a)" (computed-style form) into [r, g, b, a]. */
export const parseRgb = (css) => {
  const m = css.match(/rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+))?\s*\)/);
  if (m === null) throw new Error(`not an rgb() color: ${css}`);
  return [Number(m[1]), Number(m[2]), Number(m[3]), m[4] === undefined ? 1 : Number(m[4])];
};

const channel = (c) => {
  const s = c / 255;
  return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
};

/** WCAG 2.x relative luminance of an [r, g, b] color. */
export const luminance = ([r, g, b]) => 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);

/** WCAG 2.x contrast ratio of two [r, g, b] colors. */
export const contrastRatio = (a, b) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

/** Composite a translucent foreground [r,g,b,a] over an opaque background [r,g,b]. */
export const over = ([r, g, b, a], [br, bg, bb]) => [r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)];

/**
 * The text color and effective (first opaque ancestor) background of an element, as computed by the browser.
 * Translucent layers between the element and the opaque ancestor are composited in order.
 */
export const textAndBackground = (locator) =>
  locator.evaluate((el) => {
    const layers = [];
    for (let n = el; n !== null; n = n.parentElement) {
      const bg = getComputedStyle(n).backgroundColor;
      layers.push(bg);
      const alpha = bg.startsWith("rgba") ? Number(bg.split(",")[3].replace(")", "")) : 1;
      if (alpha >= 1) break;
    }
    return { color: getComputedStyle(el).color, layers };
  });

/** Resolve textAndBackground() output to opaque [r,g,b] text and background colors. */
export const effectiveColors = ({ color, layers }) => {
  const stack = layers.map(parseRgb).filter(([, , , a]) => a > 0);
  const hasOpaque = stack.length > 0 && stack[stack.length - 1][3] >= 1;
  const opaque = hasOpaque ? stack[stack.length - 1].slice(0, 3) : [255, 255, 255];
  const translucent = hasOpaque ? stack.slice(0, -1) : stack;
  const background = [...translucent].reverse().reduce((acc, layer) => over(layer, acc), opaque);
  const fg = parseRgb(color);
  return { text: fg[3] < 1 ? over(fg, background) : fg.slice(0, 3), background };
};
