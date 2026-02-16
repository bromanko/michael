import { test as base, expect } from "@playwright/test";

// ---------------------------------------------------------------------------
// Playwright fixtures for browser tests
// ---------------------------------------------------------------------------

/**
 * Re-export test and expect for browser tests.
 * Add shared fixtures here as needed (e.g., authenticated page, seeded data).
 */
export const test = base;
export { expect };
