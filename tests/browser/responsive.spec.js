// UI responsive layout: no horizontal overflow, touch-sized primary actions, full-page screenshots, 200% text.
import { test, expect, boot, openFixtureDashboard, openInvestigation, overflow, screenshotPath } from "./support/app.js";
import { fixtureInvestigations } from "./support/causa-data.js";

const VIEWPORTS = [
  { width: 1366, height: 900 },
  { width: 820, height: 1180 },
  { width: 1180, height: 820 },
  { width: 390, height: 844 },
];

const label = ({ width, height }) => `${width}x${height}`;

/** Elements wider than the viewport or extending past its right edge (for a useful failure message). */
const overflowingElements = (page) =>
  page.evaluate(() =>
    [...document.querySelectorAll("body *")]
      .filter((el) => el.getClientRects().length > 0)
      .filter((el) => el.getBoundingClientRect().right > window.innerWidth + 1)
      .filter((el) => !el.closest(".causa-rail, .causa-tabs")) // these scroll horizontally by design on narrow screens
      .slice(0, 8)
      .map((el) => `${el.tagName.toLowerCase()}.${[...el.classList].join(".")} right=${Math.round(el.getBoundingClientRect().right)}`));

/** Heights of visible primary buttons. */
const primaryButtonHeights = (page) =>
  page.locator(".causa-button--primary").evaluateAll((els) =>
    els.filter((el) => el.getClientRects().length > 0).map((el) => ({ text: el.textContent.trim(), height: el.getBoundingClientRect().height })));

const checkView = async (page, view, viewport, { requirePrimary = true } = {}) => {
  const { scrollWidth, innerWidth } = await overflow(page);
  const offenders = scrollWidth > innerWidth ? await overflowingElements(page) : [];
  await page.screenshot({ path: screenshotPath(`${view}-${label(viewport)}`), fullPage: true });
  expect(scrollWidth, `${view} @ ${label(viewport)} overflows horizontally: ${offenders.join(", ")}`).toBeLessThanOrEqual(innerWidth);
  const heights = await primaryButtonHeights(page);
  if (requirePrimary) expect(heights.length, `${view} has a visible primary action`).toBeGreaterThan(0);
  expect(heights.filter((b) => b.height < 44), `${view} @ ${label(viewport)} primary buttons under 44px`).toEqual([]);
};

const investigationTarget = () => fixtureInvestigations().find((f) => f.title.startsWith("Error-rate dashboard"));

for (const viewport of VIEWPORTS) {
  test.describe(`UI responsive @ ${label(viewport)}`, () => {
    test.use({ viewport });

    test(`start @ ${label(viewport)}`, async ({ page }) => {
      await boot(page);
      await checkView(page, "start", viewport);
    });

    test(`MOCKED: dashboard @ ${label(viewport)}`, async ({ page }) => {
      await openFixtureDashboard(page);
      await checkView(page, "dashboard", viewport);
    });

    test(`MOCKED: investigation @ ${label(viewport)}`, async ({ page }) => {
      await openFixtureDashboard(page);
      await openInvestigation(page, investigationTarget().id);
      // Read-only data has no primary action on the overview; the Report tab's print button covers the size check.
      await checkView(page, "investigation", viewport, { requirePrimary: false });
      await openInvestigation(page, investigationTarget().id, "report");
      await checkView(page, "investigation-report", viewport);
    });
  });
}

test.describe("UI text resize", () => {
  test.use({ viewport: { width: 1366, height: 900 } });

  test("MOCKED: at 200% text the investigation view has no horizontal overflow at 1366px", async ({ page }) => {
    await openFixtureDashboard(page);
    await openInvestigation(page, investigationTarget().id);
    const before = await page.locator("#inv-title").evaluate((el) => parseFloat(getComputedStyle(el).fontSize));
    await page.evaluate(() => { document.documentElement.style.fontSize = "200%"; });
    const after = await page.locator("#inv-title").evaluate((el) => parseFloat(getComputedStyle(el).fontSize));
    expect(after, "text actually grew").toBeGreaterThanOrEqual(before * 1.5);
    for (const tab of ["overview", "timeline", "report"]) {
      await openInvestigation(page, investigationTarget().id, tab);
      const { scrollWidth, innerWidth } = await overflow(page);
      const offenders = scrollWidth > innerWidth ? await overflowingElements(page) : [];
      await page.screenshot({ path: screenshotPath(`investigation-${tab}-text200-1366x900`), fullPage: true });
      expect(scrollWidth, `${tab} at 200% text overflows: ${offenders.join(", ")}`).toBeLessThanOrEqual(innerWidth);
    }
  });
});
