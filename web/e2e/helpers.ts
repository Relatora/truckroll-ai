import { expect, type Locator, type Page } from "@playwright/test";

/** The demo users, as the sign-in page's role cards name them. Public demo credentials. */
export const people = {
  owner: { card: "Morgan Reid", first: "Morgan", role: "Owner", reviewsCalls: true },
  manager: { card: "Pat Gallagher", first: "Pat", role: "Branch manager", reviewsCalls: true },
  dispatcher: { card: "Alex Romero", first: "Alex", role: "Dispatcher", reviewsCalls: true },
  csr: { card: "Dana Whitfield", first: "Dana", role: "Customer service", reviewsCalls: true },
  technician: { card: "Nadia Kowalczyk", first: "Nadia", role: "Technician", reviewsCalls: false },
} as const;

export type Person = keyof typeof people;

/** Signs in the way a person does: pick the role card, press Sign in. */
export async function signIn(page: Page, who: Person, next = "/dashboard") {
  await page.goto(`/sign-in?next=${encodeURIComponent(next)}`);
  await page.getByRole("button", { name: new RegExp(`^Use ${people[who].card}`) }).click();
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.waitForURL((url) => !url.pathname.startsWith("/sign-in"));
}

/** A toast with this text (sonner renders them in a live region). */
export const toast = (page: Page, text: string | RegExp) => page.locator("[data-sonner-toast]").filter({ hasText: text });

/** Waits until the dispatch board's live channel is connected. */
export async function boardIsLive(page: Page) {
  await expect(page.getByText("Live", { exact: true })).toBeVisible({ timeout: 30_000 });
}

/**
 * Drags one element onto another the way a hand does: press, nudge, glide, release. Playwright's
 * `dragTo` jumps straight to the target in one move, and Chromium never starts an HTML5 drag from
 * the board's job blocks on a jump like that (found while writing the dispatch test), so this
 * moves in small steps.
 */
export async function drag(page: Page, source: Locator, target: Locator) {
  // The mouse only reaches what's on screen: bring the target in, then make sure the source is too.
  await target.scrollIntoViewIfNeeded();
  await source.scrollIntoViewIfNeeded();
  const from = await source.boundingBox();
  const to = await target.boundingBox();
  if (!from || !to) throw new Error("drag: source or target isn't on screen");

  const start = { x: from.x + Math.min(24, from.width / 2), y: from.y + from.height / 2 };
  const end = { x: to.x + to.width / 2, y: to.y + to.height / 2 };
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(start.x + 6, start.y + 6, { steps: 3 });
  await page.mouse.move(end.x, end.y, { steps: 12 });
  await page.mouse.up();
}
