import { request } from "@playwright/test";
import { isLlmAvailable } from "./booking-flow/helpers";

/**
 * Playwright global setup — runs once before any spec file.
 *
 * Creates an APIRequestContext that respects the Playwright config's
 * baseURL / proxy settings, probes the LLM endpoint once, and caches
 * the result in the `LLM_AVAILABLE` environment variable. Spec files
 * read the cached value via `llmIsAvailable()` from helpers.
 */
export default async function globalSetup(): Promise<void> {
  const baseURL = process.env.MICHAEL_TEST_URL ?? "http://localhost:8000";
  const ctx = await request.newContext({ baseURL });
  try {
    const available = await isLlmAvailable(ctx);
    process.env.LLM_AVAILABLE = String(available);
  } finally {
    await ctx.dispose();
  }
}
