import { defineConfig, devices } from "@playwright/test";

/**
 * End-to-end tests and screenshot capture, against the real stack: start it first
 * (`dotnet run --project src/Truckroll.AppHost --launch-profile http`), with Ollama running.
 *
 *   npm run e2e       the demo path as each role
 *   npm run capture   regenerate the README screenshots and the demo video
 *
 * Both reset the demo data first (the "setup" project), so every run starts from the same company
 * on today's date. Real stack, real model: assertions on AI output check structure, not wording.
 */
const baseURL = process.env.TRUCKROLL_URL ?? "http://localhost:3000";

export default defineConfig({
  testDir: "./e2e",
  // One worker: the specs share one demo company and change it (assign, approve).
  workers: 1,
  fullyParallel: false,
  retries: 0,
  // A cold local model can take a while on the first answer.
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [["list"]],
  use: {
    baseURL,
    ...devices["Desktop Chrome"],
    viewport: { width: 1440, height: 900 },
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "setup", testMatch: /reset\.setup\.ts/ },
    { name: "e2e", testMatch: /.*\.spec\.ts/, testIgnore: /capture\.spec\.ts/, dependencies: ["setup"] },
    { name: "capture", testMatch: /capture\.spec\.ts/, dependencies: ["setup"] },
  ],
});
