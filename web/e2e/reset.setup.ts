import { expect, test as setup } from "@playwright/test";

/**
 * Resets the demo company to today and waits for the search index to finish rebuilding, so
 * every run (tests or capture) starts from the same data. Development-only endpoints.
 */
setup("reset the demo data", async ({ request }) => {
  setup.setTimeout(240_000);

  const reset = await request.post("/api/dev/reset-demo");
  expect(reset.ok(), "Is the stack running in Development? Start it with the AppHost first.").toBeTruthy();

  // The reset queues an index rebuild: about 40 s with the embedding model warm.
  await expect
    .poll(async () => (await (await request.get("/api/dev/index")).json()).ready as boolean, { timeout: 200_000, intervals: [2_000] })
    .toBe(true);
});
