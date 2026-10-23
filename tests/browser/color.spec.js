// UI-016 color: the canonical identity colors as the browser computes them, semantic tones always paired with a text
// label, and WCAG 2.x contrast >= 4.5:1 for text.
import { test, expect, boot, openFixtureDashboard, openInvestigation, parseRgb, contrastRatio, textAndBackground, effectiveColors } from "./support/app.js";
import { fixtureInvestigations } from "./support/causa-data.js";

const NAVY = "rgb(20, 40, 63)";
const TEAL = "rgb(8, 127, 140)";
const IVORY = "rgb(245, 243, 237)";
const AMBER = "rgb(233, 162, 59)";
const CORAL = "rgb(214, 107, 88)";
const WHITE = "rgb(255, 255, 255)";

const css = (locator, property) => locator.evaluate((el, p) => getComputedStyle(el).getPropertyValue(p), property);

/** Contrast of a text element against its effective background. */
const contrastOf = async (locator) => {
  const { text, background } = effectiveColors(await textAndBackground(locator));
  return { ratio: contrastRatio(text, background), text, background };
};

/** Assert every given text element meets `min` contrast; report all failures together. */
const expectContrast = async (entries, min = 4.5) => {
  const results = [];
  for (const [name, locator] of entries) results.push({ name, ...(await contrastOf(locator)) });
  const failing = results.filter((r) => r.ratio < min).map((r) => `${r.name}: ${r.ratio.toFixed(2)}:1 (text ${r.text.map(Math.round)} on ${r.background.map(Math.round)})`);
  expect(failing, `text pairs below ${min}:1`).toEqual([]);
  return results;
};

test.describe("UI-016: color", () => {
  test.use({ colorScheme: "light" });

  test("contrast helper matches known WCAG values", () => {
    expect(contrastRatio([0, 0, 0], [255, 255, 255])).toBeCloseTo(21, 5);
    expect(contrastRatio([255, 255, 255], [255, 255, 255])).toBeCloseTo(1, 5);
    expect(contrastRatio(parseRgb(WHITE), parseRgb(TEAL))).toBeCloseTo(4.75, 1);
    expect(contrastRatio(parseRgb(IVORY), parseRgb(NAVY))).toBeCloseTo(13.47, 1);
    expect(parseRgb("rgba(1, 2, 3, 0.5)")).toEqual([1, 2, 3, 0.5]);
  });

  test("start: navy masthead, teal primary button with white text, ivory body; text contrast >= 4.5", async ({ page }) => {
    await boot(page);
    const masthead = page.locator(".causa-masthead");
    const primary = page.getByRole("button", { name: "Connect data repository" });
    expect(await css(masthead, "background-color")).toBe(NAVY);
    expect(await css(primary, "background-color")).toBe(TEAL);
    expect(await css(primary, "color")).toBe(WHITE);
    expect(await css(page.locator("body"), "background-color")).toBe(IVORY);
    await expectContrast([
      ["masthead brand name", masthead.locator(".causa-brand__name")],
      ["masthead tagline", masthead.locator(".causa-brand__tag")],
      ["masthead connection lozenge", masthead.locator('[data-text="connLabel"]')],
      ["primary button", primary],
      ["page heading", page.locator("#dash-title")],
      ["page lede", page.locator(".causa-page__lede")],
      ["field label", page.locator('label[for="cn-owner"]')],
      ["field description", page.locator("#cn-token-help")],
      ["rail item", page.locator(".causa-rail__item").first()],
      ["rail footer", page.locator(".causa-rail__meta").first()],
    ]);
  });

  test("MOCKED: overview: amber on an open unknown and coral on an open contradiction, each with a text label", async ({ page }) => {
    await openFixtureDashboard(page);
    const inv = fixtureInvestigations().find((f) => f.title.startsWith("Error-rate dashboard"));
    await openInvestigation(page, inv.id);

    const unknown = page.locator('section[aria-labelledby="ov-unk"] .causa-item--unresolved[data-open="true"]').first();
    const contradiction = page.locator('section[aria-labelledby="ov-contra"] .causa-item--conflict[data-open="true"]').first();
    await expect(unknown).toBeVisible();
    await expect(contradiction).toBeVisible();
    expect(await css(unknown, "border-left-color")).toBe(AMBER);
    expect(await css(contradiction, "border-left-color")).toBe(CORAL);
    expect(await css(page.locator("section[aria-labelledby=ov-unk]"), "border-top-color")).toBe(AMBER);
    expect(await css(page.locator("section[aria-labelledby=ov-contra]"), "border-top-color")).toBe(CORAL);
    expect(await css(page.locator('#ov-unk .causa-tone-mark'), "background-color")).toBe(AMBER);
    expect(await css(page.locator('#ov-contra .causa-tone-mark'), "background-color")).toBe(CORAL);

    // Never color alone: the section names the kind, and each item states its state and materiality in text.
    await expect(page.locator("#ov-unk")).toHaveText("Unknowns");
    await expect(page.locator("#ov-contra")).toHaveText("Contradictions");
    await expect(unknown.locator(".causa-item__meta")).toHaveText(/^Identified · \S+/);
    await expect(contradiction.locator(".causa-item__meta")).toHaveText(/^Triaged · \S+/);
    await expect(page.locator(".causa-record__summary")).toContainText("1 open unknowns");
    await expect(page.locator(".causa-record__summary")).toContainText("1 open contradictions");

    // Masthead and primary-action identity colors persist on the investigation view.
    expect(await css(page.locator(".causa-masthead"), "background-color")).toBe(NAVY);
    expect(await css(page.locator("body"), "background-color")).toBe(IVORY);

    await expectContrast([
      ["open unknown text", unknown.locator(".causa-item__link")],
      ["open unknown meta", unknown.locator(".causa-item__meta")],
      ["open contradiction text", contradiction.locator(".causa-item__link")],
      ["open contradiction meta", contradiction.locator(".causa-item__meta")],
      ["open unknown count", page.locator('.causa-record__summary .causa-count-inline[data-tone="unresolved"]')],
      ["open contradiction count", page.locator('.causa-record__summary .causa-count-inline[data-tone="conflict"]').first()],
      ["blocking count", page.locator('.causa-record__summary .causa-count-inline[data-tone="conflict"]').last()],
      ["investigation title", page.locator("#inv-title")],
      ["lifecycle lozenge", page.locator('.causa-record__facts [data-text="invLifecycle"]')],
      ["current tab", page.locator('.causa-tabs__tab[aria-current="true"]')],
      ["read-only reason", page.locator(".causa-reason--block")],
      ["masthead sync lozenge", page.locator('.causa-masthead [data-text="syncLabel"]')],
    ]);
  });

  test("MOCKED: dashboard cards: amber/coral active counts carry text labels; text contrast >= 4.5", async ({ page }) => {
    await openFixtureDashboard(page);
    const card = page.locator(".causa-card", { has: page.getByRole("link", { name: /^Error-rate dashboard/ }) });
    const unknowns = card.locator('.causa-count[data-tone="unresolved"]');
    const contradictions = card.locator('.causa-count[data-tone="conflict"]');
    await expect(unknowns).toHaveAttribute("data-active", "true");
    await expect(contradictions).toHaveAttribute("data-active", "true");
    await expect(unknowns.locator("dt")).toHaveText("Open unknowns");
    await expect(contradictions.locator("dt")).toHaveText("Open contradictions");
    expect(await css(unknowns, "box-shadow")).toContain(AMBER);
    expect(await css(contradictions, "box-shadow")).toContain(CORAL);
    await expectContrast([
      ["card title", card.locator(".causa-card__title a")],
      ["unknown count", unknowns.locator("dd")],
      ["unknown label", unknowns.locator("dt")],
      ["contradiction count", contradictions.locator("dd")],
      ["contradiction label", contradictions.locator("dt")],
      ["card meta", card.locator(".causa-card__meta")],
      ["card lifecycle lozenge", card.locator('.causa-lozenge [data-text="lifecycle"]')],
      ["toolbar count", page.locator(".causa-toolbar__count")],
      ["create reason", page.locator("#create-reason")],
    ]);
  });
});
