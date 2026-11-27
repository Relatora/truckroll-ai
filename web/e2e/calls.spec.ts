import { expect, test } from "@playwright/test";
import { signIn, toast } from "./helpers";

/** A CSR turns this morning's call into a booked job and a filed follow-up, with the real model. */
test("analysing and approving a call books the job and files the follow-up", async ({ page }) => {
  await signIn(page, "csr", "/calls");

  await page.getByRole("button", { name: /Priya Raman on Maple Street/ }).click();
  await page.getByRole("button", { name: "Analyse the call" }).click();

  const approve = page.getByRole("button", { name: "Approve: book the job and file the follow-up" });
  await expect(approve).toBeVisible({ timeout: 150_000 });
  await expect(page.getByText(/Matched to Priya Raman at 14 Maple St/)).toBeVisible();

  await approve.click();

  await expect(toast(page, /Booked J-\d+/)).toBeVisible();
  await expect(page.getByText("Outbox: not sent")).toBeVisible();
  await expect(page.getByRole("radio", { name: /Outbox 1/ })).toBeVisible();
});
