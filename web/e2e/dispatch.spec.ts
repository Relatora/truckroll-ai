import { expect, test } from "@playwright/test";
import { boardIsLive, drag, signIn, toast } from "./helpers";

/** The seeded emergency at 14 Maple St, by its own words: other specs book more jobs at that address. */
const EMERGENCY = /Third time this has happened/;

/**
 * The dispatch board with a real mouse: drag the 14 Maple St emergency onto Nadia's lane, and
 * watch a second dispatcher's board move with it, live.
 */
test("dragging a job onto a lane assigns it, and every open board follows", async ({ page, browser }) => {
  await signIn(page, "dispatcher", "/dispatch");
  await boardIsLive(page);

  // A second person watching the same board.
  const other = await browser.newContext();
  const watcher = await other.newPage();
  await signIn(watcher, "dispatcher", "/dispatch");
  await boardIsLive(watcher);

  const waiting = page.getByRole("group", { name: "Waiting lane" });
  const nadia = page.getByRole("group", { name: "Nadia Kowalczyk's lane" });
  const emergency = waiting.getByTitle(EMERGENCY);
  await expect(emergency).toBeVisible();

  await drag(page, emergency, nadia);

  await expect(toast(page, /Assigned J-\d+ to Nadia Kowalczyk/)).toBeVisible();
  await expect(nadia.getByTitle(EMERGENCY)).toBeVisible();
  // The other board moved without a reload.
  await expect(watcher.getByRole("group", { name: "Nadia Kowalczyk's lane" }).getByTitle(EMERGENCY)).toBeVisible();

  // And back, the keyboard way: the job's menu.
  await nadia.getByTitle(EMERGENCY).click();
  await page.getByRole("menuitem", { name: "Back to waiting" }).click();
  await expect(toast(page, /is back in Waiting/)).toBeVisible();
  await expect(waiting.getByTitle(EMERGENCY)).toBeVisible();

  await other.close();
});

test("a lane without the licence refuses the drop", async ({ page }) => {
  await signIn(page, "dispatcher", "/dispatch");
  await boardIsLive(page);

  const emergency = page.getByRole("group", { name: "Waiting lane" }).getByTitle(EMERGENCY);
  // Ben holds a G3 only; the furnace emergency needs a G2.
  await drag(page, emergency, page.getByRole("group", { name: "Ben Okoro's lane" }));

  await expect(page.getByRole("group", { name: "Waiting lane" }).getByTitle(EMERGENCY)).toBeVisible();
  await expect(page.getByRole("group", { name: "Ben Okoro's lane" }).getByTitle(EMERGENCY)).toHaveCount(0);
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: /Assigned/ })).toHaveCount(0);
});
