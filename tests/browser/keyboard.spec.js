// UI keyboard operation: connect → open an investigation → open a command dialog → cancel, using the keyboard only.
import { test, expect, boot, WORKSPACE_TARGET, seededWorkspace } from "./support/app.js";
import { fixtureInvestigations } from "./support/causa-data.js";
import { installFakeGitHub } from "./support/fake-github.js";

/** Facts about the focused element: an identifying description and whether it shows a visible focus indicator. */
const focusFacts = (page) =>
  page.evaluate(() => {
    const el = document.activeElement;
    if (el === null || el === document.body) return { id: "", text: "", visible: false };
    const style = getComputedStyle(el);
    const ring = style.outlineStyle !== "none" && parseFloat(style.outlineWidth) >= 2;
    return { id: el.id, tag: el.tagName, text: (el.textContent ?? "").trim(), focusVisible: el.matches(":focus-visible"), visible: ring || style.boxShadow !== "none" };
  });

/** Press Tab until `matches(focusFacts)` holds; fail after `max` presses. Returns the facts of the matching element. */
const tabUntil = async (page, matches, max = 60) => {
  for (let i = 0; i < max; i++) {
    await page.keyboard.press("Tab");
    const facts = await focusFacts(page);
    if (matches(facts)) return facts;
  }
  throw new Error("keyboard focus never reached the expected element");
};

test.describe("MOCKED: keyboard-only operation", () => {
  test("MOCKED: connect, open an investigation, open a command dialog and cancel it — focus visible and restored", async ({ page }) => {
    const inv = fixtureInvestigations().find((f) => f.expectedLifecycle === "active");
    await installFakeGitHub(page, { files: seededWorkspace(inv.id), push: true });
    await boot(page);

    // After the handoff focus is on <main>, so Tab continues into the Connect form.
    await expect(page.locator("#causa-main")).toBeFocused();
    // Connect: type each field, then Enter submits the form.
    await tabUntil(page, (f) => f.id === "cn-owner");
    expect((await focusFacts(page)).visible).toBe(true);
    for (const [id, value] of [["cn-owner", WORKSPACE_TARGET.owner], ["cn-repo", WORKSPACE_TARGET.repo], ["cn-branch", WORKSPACE_TARGET.branch], ["cn-root", WORKSPACE_TARGET.root], ["cn-token", WORKSPACE_TARGET.token]]) {
      if ((await focusFacts(page)).id !== id) await tabUntil(page, (f) => f.id === id, 3);
      await page.keyboard.type(value);
    }
    await page.keyboard.press("Enter");
    await expect(page.locator('.causa-card[data-status="valid"]')).toHaveCount(1);

    // Open the investigation from its card.
    const link = await tabUntil(page, (f) => f.tag === "A" && f.text === inv.title);
    expect(link.visible).toBe(true);
    await page.keyboard.press("Enter");
    await expect(page.locator("#inv-title")).toHaveText(inv.title);

    // Open a command dialog from an enabled action.
    const command = await tabUntil(page, (f) => f.tag === "BUTTON" && f.text === "Record observation");
    expect(command.visible, "the focused command shows a focus indicator").toBe(true);
    expect(command.focusVisible).toBe(true);
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Record observation" });
    await expect(dialog).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.querySelector(".causa-dialog")?.contains(document.activeElement))).toBe(true);
    await expect(page.locator("#dlg-textB")).toBeFocused();

    // Cancel with the keyboard; focus returns to the command that opened the dialog.
    const cancel = await tabUntil(page, (f) => f.tag === "BUTTON" && f.text === "Cancel", 10);
    expect(cancel.visible).toBe(true);
    await page.keyboard.press("Enter");
    await expect(dialog).toBeHidden();
    await expect.poll(async () => (await focusFacts(page)).text).toBe("Record observation");
    expect(await page.evaluate(() => document.activeElement.closest(".causa-commands") !== null)).toBe(true);
  });
});
