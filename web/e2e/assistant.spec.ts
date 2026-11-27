import { expect, test } from "@playwright/test";
import { signIn } from "./helpers";

/**
 * Ask Truckroll with the real model. The wording of an answer varies, so this checks its shape:
 * sources first, a streamed answer that cites them, and a citation that opens its passage.
 */
test("a question gets sources, a cited answer, and a citation that opens its passage", async ({ page }) => {
  await signIn(page, "owner", "/assistant");

  const box = page.getByPlaceholder(/Ask about a customer/);
  await box.fill("What did we install at 14 Maple St?");
  await box.press("Enter");

  const sources = page.getByRole("list", { name: "Sources" });
  await expect(sources).toBeVisible({ timeout: 60_000 });
  await expect(sources.getByRole("button").first()).toContainText("14 Maple St");

  // Done when the footer names the model that answered.
  await expect(page.getByText(/via Ollama|via Claude/)).toBeVisible({ timeout: 150_000 });
  const citation = page.getByRole("button", { name: /^Source \d+:/ }).first();
  await expect(citation).toBeVisible();

  await citation.click();
  const drawer = page.getByRole("dialog");
  await expect(drawer).toBeVisible();
  await expect(drawer).toContainText(/14 Maple St|Maple/);

  const open = drawer.getByRole("link", { name: /Open the (customer|job)/ });
  if (await open.count()) {
    await open.click();
    await expect(page).toHaveURL(/\/(customers|jobs)\/\d+/);
  }
});
