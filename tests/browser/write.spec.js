// MOCKED: a writable workspace. Commands open a dialog, a submit publishes one atomic commit to the fake (PATCH of the
// branch ref) and the sync status reports progress. UI behavior only — never evidence of live persistence.
import { test, expect, boot, connect, openInvestigation, seededWorkspace, WORKSPACE_TARGET } from "./support/app.js";
import { fixtureInvestigations } from "./support/causa-data.js";
import { installFakeGitHub } from "./support/fake-github.js";

const syncLabel = (page) => page.locator('.causa-masthead [data-text="syncLabel"]');

test.describe("MOCKED: writable workspace", () => {
  test("MOCKED: New investigation — dialog, focus in the first field, submit PATCHes the branch, sync goes Saving → Synchronized", async ({ page }) => {
    const fake = await installFakeGitHub(page, { files: {}, push: true, writeLatencyMs: 300 });
    await boot(page);
    await connect(page, WORKSPACE_TARGET, { expectCards: 0 });
    await expect(page.getByRole("heading", { name: "No investigations in this workspace" })).toBeVisible();
    await expect(page.locator(".causa-repo__access")).toHaveText("Read and write (repository role); writes go to workspaces/demo/r002-test");
    await expect(syncLabel(page)).toHaveText("Up to date");

    const create = page.getByRole("button", { name: "New investigation" });
    await expect(create).toBeEnabled();
    await create.click();
    const dialog = page.getByRole("dialog", { name: "New investigation" });
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveAttribute("aria-modal", "true");
    await expect(page.locator("#dlg-textB")).toBeFocused();
    await expect(dialog.getByLabel("Initial observation")).toBeFocused();

    const head = fake.head();
    await page.keyboard.type("Checkout returns 'payment declined' for about one in five card payments since 09:40 UTC.");
    await dialog.getByLabel("Title").fill("R002 test: declined card payments");
    await dialog.getByRole("button", { name: "Save to GitHub" }).click();

    await expect(dialog).toBeHidden();
    await expect(syncLabel(page)).toHaveText("Saving to GitHub…");
    await expect(page.locator('.causa-masthead [aria-busy="true"]')).toHaveCount(1);
    await expect(syncLabel(page)).toHaveText("Synchronized", { timeout: 30_000 });
    await expect(page.locator('.causa-masthead [aria-busy="true"]')).toHaveCount(0);
    await expect(page.locator(".causa-notice")).toContainText(/Saved to GitHub and verified by an independent read-back \(commit [0-9a-f]{7}\)\./);

    const patches = fake.log.filter((r) => r.method === "PATCH");
    expect(patches).toHaveLength(1);
    expect(patches[0].path).toBe("/repos/causa-test/causa-data/git/refs/heads/main");
    expect(patches[0].body.force).toBe(false);
    expect(fake.head()).not.toBe(head);
    expect(fake.head()).toBe(patches[0].body.sha);
    const tree = fake.state().trees[fake.state().commits[fake.head()].tree];
    expect(Object.keys(tree).filter((p) => p.startsWith("workspaces/demo/r002-test/investigations/")).map((p) => p.split("/").pop()).sort())
      .toEqual(["events.ndjson", "manifest.json", "snapshot.json"]);

    // The new investigation is opened, from the verified state.
    await expect(page.locator("#inv-title")).toHaveText("R002 test: declined card payments");
    await expect(page).toHaveURL(/#\/i\/[0-9a-f-]{36}\/overview$/);
  });

  test("MOCKED: a disabled action shows the domain's reason next to it", async ({ page }) => {
    const { id } = fixtureInvestigations().find((f) => f.expectedLifecycle === "active");
    await installFakeGitHub(page, { files: seededWorkspace(id), push: true });
    await boot(page);
    await connect(page, WORKSPACE_TARGET, { expectCards: 1 });
    await openInvestigation(page, id);
    const list = page.locator(".causa-commands .causa-command-list");
    await expect(list).toBeVisible();
    const disabled = list.locator(".causa-command", { has: page.locator("button:disabled") });
    expect(await disabled.count()).toBeGreaterThan(0);
    const first = disabled.first();
    await expect(first.locator(".causa-command__reason")).not.toHaveText("");
    const reason = (await first.locator(".causa-command__reason").innerText()).trim();
    expect(reason.length).toBeGreaterThan(10);
    // Every disabled action has a reason; every enabled one has none.
    const rows = await list.locator(".causa-command").evaluateAll((els) =>
      els.map((li) => ({ disabled: li.querySelector("button").disabled, reason: li.querySelector(".causa-command__reason")?.textContent ?? "" })));
    expect(rows.filter((r) => r.disabled && r.reason.trim() === "")).toEqual([]);
    expect(rows.filter((r) => !r.disabled && r.reason.trim() !== "")).toEqual([]);
    expect(rows.some((r) => !r.disabled)).toBe(true);
  });
});

test.describe("MOCKED: advisory repository role (DC-004)", () => {
  // GitHub's refusal of the write is the expected outcome here; the browser logs each 403 response.
  test.use({ allowedConsoleErrors: /status of 403/ });

  test("MOCKED: a reported read-only role is advisory: saving is offered, GitHub's refusal is held with the change kept (DC-004)", async ({ page }) => {
    await installFakeGitHub(page, { files: {}, push: false });
    await boot(page);
    await connect(page, WORKSPACE_TARGET, { expectCards: 0 });
    await expect(page.locator(".causa-masthead")).toContainText("write may be refused");
    await page.getByRole("button", { name: "New investigation" }).click();
    await page.locator("#dlg-textA").fill("Refused by GitHub");
    await page.locator("#dlg-textB").fill("The role said read-only and GitHub agreed.");
    await page.locator('form[data-event="cmd.submit"] button[type="submit"]').click();
    await expect(page.getByRole("heading", { name: "Write refused" }).first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole("button", { name: "Use new token and retry" }).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Discard my change" }).first()).toBeVisible();
  });
});
