import { test } from "./fixtures";

/**
 * Test mode: "all" runs everything, "safe" skips destructive tests.
 * Set via MICHAEL_TEST_MODE environment variable.
 */
type TestMode = "all" | "safe";

function getTestMode(): TestMode {
  const mode = process.env.MICHAEL_TEST_MODE ?? "all";
  if (mode !== "all" && mode !== "safe") {
    throw new Error(
      `Invalid MICHAEL_TEST_MODE: "${mode}". Must be "all" or "safe".`,
    );
  }
  return mode;
}

/**
 * A tagged variant of `test` from `./fixtures` for destructive tests — those
 * that create real bookings or modify server state. Extends the project's
 * fixture-equipped `test` instance so it inherits the same fixture types.
 * Skipped automatically when MICHAEL_TEST_MODE=safe.
 *
 * Usage mirrors the standard Playwright `test` API:
 *   destructive("name", async ({ page }) => { ... });
 *   destructive.describe("group", () => { ... });
 *   destructive.beforeEach(async ({ page }) => { ... });
 */
export const destructive = test.extend({
  // eslint-disable-next-line no-empty-pattern
  _skipIfSafe: [
    async ({}, use, testInfo) => {
      testInfo.annotations.push({ type: "tag", description: "destructive" });
      if (getTestMode() === "safe") {
        test.skip();
      }
      await use(undefined);
    },
    { auto: true },
  ],
});
