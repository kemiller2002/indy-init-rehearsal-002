// MOCKED: the investigation views, the inspector, deep links, routing and print media. UI behavior only.
import { test, expect, boot, connect, openFixtureDashboard, openInvestigation, goHash, FIXTURE_TARGET } from "./support/app.js";
import { fixtureInvestigations, fixtureRepoFiles } from "./support/causa-data.js";
import { installFakeGitHub } from "./support/fake-github.js";

const byTitle = (prefix) => fixtureInvestigations().find((f) => f.title.startsWith(prefix));

/** The tab key, its label, and the heading that proves its content rendered. */
const TABS = [
  ["overview", "Overview", "Current conclusion"],
  ["hypotheses", "Hypotheses", "Evidence relationships"],
  ["evidence", "Evidence", "Established not to be the case"],
  ["questions", "Questions & conflicts", "Investigative actions and findings"],
  ["causes", "Causes & verification", "Verification criteria and outcomes"],
  ["timeline", "Timeline", "Timeline"],
  ["report", "Report", "Investigation report"],
];

/** The visible sequence numbers of the timeline, as integers. */
const timelineSequence = (page) =>
  page.locator(".causa-timeline__seq").evaluateAll((els) => els.map((e) => Number(e.textContent.replace(/^v/, ""))));

test.describe("MOCKED: investigation views", () => {
  test("MOCKED: every tab renders its content; Timeline is ordered by version; Report contains ef-print-document", async ({ page }) => {
    await openFixtureDashboard(page);
    // The richest fixture: causes, corrective actions, verifications and a conclusion are all present.
    const inv = byTitle("Users unable to sign in");
    await page.getByRole("link", { name: inv.title, exact: true }).click();
    await expect(page.locator("#inv-title")).toHaveText(inv.title);
    await expect(page).toHaveTitle(`${inv.title} · Overview · Causa`);
    await expect(page.locator(".causa-record__facts")).toContainText(`Version ${inv.expectedVersion} · ${inv.expectedVersion} events`);

    for (const [key, label, heading] of TABS) {
      await page.locator(".causa-tabs").getByRole("link", { name: label, exact: true }).click();
      await expect(page).toHaveURL(new RegExp(`#/i/${inv.id}/${key}$`));
      await expect(page.locator(".causa-tabs").getByRole("link", { name: label, exact: true })).toHaveAttribute("aria-current", "true");
      await expect(page.locator("main").getByRole("heading", { level: 2, name: heading, exact: true })).toBeVisible();
      await expect(page.locator("#inv-title")).toHaveText(inv.title);
    }

    await goHash(page, `#/i/${inv.id}/hypotheses`);
    await expect(page.locator(".causa-relation")).not.toHaveCount(0);
    await goHash(page, `#/i/${inv.id}/evidence`);
    await expect(page.locator('section[aria-labelledby="ev-list"] .causa-item')).toHaveCount(7);
    await goHash(page, `#/i/${inv.id}/causes`);
    await expect(page.locator('section[aria-labelledby="c-concl"] .causa-item')).toHaveCount(1);

    await goHash(page, `#/i/${inv.id}/timeline`);
    await expect(page.locator(".causa-timeline__event")).toHaveCount(inv.expectedVersion);
    const seq = await timelineSequence(page);
    expect(seq).toEqual(Array.from({ length: inv.expectedVersion }, (_, i) => i + 1));
    await expect(page.locator(".causa-timeline__event").first()).toContainText(`Started "${inv.title}"`);
    await page.locator('select[data-event="timeline.filter"]').selectOption("evidence");
    await expect.poll(async () => (await timelineSequence(page)).length).toBeLessThan(inv.expectedVersion);
    const filtered = await timelineSequence(page);
    expect(filtered.length).toBeGreaterThan(0);
    expect(filtered).toEqual([...filtered].sort((a, b) => a - b));
    await expect(page.locator('.causa-timeline__event:not([data-category="evidence"])')).toHaveCount(0);
    await page.locator('select[data-event="timeline.filter"]').selectOption("all");
    await expect(page.locator(".causa-timeline__event")).toHaveCount(inv.expectedVersion);

    await goHash(page, `#/i/${inv.id}/report`);
    const report = page.locator("ef-print-document.causa-report");
    await expect(report).toBeVisible();
    await expect(report.locator("ef-print-title-page h1")).toHaveText(inv.title);
    await expect(report).toContainText("Chronology");
    await expect(report.locator("ol li")).toHaveCount(inv.expectedVersion);
    await expect(page.getByRole("button", { name: "Print or save as PDF" })).toBeVisible();
  });

  test("MOCKED: the Report chronology lists every event even after the Timeline filter was narrowed", async ({ page }) => {
    // The printed report presents itself as the investigation's chronology; a filter chosen on another tab must not
    // silently drop events from it.
    await openFixtureDashboard(page);
    const inv = byTitle("Users unable to sign in");
    await openInvestigation(page, inv.id, "timeline");
    await page.locator('select[data-event="timeline.filter"]').selectOption("evidence");
    await expect.poll(async () => (await timelineSequence(page)).length).toBeLessThan(inv.expectedVersion);
    await page.locator(".causa-tabs").getByRole("link", { name: "Report", exact: true }).click();
    const chronology = page.locator("ef-print-document.causa-report ol li");
    await expect(chronology, "report chronology must not inherit the Timeline tab's filter").toHaveCount(inv.expectedVersion);
  });

  test("MOCKED: read-only fixtures explain why no change can be made", async ({ page }) => {
    await openFixtureDashboard(page);
    const inv = byTitle("Card payments");
    await openInvestigation(page, inv.id);
    await expect(page.locator(".causa-commands")).toContainText("No changes can be made here: The frozen v1.0.0 fixtures are read-only.");
    await expect(page.locator(".causa-command-list").first()).toBeHidden();
  });

  test("MOCKED: selecting an entity opens the inspector with provenance; Close returns to the tab", async ({ page }) => {
    await openFixtureDashboard(page);
    const inv = byTitle("Error-rate dashboard");
    await openInvestigation(page, inv.id, "hypotheses");
    const first = page.locator('section[aria-labelledby="hy-list"] .causa-item__link').first();
    const statement = (await first.innerText()).trim();
    await first.click();
    await expect(page).toHaveURL(new RegExp(`#/i/${inv.id}/hypotheses/[0-9a-f-]{36}$`));
    const inspector = page.getByRole("complementary", { name: "Details" }).locator(".causa-inspector");
    await expect(inspector).toBeVisible();
    await expect(inspector.locator(".causa-record__kicker")).toHaveText("Hypothesis");
    await expect(inspector.locator("#sel-title")).toHaveText(statement);
    const facts = inspector.locator(".causa-facts");
    await expect(facts.locator("dt", { hasText: "Recorded by" })).toHaveCount(1);
    await expect(facts.locator("dt", { hasText: "Recorded at" })).toHaveCount(1);
    await expect(facts.locator("div", { has: page.locator("dt", { hasText: "Recorded at" }) }).locator("dd")).toHaveText(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2} UTC$/);
    await expect(facts.locator("div", { has: page.locator("dt", { hasText: "Recorded by" }) }).locator("dd")).not.toHaveText("");
    await expect(first).toHaveAttribute("aria-current", "true");
    await expect(page.locator(".causa-shell")).toHaveAttribute("data-detail", "true");

    await inspector.getByRole("link", { name: "Close details" }).click();
    await expect(page).toHaveURL(new RegExp(`#/i/${inv.id}/hypotheses$`));
    await expect(page.locator(".causa-inspector")).toHaveCount(0);
  });

  test("MOCKED: a deep link #/i/<id>/<tab>/<entity> asks to connect, then resolves after connecting", async ({ page }) => {
    const inv = byTitle("Intermittent 502");
    // Learn an entity id from a first session, then use it as a cold deep link in a fresh page.
    await openFixtureDashboard(page);
    await openInvestigation(page, inv.id, "evidence");
    await page.locator('section[aria-labelledby="ev-list"] .causa-item__link').first().click();
    await expect(page.locator(".causa-inspector")).toBeVisible();
    const deepLink = new URL(page.url()).hash;
    const body = (await page.locator("#sel-title").innerText()).trim();
    expect(deepLink).toMatch(new RegExp(`^#/i/${inv.id}/evidence/[0-9a-f-]{36}$`));

    const cold = await page.context().newPage();
    await installFakeGitHub(cold, { files: fixtureRepoFiles() });
    await boot(cold, deepLink);
    await expect(cold.getByRole("heading", { level: 1, name: "Connect to view this investigation" })).toBeVisible();
    await expect(cold.locator(".causa-inspector")).toHaveCount(0);
    await cold.getByRole("link", { name: "Go to Connect data repository" }).click();
    await connect(cold, FIXTURE_TARGET, { expectCards: 5 });
    await cold.goBack(); // the deep link is still in history; the in-memory connection survives
    await expect(cold).toHaveURL(new RegExp(`${deepLink}$`));
    await expect(cold.locator("#inv-title")).toHaveText(inv.title);
    await expect(cold.locator(".causa-tabs").getByRole("link", { name: "Evidence", exact: true })).toHaveAttribute("aria-current", "true");
    await expect(cold.locator(".causa-inspector #sel-title")).toHaveText(body);
    await expect(cold.locator(".causa-inspector .causa-record__kicker")).toHaveText("Evidence");
    await cold.close();
  });

  test("MOCKED: an unknown route shows the not-found state; an unknown investigation id shows cannot-be-shown", async ({ page }) => {
    await openFixtureDashboard(page);
    await goHash(page, "#/no/such/view");
    await expect(page.getByRole("heading", { level: 1, name: "This address does not match a Causa view" })).toBeVisible();
    await expect(page).toHaveTitle("Not found · Causa");
    await goHash(page, `#/i/${fixtureInvestigations()[0].id}/not-a-tab`);
    await expect(page.getByRole("heading", { level: 1, name: "This address does not match a Causa view" })).toBeVisible();
    await page.getByRole("link", { name: "Back to investigations" }).click();
    await expect(page.locator(".causa-card")).toHaveCount(5);

    await goHash(page, "#/i/00000000-0000-4000-8000-0000000000ff/overview");
    await expect(page.getByRole("heading", { level: 1, name: "This investigation cannot be shown" })).toBeVisible();
    await expect(page.locator("main").getByRole("alert")).toHaveText("No investigation with this identifier exists in the connected workspace.");
  });

  test("not-found works without a connection too", async ({ page }) => {
    await boot(page, "#/definitely/not/a/route");
    await expect(page.getByRole("heading", { level: 1, name: "This address does not match a Causa view" })).toBeVisible();
  });
});

test.describe("MOCKED: print media", () => {
  test("MOCKED: printing the Report hides the masthead, rail and actions and shows the report", async ({ page }) => {
    await openFixtureDashboard(page);
    const inv = byTitle("p95 latency");
    await openInvestigation(page, inv.id, "report");
    await expect(page.locator("ef-print-document")).toBeVisible();
    await page.emulateMedia({ media: "print" });
    for (const selector of [".causa-masthead", ".causa-rail", ".causa-commands", ".causa-tabs", ".causa-record", ".causa-no-print", ".causa-skip"])
      await expect(page.locator(selector).first(), `${selector} hidden in print`).toBeHidden();
    await expect(page.getByRole("button", { name: "Print or save as PDF" })).toBeHidden();
    const report = page.locator("ef-print-document.causa-report");
    await expect(report).toBeVisible();
    await expect(report.locator("ef-print-title-page h1")).toHaveText(inv.title);
    await expect(report).toContainText("Corrective actions and verification");
    const box = await report.boundingBox();
    expect(box.width).toBeGreaterThan(300);
    await page.emulateMedia({ media: "screen" });
    await expect(page.locator(".causa-masthead")).toBeVisible();
  });
});
