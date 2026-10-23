// EXT-DATA: zero bundled data, explicit selection, and (MOCKED) reading, verifying and refusing repository data.
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { test, expect, boot, fillConnect, submitConnect, connect, openFixtureDashboard, goHash, FIXTURE_TARGET } from "./support/app.js";
import { fixtureInvestigations, fixtureRepoFiles, investigationFiles, flipOneByte } from "./support/causa-data.js";
import { installFakeGitHub, recordGitHubRequests } from "./support/fake-github.js";

const siteRoot = resolve(import.meta.dirname, "../../dist/site");

/** Every file under `dir`, recursively. */
const walk = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(join(dir, e.name)) : [join(dir, e.name)]));

/** The needles a file contains, as UTF-8 or UTF-16LE (how .NET stores string literals), at any alignment. */
const hitsIn = (bytes, needles) =>
  needles.filter((n) => bytes.indexOf(Buffer.from(n, "utf8")) >= 0 || bytes.indexOf(Buffer.from(n, "utf16le")) >= 0);

/** A sixth directory: a copy of a valid fixture under a new id, with exactly one byte of events.ndjson changed. */
const TAMPERED_ID = "0f0f0f0f-0000-4000-8000-000000000006";
const tamperedFiles = (sourceId) => {
  const files = investigationFiles(sourceId);
  return {
    [`fixtures/v1.0.0/investigations/${TAMPERED_ID}/manifest.json`]: files["manifest.json"],
    [`fixtures/v1.0.0/investigations/${TAMPERED_ID}/events.ndjson`]: flipOneByte(files["events.ndjson"]),
    [`fixtures/v1.0.0/investigations/${TAMPERED_ID}/snapshot.json`]: files["snapshot.json"],
  };
};

test.describe("EXT-DATA-001: zero bundled data", () => {
  test("a fresh launch shows the Connect form and no investigation cards", async ({ page }) => {
    const seen = await recordGitHubRequests(page);
    await boot(page);
    await expect(page.getByRole("heading", { level: 2, name: "Connect data repository" })).toBeVisible();
    await expect(page.locator("form.causa-form")).toBeVisible();
    await expect(page.locator(".causa-card")).toHaveCount(0);
    await expect(page.locator(".causa-cards")).toHaveCount(0);
    await expect(page.locator(".causa-page__lede")).toHaveText("No investigations: no data repository is connected.");
    for (const { title } of fixtureInvestigations()) await expect(page.locator("body")).not.toContainText(title);
    expect(seen, "no request to GitHub before an explicit connect").toEqual([]);
  });

  test("no file in dist/site contains any fixture investigation id or title", () => {
    test.skip(!!process.env.CAUSA_BASE_URL, "file scan needs the local dist/site build; CAUSA_BASE_URL points at a deployed site (the UI check above still runs)");
    const fixtures = fixtureInvestigations();
    expect(fixtures).toHaveLength(5);
    const needles = fixtures.flatMap((f) => [f.id, f.title]);
    const files = walk(siteRoot);
    expect(files.length).toBeGreaterThan(50);
    const hits = files.flatMap((file) => hitsIn(readFileSync(file), needles).map((n) => `${relative(siteRoot, file)}: ${n}`));
    expect(hits).toEqual([]);
    expect(files.reduce((sum, f) => sum + statSync(f).size, 0)).toBeGreaterThan(1_000_000); // the scan covered the WASM payload
  });
});

test.describe("EXT-DATA-003: explicit selection", () => {
  const complete = FIXTURE_TARGET;
  const cases = [
    ["credential", { ...complete, token: "   " }, /A credential is required/],
    ["owner", { ...complete, owner: " " }, /Owner is not a valid GitHub user or organization name\./],
    ["repository", { ...complete, repo: " " }, /Repository is not a valid GitHub repository name\./],
    ["branch", { ...complete, branch: " " }, /Branch is not a valid branch name\./],
    ["workspace", { ...complete, root: " " }, /Workspace must be 'fixtures'/],
  ];

  for (const [missing, fields, message] of cases) {
    test(`a blank ${missing} sends nothing to GitHub and shows a notice`, async ({ page }) => {
      const seen = await recordGitHubRequests(page);
      await boot(page);
      await fillConnect(page, fields);
      await submitConnect(page);
      const notice = page.locator(".causa-notice");
      await expect(notice).toBeVisible();
      await expect(notice).toHaveAttribute("role", "status");
      await expect(notice).toHaveAttribute("data-tone", "critical");
      await expect(notice).toContainText(message);
      await expect(page.locator("form.causa-form")).toBeVisible();
      await expect(page.locator(".causa-repo")).toHaveCount(0);
      expect(seen).toEqual([]);
    });
  }

  for (const missing of ["owner", "repo", "branch", "root", "token"]) {
    test(`an empty ${missing} field is refused before submission (no request)`, async ({ page }) => {
      const seen = await recordGitHubRequests(page);
      await boot(page);
      const { [missing]: _omitted, ...rest } = complete;
      await fillConnect(page, rest);
      await submitConnect(page);
      const field = page.locator(`#cn-${missing}`);
      expect(await field.evaluate((el) => el.validity.valueMissing)).toBe(true);
      expect(await field.evaluate((el) => el.validationMessage)).not.toBe("");
      await expect(page.locator(".causa-repo")).toHaveCount(0);
      expect(seen).toEqual([]);
    });
  }
});

test.describe("MOCKED: EXT-DATA reading the frozen fixtures", () => {
  test("MOCKED: root 'fixtures' lists the five fixtures as verified cards; New investigation is disabled with the read-only reason", async ({ page }) => {
    const fake = await openFixtureDashboard(page);
    const fixtures = fixtureInvestigations();
    await expect(page.locator(".causa-card")).toHaveCount(5);
    await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(5);
    for (const f of fixtures) {
      const card = page.locator(".causa-card", { has: page.getByRole("link", { name: f.title, exact: true }) });
      await expect(card).toHaveCount(1);
      await expect(card.locator(".causa-card__status")).toHaveText("Verified");
      await expect(card.locator(".causa-card__meta")).toContainText(`Version ${f.expectedVersion}`);
      await expect(card.getByRole("link", { name: f.title, exact: true })).toHaveAttribute("href", `#/i/${f.id}/overview`);
    }
    await expect(page.locator(".causa-toolbar__count")).toHaveText("5 directories · 5 verified · 0 refused");
    const create = page.getByRole("button", { name: "New investigation" });
    await expect(create).toBeDisabled();
    await expect(page.locator("#create-reason")).toHaveText(/frozen v1\.0\.0 fixtures are read-only/);
    await expect(create).toHaveAccessibleDescription(/frozen v1\.0\.0 fixtures are read-only/);
    await expect(page.locator(".causa-repo__access")).toHaveText("Read-only: frozen fixtures");
    // Every request carried the credential as a bearer token, and nothing was written.
    expect(fake.log.length).toBeGreaterThan(5);
    expect(fake.log.every((r) => r.authorization === `Bearer ${FIXTURE_TARGET.token}`)).toBe(true);
    expect(fake.log.filter((r) => r.method !== "GET")).toEqual([]);
    expect(page.url()).not.toContain(FIXTURE_TARGET.token);
  });

  test("MOCKED: a sixth directory with one changed byte in events.ndjson is refused with a CAUSA.* code and cannot be opened", async ({ page }) => {
    const source = fixtureInvestigations()[0].id;
    await openFixtureDashboard(page, { extraFiles: tamperedFiles(source) });
    await expect(page.locator(".causa-card")).toHaveCount(6);
    await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(5);
    const refused = page.locator('.causa-card[data-status="invalid"]');
    await expect(refused).toHaveCount(1);
    await expect(refused.locator(".causa-card__title")).toHaveText(`Directory ${TAMPERED_ID}`);
    await expect(refused.locator(".causa-code")).toHaveText(/^CAUSA\.[A-Z0-9_.]+/);
    await expect(refused).toContainText("Refused: failed contract validation");
    await expect(refused).toContainText("Nothing from it is shown or trusted.");
    await expect(refused.getByRole("link")).toHaveCount(0);
    await expect(page.locator(".causa-toolbar__count")).toHaveText("6 directories · 5 verified · 1 refused");

    await goHash(page, `#/i/${TAMPERED_ID}/overview`);
    await expect(page.getByRole("heading", { level: 1, name: "This investigation cannot be shown" })).toBeVisible();
    await expect(page.locator("main").getByRole("alert")).toContainText(/failed contract validation and is not shown \(CAUSA\./);
    await expect(page.locator("#inv-title")).toHaveCount(0);
  });

  test("MOCKED: the changed byte itself is detected — the same one-byte change in place is refused as CAUSA.EVENTS_HASH_MISMATCH", async ({ page }) => {
    // A copied directory under a new id is refused first for its identity (event ids are UUIDv5s derived from the
    // investigation id), so this test isolates the byte change: one fixture's events.ndjson is altered in place.
    const { id, title } = fixtureInvestigations()[0];
    const path = `fixtures/v1.0.0/investigations/${id}/events.ndjson`;
    await openFixtureDashboard(page, { extraFiles: { [path]: flipOneByte(investigationFiles(id)["events.ndjson"]) } });
    await expect(page.locator(".causa-card")).toHaveCount(5);
    const refused = page.locator('.causa-card[data-status="invalid"]');
    await expect(refused).toHaveCount(1);
    await expect(refused.locator(".causa-card__title")).toHaveText(`Directory ${id}`);
    await expect(refused.locator(".causa-code")).toHaveText("CAUSA.EVENTS_HASH_MISMATCH");
    await expect(page.getByRole("link", { name: title, exact: true })).toHaveCount(0);
  });

});

test.describe("MOCKED: EXT-DATA refused access", () => {
  test.use({ allowedConsoleErrors: /status of 401/ });

  test("MOCKED: a 401 on the repository check shows the access-denied state and keeps the form", async ({ page }) => {
    const fake = await installFakeGitHub(page, { files: fixtureRepoFiles(), repoStatus: 401 });
    await boot(page);
    await fillConnect(page, FIXTURE_TARGET);
    await submitConnect(page);
    const alert = page.locator(".causa-connect .causa-alert");
    await expect(alert).toBeVisible();
    await expect(alert).toHaveAttribute("role", "alert");
    await expect(alert.locator(".causa-alert__title")).toHaveText("Access denied");
    await expect(alert).toContainText("GitHub did not accept the credential");
    await expect(alert).toContainText("Nothing was read or changed.");
    await expect(page.locator(".causa-masthead")).toContainText("Access denied");
    await expect(page.locator(".causa-card")).toHaveCount(0);
    await expect(page.locator("form.causa-form")).toBeVisible();
    await expect(page.locator("#cn-token")).toHaveValue(""); // the refused credential is not kept in the form
    expect(fake.log.map((r) => r.path)).toEqual(["/repos/causa-test/causa-data"]);
  });
});
