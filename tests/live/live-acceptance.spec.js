// Live acceptance: the built Causa app against a real GitHub repository with a real, short-lived credential.
// Every claim is checked twice: in the UI, and independently through the GitHub REST API by this test (never by the
// app). The credential is read from the environment, typed into the password field, and never printed or written.
import { expect, test } from "@playwright/test";
import { mkdirSync, writeFileSync } from "node:fs";

const env = (name) => {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required for live acceptance`);
  return value;
};
const token = env("CAUSA_LIVE_TOKEN");
const owner = env("CAUSA_LIVE_OWNER");
const repo = env("CAUSA_LIVE_REPO");
const branch = env("CAUSA_LIVE_BRANCH");
const run = `r002-live-${process.env.GITHUB_RUN_ID ?? Date.now()}`;
const prefix = `workspaces/demo/${run}/investigations/`;
const results = { run, owner, repo, branch, baseUrl: process.env.CAUSA_BASE_URL ?? "local dist/site", checks: [], timings: {} };
const record = (id, outcome, detail = {}) => results.checks.push({ id, outcome, ...detail });

// ---------------------------------------------------------------- Independent GitHub reads (test side only)
const api = async (path, init = {}) => {
  const response = await fetch(`https://api.github.com/repos/${owner}/${repo}${path}`, {
    ...init,
    headers: { Accept: "application/vnd.github+json", Authorization: `Bearer ${token}`, "X-GitHub-Api-Version": "2022-11-28", "Content-Type": "application/json", ...(init.headers ?? {}) },
  });
  const body = await response.json().catch(() => ({}));
  return { status: response.status, body };
};
const head = async () => (await api(`/git/ref/heads/${branch}`)).body.object.sha;
const treeAt = async (commit) => {
  const c = await api(`/git/commits/${commit}`);
  return (await api(`/git/trees/${c.body.tree.sha}?recursive=1`)).body;
};
const filesUnder = (tree, dir) => tree.tree.filter((e) => e.path.startsWith(dir) && e.type === "blob");
const blobText = async (sha) => Buffer.from((await api(`/git/blobs/${sha}`)).body.content, "base64").toString("utf8");

// ---------------------------------------------------------------- UI helpers
const boot = async (page) => {
  await page.goto("./");
  await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 120_000 });
};
const connect = async (page, { root, credential = token }) => {
  await page.fill("#cn-owner", owner);
  await page.fill("#cn-repo", repo);
  await page.fill("#cn-branch", branch);
  await page.fill("#cn-root", root);
  await page.fill("#cn-token", credential);
  await page.getByRole("button", { name: "Connect data repository" }).click();
};
const syncLabel = (page) => page.locator(".causa-masthead .causa-lozenge").nth(1);
const waitSynchronized = async (page) => {
  await expect(syncLabel(page)).toContainText("Synchronized", { timeout: 180_000 });
  const notice = await page.locator(".causa-notice p").innerText();
  return /commit ([0-9a-f]{7})/.exec(notice)?.[1];
};
const submitDialog = async (page, fields) => {
  for (const [selector, value] of Object.entries(fields)) await page.fill(selector, value);
  await page.locator(".causa-dialog button[type=submit]").click();
};

test.describe.configure({ mode: "serial" });

test.afterAll(() => {
  mkdirSync("test-results", { recursive: true });
  writeFileSync("test-results/live-acceptance.json", JSON.stringify(results, null, 2) + "\n");
});

test("L1 a fresh launch has zero investigations and a Connect action", async ({ page }) => {
  await boot(page);
  await expect(page.getByRole("heading", { name: "Connect data repository" })).toBeVisible();
  await expect(page.locator(".causa-card")).toHaveCount(0);
  record("L1-empty-first-launch", "PASS");
});

test("L2 an invalid credential is denied by GitHub and nothing is shown", async ({ page }) => {
  await boot(page);
  await connect(page, { root: "fixtures", credential: "ghs_invalidcausaR002credential0000000000" });
  await expect(page.locator(".causa-alert[data-tone=critical]")).toContainText("Access denied", { timeout: 60_000 });
  await expect(page.locator(".causa-card")).toHaveCount(0);
  record("L2-denied-without-authorization", "PASS");
});

test("L3 the frozen fixtures load, validate and replay; writing is not offered", async ({ page }) => {
  await boot(page);
  const t0 = Date.now();
  await connect(page, { root: "fixtures" });
  await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(5, { timeout: 120_000 });
  results.timings.fixturesLoadMs = Date.now() - t0;
  await expect(page.getByRole("button", { name: "New investigation" })).toBeDisabled();
  record("L3-authorized-read-and-replay", "PASS", { verified: 5 });
});

let created = null;

test("L4 create a new investigation: atomic commit, independent read-back, reload", async ({ page }) => {
  await boot(page);
  await connect(page, { root: run });
  await expect(page.getByRole("heading", { name: "No investigations in this workspace" })).toBeVisible({ timeout: 60_000 });
  const before = await head();
  const t0 = Date.now();
  await page.getByRole("button", { name: "New investigation" }).click();
  await submitDialog(page, { "#dlg-textA": `R002 live acceptance ${run}`, "#dlg-textB": "Checkout latency doubled after the 14:00 deploy (live acceptance observation)." });
  const short = await waitSynchronized(page);
  results.timings.createWriteVerifyMs = Date.now() - t0;
  const after = await head();
  expect(after.startsWith(short)).toBe(true);
  // Independent verification: one commit on top of the previous head, adding exactly three files under one id.
  const commit = (await api(`/git/commits/${after}`)).body;
  expect(commit.parents.map((p) => p.sha)).toEqual([before]);
  const files = filesUnder(await treeAt(after), prefix);
  expect(files.map((f) => f.path.split("/").pop()).sort()).toEqual(["events.ndjson", "manifest.json", "snapshot.json"]);
  const id = files[0].path.slice(prefix.length).split("/")[0];
  const manifest = JSON.parse(await blobText(files.find((f) => f.path.endsWith("manifest.json")).sha));
  expect(manifest.investigationId).toBe(id);
  expect(manifest.aggregateVersion).toBe(1);
  created = { id, commit: after };
  record("L4-new-investigation-github-write-read-back", "PASS", { commit: after, investigationId: id });

  // Reload: the credential is gone (memory only), nothing was stored in browser storage, data reloads from GitHub.
  const stored = await page.evaluate(() => JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage }));
  expect(stored.includes(token)).toBe(false);
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-boot", "ready", { timeout: 120_000 });
  await expect(page.getByRole("heading", { name: "Connect data repository" })).toBeVisible();
  await connect(page, { root: run });
  await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(1, { timeout: 120_000 });
  await expect(page.locator(".causa-card__title")).toContainText(`R002 live acceptance ${run}`);
  record("L4b-reload-requires-reauthorization-and-reloads", "PASS");
});

test("L5 concurrent writers: the stale writer gets a conflict, never an overwrite, and can re-apply", async ({ browser }) => {
  test.skip(created === null, "requires L4");
  const open = async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await boot(page);
    await connect(page, { root: run });
    await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(1, { timeout: 120_000 });
    await page.goto(`./#/i/${created.id}/overview`);
    await expect(page.locator(".causa-record__title")).toBeVisible();
    return { context, page };
  };
  const a = await open();
  const b = await open();
  // B writes first.
  await b.page.getByRole("button", { name: "Record observation" }).click();
  await submitDialog(b.page, { "#dlg-textB": "Second writer: error budget alert fired at 14:05." });
  await waitSynchronized(b.page);
  const afterB = await head();
  // A is stale (loaded version 1) and writes a hypothesis.
  await a.page.getByRole("button", { name: "Record hypothesis" }).click();
  await submitDialog(a.page, { "#dlg-textB": "The 14:00 deploy enabled synchronous fraud checks." });
  await expect(syncLabel(a.page)).toContainText("Conflict", { timeout: 180_000 });
  expect(await head()).toBe(afterB); // A did not overwrite B
  record("L5-concurrent-write-conflict-protection", "PASS", { headAfterB: afterB });
  // Recovery: reload the latest version and re-decide A's command on top of it.
  await a.page.getByRole("button", { name: "Reload latest and re-apply" }).first().click();
  await waitSynchronized(a.page);
  const final = await head();
  const files = filesUnder(await treeAt(final), prefix + created.id + "/");
  const events = (await blobText(files.find((f) => f.path.endsWith("events.ndjson")).sha)).trim().split("\n").map((l) => JSON.parse(l));
  expect(events.map((e) => e.eventType)).toEqual(["InvestigationStarted", "ObservationRecorded", "HypothesisRecorded"]);
  record("L5b-conflict-recovery-reapply", "PASS", { finalCommit: final, versions: events.length });
  await a.context.close();
  await b.context.close();
});

test("L6 malformed data placed in the repository is refused with a contract code", async ({ page }) => {
  test.skip(created === null, "requires L4");
  // Independently (test side) commit a copy of the investigation whose events.ndjson has one byte changed.
  const base = await head();
  const tree = await treeAt(base);
  const files = filesUnder(tree, prefix + created.id + "/");
  const forgedId = "00000000-0000-4000-8000-00000000c0de";
  const entries = await Promise.all(files.map(async (f) => {
    const name = f.path.split("/").pop();
    const text = await blobText(f.sha);
    const content = name === "events.ndjson" ? text.replace("Checkout", "Checkoub") : text;
    const blob = await api("/git/blobs", { method: "POST", body: JSON.stringify({ content: Buffer.from(content).toString("base64"), encoding: "base64" }) });
    return { path: `${prefix}${forgedId}/${name}`, mode: "100644", type: "blob", sha: blob.body.sha };
  }));
  const newTree = await api("/git/trees", { method: "POST", body: JSON.stringify({ base_tree: tree.sha, tree: entries }) });
  const commit = await api("/git/commits", { method: "POST", body: JSON.stringify({ message: "r002 live acceptance: forged investigation (must be refused)", tree: newTree.body.sha, parents: [base] }) });
  const moved = await api(`/git/refs/heads/${branch}`, { method: "PATCH", body: JSON.stringify({ sha: commit.body.sha, force: false }) });
  expect(moved.status).toBe(200);
  await boot(page);
  await connect(page, { root: run });
  await expect(page.locator('.causa-card[data-status="invalid"]')).toHaveCount(1, { timeout: 120_000 });
  const code = await page.locator(".causa-card[data-status=invalid] .causa-code").innerText();
  expect(code).toMatch(/^CAUSA\./);
  await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(1);
  record("L6-malformed-data-refusal", "PASS", { code });
});
