import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.MICHAEL_TEST_URL ?? "http://localhost:8000";

/** Browser timezone — must match the server's MICHAEL_HOST_TIMEZONE so that
 *  natural-language availability like "9am to 5pm" overlaps with host
 *  business hours and slot labels display the expected times.
 *
 *  The Nix dev shell (flake.nix) pins this to "America/Los_Angeles".
 *  If you override it, slot-matching patterns (TIME_SLOT_PATTERN) and any
 *  assertions on displayed times must still hold for the chosen timezone. */
const browserTimezone =
  process.env.MICHAEL_HOST_TIMEZONE ?? "America/Los_Angeles";
if (!process.env.MICHAEL_HOST_TIMEZONE) {
  console.warn(
    "⚠ MICHAEL_HOST_TIMEZONE not set — defaulting to America/Los_Angeles. " +
      "Set this env var to match the host's timezone for reliable time-dependent tests.",
  );
}

export default defineConfig({
  globalSetup: "./global-setup.ts",
  testDir: "./booking-flow",
  forbidOnly: !!process.env.CI,
  workers: 1,
  reporter: [["list"], ["html", { open: "never" }]],

  use: {
    baseURL,
    trace: "on-first-retry",
    timezoneId: browserTimezone,
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
