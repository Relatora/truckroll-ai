import { mkdirSync } from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { boardIsLive, signIn } from "./helpers";

/**
 * Regenerates the README's pictures from the running app: every screen in light and in dark, and
 * one recorded walk through the demo. `npm run capture` runs this, then encodes the video.
 *
 * Nothing here is staged: it's the real stack and the real model, so a screenshot shows what the
 * app does today. The demo data is reset first (the setup project), so the pictures match DEMO.md.
 */

const docs = path.resolve(__dirname, "../../docs");
const EMERGENCY = /Third time this has happened/;

// Several model answers in one test: give it room beyond the e2e default.
test.setTimeout(600_000);

/** Hides the dev-mode badge and waits for fonts, loading skeletons and entrance animations. */
async function settle(page: Page) {
  await page.addStyleTag({ content: "nextjs-portal { display: none !important; }" });
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('[aria-label^="Loading"], [aria-label="Ranking technicians"]')).toHaveCount(0, { timeout: 30_000 });
  await page.waitForTimeout(1_200);
}

for (const scheme of ["light", "dark"] as const) {
  test.describe(`screenshots, ${scheme}`, () => {
    test.use({ colorScheme: scheme });

    test(`every screen in ${scheme}`, async ({ page }) => {
      const folder = path.join(docs, "screenshots", scheme);
      mkdirSync(folder, { recursive: true });
      // A long page is shot by making the window as tall as the page, not with fullPage: the
      // backdrop is fixed to the window, and fullPage would show it stopping at 900 pixels.
      const shot = async (name: string, wholePage = false) => {
        await settle(page);
        if (wholePage) {
          const height = await page.evaluate(() => document.documentElement.scrollHeight);
          await page.setViewportSize({ width: 1440, height: Math.max(900, height) });
          await page.waitForTimeout(600);
        }
        await page.screenshot({ path: path.join(folder, `${name}.png`) });
        await page.setViewportSize({ width: 1440, height: 900 });
      };

      await page.goto("/");
      await shot("landing");

      await page.goto("/sign-in");
      await shot("sign-in");

      await signIn(page, "owner", "/dashboard");
      await expect(page.getByRole("heading", { name: /numbers$/ }).first()).toBeVisible();
      await shot("dashboard", true);

      // Guelph, where the demo story happens (the owner would otherwise land on the first branch).
      await page.goto("/dispatch?branch=GUE");
      await boardIsLive(page);
      await shot("dispatch", true);

      await page.goto("/customers");
      await page.getByPlaceholder("Name, phone or street").fill("Priya");
      await page.getByRole("link", { name: /Priya Raman/ }).first().click();
      await expect(page.getByRole("heading", { name: "Priya Raman" }).first()).toBeVisible();
      await shot("customer-360");

      // A job with its dispatch suggestions, once the model has written the reasons.
      await page.goto("/jobs");
      await page.getByPlaceholder("Job number, customer or street").fill("Maple");
      await page.getByRole("link", { name: EMERGENCY }).first().click();
      await expect(page.getByText(/^Sentences by /)).toBeVisible({ timeout: 150_000 });
      await shot("job-suggestions", true);

      await page.goto("/assistant");
      const box = page.getByPlaceholder(/Ask about a customer/);
      await box.fill("What did we install at 14 Maple St?");
      await box.press("Enter");
      await expect(page.getByText(/via Ollama|via Claude/)).toBeVisible({ timeout: 150_000 });
      await shot("assistant");
      await page.getByRole("button", { name: /^Source \d+:/ }).first().click();
      await expect(page.getByRole("dialog")).toBeVisible();
      await shot("assistant-source");
      await page.keyboard.press("Escape");

      await page.goto("/calls");
      // The light pass analyses the call; the dark pass finds it already drafted, under her name.
      await page.getByRole("button", { name: /Priya Raman/ }).first().click();
      const analyse = page.getByRole("button", { name: "Analyse the call" });
      const approve = page.getByRole("button", { name: "Approve: book the job and file the follow-up" });
      await expect(analyse.or(approve)).toBeVisible();
      if (await analyse.isVisible()) await analyse.click();
      await expect(approve).toBeVisible({ timeout: 150_000 });
      await shot("call-review", true);

      await page.goto("/settings/mcp");
      await expect(page.getByRole("heading", { name: /tokens/i }).first()).toBeVisible();
      await shot("mcp-access");
    });
  });
}

/**
 * The demo video: one dispatcher's morning in about a minute. Playwright's recording has no mouse
 * pointer, so the page draws one, or a drag would look like a job teleporting.
 */
test("demo video", async ({ browser }) => {
  const size = { width: 1440, height: 900 };
  const context = await browser.newContext({
    viewport: size,
    colorScheme: "light",
    recordVideo: { dir: path.resolve(__dirname, "../test-results/demo"), size },
  });
  await context.addInitScript(() => {
    addEventListener("DOMContentLoaded", () => {
      const style = document.createElement("style");
      style.textContent = `nextjs-portal { display: none !important; }
        #demo-cursor { position: fixed; z-index: 2147483647; width: 22px; height: 22px; margin: -11px 0 0 -11px;
          border-radius: 50%; background: rgb(15 23 42 / 0.35); border: 2px solid white; pointer-events: none;
          box-shadow: 0 2px 8px rgb(0 0 0 / 0.3); transition: transform 120ms ease; }
        #demo-cursor.down { transform: scale(0.7); background: rgb(15 23 42 / 0.6); }`;
      document.head.append(style);
      const cursor = document.createElement("div");
      cursor.id = "demo-cursor";
      document.body.append(cursor);
      const move = (event: MouseEvent) => {
        cursor.style.left = `${event.clientX}px`;
        cursor.style.top = `${event.clientY}px`;
      };
      // Drag events too: during an HTML5 drag the browser stops sending mousemove.
      for (const type of ["mousemove", "dragover", "drag"]) addEventListener(type, move as EventListener, true);
      addEventListener("mousedown", () => cursor.classList.add("down"), true);
      for (const type of ["mouseup", "dragend", "drop"]) addEventListener(type, () => cursor.classList.remove("down"), true);
    });
  });
  const page = await context.newPage();
  const pause = (ms = 1_200) => page.waitForTimeout(ms);
  // Moves the drawn pointer onto an element before clicking it, so viewers can follow.
  const point = async (locator: ReturnType<Page["locator"]>) => {
    await locator.scrollIntoViewIfNeeded();
    const box = (await locator.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 15 });
    await pause(300);
  };

  await page.goto("/sign-in?next=%2Fdispatch");
  await pause();
  const card = page.getByRole("button", { name: /^Use Alex Romero/ });
  await point(card);
  await card.click();
  const signInButton = page.getByRole("button", { name: "Sign in", exact: true });
  await point(signInButton);
  await signInButton.click();
  await page.waitForURL(/\/dispatch/);
  await boardIsLive(page);
  await pause(1_500);

  // Drag the emergency onto Nadia's lane, slowly enough to watch the lanes light up.
  const nadia = page.getByRole("group", { name: "Nadia Kowalczyk's lane" });
  const emergency = page.getByRole("group", { name: "Waiting lane" }).getByTitle(EMERGENCY);
  await nadia.scrollIntoViewIfNeeded();
  await pause(600);
  const from = (await emergency.boundingBox())!;
  const to = (await nadia.boundingBox())!;
  await page.mouse.move(from.x + 40, from.y + from.height / 2, { steps: 15 });
  await pause(400);
  await page.mouse.down();
  await page.mouse.move(from.x + 46, from.y + from.height / 2 + 6, { steps: 4 });
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 40 });
  await pause(700);
  await page.mouse.up();
  await expect(nadia.getByTitle(EMERGENCY)).toBeVisible();
  await pause(1_800);

  // Open the job to see who the rules would send and why.
  await nadia.getByTitle(EMERGENCY).click();
  await page.keyboard.press("Escape");
  const openJob = page.getByRole("link", { name: "Open the job" });
  await point(openJob);
  await openJob.click();
  await expect(page.getByText(/^Sentences by /)).toBeVisible({ timeout: 150_000 });
  await page.getByRole("heading", { name: /Who should go/i }).scrollIntoViewIfNeeded();
  await pause(3_000);

  // Ask the assistant, and open a citation.
  await page.goto("/assistant");
  const box = page.getByPlaceholder(/Ask about a customer/);
  await point(box);
  await box.click();
  await box.pressSequentially("What did we install at 14 Maple St?", { delay: 35 });
  await box.press("Enter");
  await expect(page.getByText(/via Ollama|via Claude/)).toBeVisible({ timeout: 150_000 });
  await pause(1_500);
  const citation = page.getByRole("button", { name: /^Source \d+:/ }).first();
  await point(citation);
  await citation.click();
  await pause(3_000);

  await context.close();
  const video = page.video();
  if (!video) throw new Error("no video was recorded");
  mkdirSync(path.resolve(__dirname, "../test-results"), { recursive: true });
  await video.saveAs(path.resolve(__dirname, "../test-results/demo.webm"));
});
