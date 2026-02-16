import { test as base } from "@playwright/test";

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
 * Re-export the base test for safe tests. Always runs regardless of test mode.
 */
export const test = base;

/**
 * A test tagged as "destructive" — creates real bookings or modifies server
 * state. Skipped when MICHAEL_TEST_MODE=safe.
 */
export const destructive = base.extend({
  // eslint-disable-next-line no-empty-pattern
  _skipIfSafe: [
    async ({}, use, testInfo) => {
      testInfo.annotations.push({ type: "tag", description: "destructive" });
      if (getTestMode() === "safe") {
        base.skip();
      }
      await use(undefined);
    },
    { auto: true },
  ],
});
