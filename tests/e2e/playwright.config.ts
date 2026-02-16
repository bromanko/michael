import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.MICHAEL_TEST_URL ?? "http://localhost:8000";

export default defineConfig({
  testDir: "./booking-flow",
  forbidOnly: !!process.env.CI,
  workers: 1,
  reporter: [["list"], ["html", { open: "never" }]],

  use: {
    baseURL,
    trace: "on-first-retry",
    ...devices["Desktop Chrome"],
  },

  projects: [
    {
      // Tests that never call the LLM parse endpoint.
      // Fast, safe to run in parallel.
      name: "booking-safe",
      testMatch: /(?<!\.llm)\.spec\.ts$/,
      fullyParallel: true,
      retries: 0,
    },
    {
      // Tests that depend on the LLM parse endpoint.
      // Run serially with one retry to handle transient 429 rate limits.
      name: "booking-llm",
      testMatch: /\.llm\.spec\.ts$/,
      fullyParallel: false,
      retries: 2,
      timeout: 120_000,
    },
  ],
});
