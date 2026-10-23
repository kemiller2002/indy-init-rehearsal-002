// UI accessibility: axe (WCAG 2.x A/AA) on the main states, the skip link, named controls and live regions.
import AxeBuilder from "@axe-core/playwright";
import { test, expect, boot, openFixtureDashboard, openInvestigation, openWorkspace } from "./support/app.js";
import { fixtureInvestigations } from "./support/causa-data.js";

const TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

/** One line per serious/critical violation node, for a readable failure. */
const describeViolations = (violations) =>
  violations.flatMap((v) => v.nodes.map((n) => `[${v.impact}] ${v.id}: ${v.help} — ${n.target.join(" ")} — ${n.failureSummary?.replace(/\s+/g, " ")}`));

const seriousViolations = async (page, testInfo, name) => {
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  const serious = results.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  await testInfo.attach(`axe-${name}.json`, { body: JSON.stringify(results.violations, null, 2), contentType: "application/json" });
  return describeViolations(serious);
};

/** Form controls (visible, enabled or not) without an accessible name. */
const unnamedControls = (page) =>
  page.locator("input:not([type=hidden]), select, textarea, button").evaluateAll((els) =>
    els
      .filter((el) => el.getClientRects().length > 0)
      .filter((el) => {
        const labelled = (el.labels?.length ?? 0) > 0 && [...el.labels].some((l) => l.textContent.trim() !== "");
        const aria = (el.getAttribute("aria-label") ?? "").trim() !== "" || (el.getAttribute("aria-labelledby") ?? "") !== "";
        const text = el.tagName === "BUTTON" && el.textContent.trim() !== "";
        return !(labelled || aria || text || (el.getAttribute("title") ?? "").trim() !== "");
      })
      .map((el) => el.outerHTML.slice(0, 160)));

const overviewTarget = () => fixtureInvestigations().find((f) => f.title.startsWith("Error-rate dashboard"));

test.describe("UI accessibility (axe)", () => {
  test("disconnected start: no serious or critical violations", async ({ page }, testInfo) => {
    await boot(page);
    expect(await seriousViolations(page, testInfo, "start")).toEqual([]);
  });

  test("MOCKED: dashboard: no serious or critical violations", async ({ page }, testInfo) => {
    await openFixtureDashboard(page);
    expect(await seriousViolations(page, testInfo, "dashboard")).toEqual([]);
  });

  test("MOCKED: investigation overview: no serious or critical violations", async ({ page }, testInfo) => {
    await openFixtureDashboard(page);
    await openInvestigation(page, overviewTarget().id);
    expect(await seriousViolations(page, testInfo, "overview")).toEqual([]);
  });

  test("MOCKED: overview with the inspector open: no serious or critical violations", async ({ page }, testInfo) => {
    await openFixtureDashboard(page);
    await openInvestigation(page, overviewTarget().id);
    await page.locator('section[aria-labelledby="ov-unk"] .causa-item__link').first().click();
    await expect(page.locator(".causa-inspector")).toBeVisible();
    expect(await seriousViolations(page, testInfo, "inspector")).toEqual([]);
  });

  test("MOCKED: open command dialog: no serious or critical violations", async ({ page }, testInfo) => {
    await openWorkspace(page);
    await page.getByRole("button", { name: "New investigation" }).click();
    await expect(page.getByRole("dialog", { name: "New investigation" })).toBeVisible();
    expect(await seriousViolations(page, testInfo, "dialog")).toEqual([]);
    expect(await unnamedControls(page)).toEqual([]);
  });
});

test.describe("UI accessibility (structure)", () => {
  test("the skip link is the first focusable element and moves focus to main", async ({ page }) => {
    await boot(page);
    // Restart sequential navigation from the top of the document (as after a fresh page load).
    await page.evaluate(() => {
      document.body.tabIndex = -1;
      document.body.focus();
      document.body.removeAttribute("tabindex");
    });
    await page.keyboard.press("Tab");
    const skip = page.getByRole("link", { name: "Skip to content" });
    await expect(skip).toBeFocused();
    await expect(skip).toBeVisible();
    const box = await skip.boundingBox();
    expect(box.y).toBeGreaterThanOrEqual(0); // revealed on focus, not left off-screen
    await page.keyboard.press("Enter");
    await expect(page.locator("#causa-main")).toBeFocused();
  });

  test("every visible form control has an accessible name (start)", async ({ page }) => {
    await boot(page);
    expect(await unnamedControls(page)).toEqual([]);
    for (const label of ["Owner", "Repository", "Branch", "Workspace", "Access token"]) await expect(page.getByLabel(label, { exact: true })).toBeVisible();
    await expect(page.getByLabel("Workspace", { exact: true })).toHaveAccessibleDescription(/fixtures/);
    await expect(page.getByLabel("Access token", { exact: true })).toHaveAccessibleDescription(/held in memory only/);
  });

  test("MOCKED: every visible form control has an accessible name (dashboard, timeline)", async ({ page }) => {
    await openFixtureDashboard(page);
    expect(await unnamedControls(page)).toEqual([]);
    await expect(page.getByLabel("Sort by")).toBeVisible();
    await openInvestigation(page, overviewTarget().id, "timeline");
    expect(await unnamedControls(page)).toEqual([]);
    await expect(page.getByLabel("Show")).toBeVisible();
  });

  test("status and alert regions are present: boot status, notices, refusals, read-only reason", async ({ page }) => {
    await page.goto("./");
    await expect(page.locator("#causa-boot-status")).toHaveAttribute("role", "status");
    await expect(page.locator("#causa-boot-error")).toHaveAttribute("role", "alert");
    await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 60_000 });
    await expect(page.getByRole("banner")).toHaveCount(1);
    await expect(page.getByRole("navigation", { name: "Primary" })).toBeVisible();
    await expect(page.getByRole("main")).toHaveCount(1);
    // Masthead lozenges name what they report for assistive technology.
    await expect(page.locator(".causa-masthead")).toContainText("Data repository: Not connected");
    // Live regions are mounted before any content arrives, so changes inside them are announced (F8).
    await expect(page.locator('[data-text="liveStatus"]')).toHaveAttribute("role", "status");
    await expect(page.locator('[data-text="liveStatus"]')).toHaveAttribute("aria-live", "polite");
    await expect(page.locator('[data-text="liveAlert"]')).toHaveAttribute("role", "alert");
    // A critical notice is announced assertively.
    await page.locator("#cn-owner").fill(" ");
    for (const [id, v] of [["#cn-repo", "r"], ["#cn-branch", "main"], ["#cn-root", "fixtures"], ["#cn-token", "t"]]) await page.locator(id).fill(v);
    await page.getByRole("button", { name: "Connect data repository" }).click();
    await expect(page.locator(".causa-notice")).toContainText("Owner is not a valid");
    await expect(page.getByRole("alert").filter({ hasText: "Owner is not a valid" })).toHaveCount(1);
  });
});
