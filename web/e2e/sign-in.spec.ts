import { expect, test } from "@playwright/test";
import { people, signIn, type Person } from "./helpers";

/** Every demo role signs in, lands home, and sees only the places they may go. */
for (const who of Object.keys(people) as Person[]) {
  test(`${people[who].role} signs in and sees their own home`, async ({ page }) => {
    const person = people[who];

    await signIn(page, who);

    await expect(page).toHaveURL(/\/dashboard/);
    await expect(page.getByRole("heading", { level: 1 })).toContainText(person.first);
    await expect(page.getByText(person.role, { exact: false }).first()).toBeVisible();

    const dock = page.getByRole("navigation", { name: "Main" });
    await expect(dock.getByRole("link", { name: "Dispatch board" })).toBeVisible();
    await expect(dock.getByRole("link", { name: "Calls" })).toHaveCount(person.reviewsCalls ? 1 : 0);
  });
}

test("signing out returns to the sign-in page and keeps the app closed", async ({ page }) => {
  await signIn(page, "csr");

  await page.getByRole("button", { name: /^Account menu for/ }).click();
  await page.getByRole("menuitem", { name: "Sign out" }).click();

  await expect(page).toHaveURL(/\/sign-in/);
  await page.goto("/customers");
  await expect(page).toHaveURL(/\/sign-in\?next=%2Fcustomers/);
});
