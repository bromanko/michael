import { type Page, expect, test } from "@playwright/test";

// ---------------------------------------------------------------------------
// Black-box navigation helpers for the booking flow.
//
// These helpers drive the UI through each step using only user-visible
// elements (labels, button text, roles). No source-code knowledge needed.
// ---------------------------------------------------------------------------

/** Matches time-slot text like "2 PM", "12:30 AM", "9:00 PM". */
export const TIME_SLOT_PATTERN = /\d{1,2}(:\d{2})?\s*(AM|PM)/i;

/** Matches completion-step confirmation text: "You're booked" (with smart
 *  or straight apostrophe) or "booking confirmed". */
export const BOOKED_CONFIRMATION_PATTERN =
  /you[\u2019']re booked|booking confirmed/i;

/** Base URL for the test server, used to build route-interception patterns. */
const BASE_URL = process.env.MICHAEL_TEST_URL ?? "http://localhost:8000";

/** Known API paths that may be intercepted in tests. */
type KnownApiPath =
  | "/api/book"
  | "/api/csrf-token"
  | "/api/parse"
  | "/api/slots";

/** Build a route-interception URL anchored to the test server base URL. */
export function apiRoute(path: KnownApiPath): string {
  return `${BASE_URL}${path}`;
}

/** Navigate to the booking page and wait for the title step to load. */
export async function goToBookingPage(page: Page): Promise<void> {
  await page.goto("/");
  // The title step should have a text input — wait for it
  await page.getByRole("textbox").first().waitFor({ state: "visible" });
}

/** Complete the title step by entering a title and submitting. */
export async function completeTitle(page: Page, title: string): Promise<void> {
  const input = page.getByRole("textbox").first();
  await input.fill(title);
  await input.press("Enter");
}

/** Click the OK submit button (used on the title step). */
export async function clickOk(page: Page): Promise<void> {
  await page.getByRole("button", { name: /^OK/ }).click();
}

/** Click the step's primary submit button. Button text varies by step
 *  (OK, Find slots, Looks good, Review, etc.). This matches any of them. */
export async function clickStepSubmit(
  page: Page,
  expectedName?: RegExp,
): Promise<void> {
  const pattern = expectedName ?? /^(OK|review|find slots|looks good)/i;
  const submitBtn = page.getByRole("button", { name: pattern });
  await expect(submitBtn).toHaveCount(1);
  await submitBtn.click();
}

/** Complete the availability step by entering text and submitting. */
export async function completeAvailability(
  page: Page,
  text: string,
): Promise<void> {
  const textarea = page.getByRole("textbox").first();
  await textarea.fill(text);
  await textarea.press("Enter");
}

/** Wait for the availability confirmation step to appear (after parsing).
 *  If a 429 rate-limit error appears, waits briefly and re-submits the
 *  availability text (up to `maxRetries` times). */
export async function waitForConfirmationStep(
  page: Page,
  maxRetries = 3,
): Promise<void> {
  const deadline = Date.now() + 60_000 * (maxRetries + 1);

  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    const remaining = deadline - Date.now();
    if (remaining <= 0) break;

    // Poll with a short timeout so we can detect 429 errors quickly
    const pollTimeout = Math.min(remaining, 15_000);

    const confirmHeading = page.getByText(/did I get that right/i);

    try {
      await confirmHeading.waitFor({
        state: "visible",
        timeout: pollTimeout,
      });
      return; // Confirmation step appeared
    } catch {
      // Check if a 429 error is showing
      const has429 = await page
        .getByText(/Server error \(429\)/i)
        .isVisible()
        .catch(() => false);

      if (has429 && attempt < maxRetries) {
        // Wait for rate limit to clear, then re-submit
        await page.waitForTimeout(5_000);
        const submitBtn = page.getByRole("button", {
          name: /find slots/i,
        });
        if (await submitBtn.isEnabled().catch(() => false)) {
          await submitBtn.click();
        }
        continue;
      }

      // Not a 429, or out of retries — check if maybe just slow
      if (!has429 && remaining > pollTimeout) {
        // Still time left, keep polling without counting as a retry
        attempt--;
        continue;
      }
    }
  }

  // Final attempt — wait for whatever time remains
  const finalTimeout = Math.max(deadline - Date.now(), 5_000);
  await page
    .getByText(/did I get that right/i)
    .waitFor({ state: "visible", timeout: finalTimeout });
}

/** Confirm the parsed availability on the confirmation step. */
export async function confirmAvailability(page: Page): Promise<void> {
  await page
    .getByRole("button", { name: /confirm|find slots|looks good/i })
    .click();
}

/** Wait for slots to load and select the first available slot.
 *  Throws if no time-slot buttons are found — callers should check
 *  slot availability (e.g. via `waitForSlotsOrEmpty`) before calling. */
export async function selectFirstSlot(page: Page): Promise<void> {
  const timeSlotButtons = page.getByRole("button", {
    name: TIME_SLOT_PATTERN,
  });
  await timeSlotButtons.first().waitFor({ state: "visible", timeout: 30_000 });
  await timeSlotButtons.first().click();
}

/** Complete the contact information step. */
export async function completeContactInfo(
  page: Page,
  info: { name: string; email: string; phone?: string },
): Promise<void> {
  // Fill name — look for input labeled "name" or similar
  const nameInput = page.getByLabel(/name/i);
  await nameInput.fill(info.name);

  // Fill email
  const emailInput = page.getByLabel(/email/i);
  await emailInput.fill(info.email);

  // Fill phone if provided
  if (info.phone) {
    const phoneInput = page.getByLabel(/phone/i);
    await phoneInput.fill(info.phone);
  }

  // Submit via the step's submit button (text varies by step: OK, Review, etc.)
  await clickStepSubmit(page);
}

/** Confirm the booking on the final confirmation step. */
export async function confirmBooking(page: Page): Promise<void> {
  await page.getByRole("button", { name: /confirm booking/i }).click();
}

// ---------------------------------------------------------------------------
// Multi-step navigation helpers.
//
// These compose the single-step helpers above to navigate deep into the flow
// in one call. They use default test data; pass custom values when the test
// cares about specific inputs.
// ---------------------------------------------------------------------------

/**
 * Wait for the slot selection step to finish loading. Resolves once either
 * time-slot buttons or the "no overlapping slots" empty state is visible.
 * If a "Failed to load" error appears (e.g. from a 429 on /api/slots),
 * waits and re-confirms availability to retry.
 * Returns the number of available slot buttons.
 *
 * @param page - Playwright page instance
 * @param options.timeout - Per-attempt timeout in ms (default 30 000)
 * @param options.maxRetries - Retry count on load errors (default 2)
 */
export async function waitForSlotsOrEmpty(
  page: Page,
  {
    timeout = 30_000,
    maxRetries = 2,
  }: { timeout?: number; maxRetries?: number } = {},
): Promise<number> {
  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    const slotBtn = page
      .getByRole("button", { name: TIME_SLOT_PATTERN })
      .first();
    const emptyMsg = page.getByText(/no overlapping slots/i);
    const loadError = page.getByText(/failed to load/i);

    const result = slotBtn.or(emptyMsg).or(loadError);
    await expect(result).toBeVisible({ timeout });

    // If slots or empty message is visible, we're done
    if (
      (await slotBtn.isVisible().catch(() => false)) ||
      (await emptyMsg.isVisible().catch(() => false))
    ) {
      return page.getByRole("button", { name: TIME_SLOT_PATTERN }).count();
    }

    // Load error — go back and re-confirm to retry
    if (attempt < maxRetries) {
      await page.waitForTimeout(3_000);
      // Click "Looks good" again to re-trigger slot loading
      const confirmBtn = page.getByRole("button", {
        name: /looks good/i,
      });
      if (await confirmBtn.isVisible().catch(() => false)) {
        await confirmBtn.click();
      }
    }
  }
  return page.getByRole("button", { name: TIME_SLOT_PATTERN }).count();
}

/** Navigate from the booking page through to the availability confirmation
 *  step (after the LLM parses the availability text). */
export async function navigateToConfirmation(
  page: Page,
  options?: { title?: string; availability?: string },
): Promise<void> {
  const title = options?.title ?? "E2E Test Meeting";
  const availability =
    options?.availability ??
    "I am free next Tuesday from 9am to 5pm and next Wednesday from 10am to 4pm";

  await goToBookingPage(page);
  await completeTitle(page, title);
  await completeAvailability(page, availability);
  await waitForConfirmationStep(page);
}

/** Navigate from the booking page through to the slot selection step.
 *  Returns the number of available slot buttons.
 *
 *  When `requireSlots` is set, the test is skipped automatically if
 *  fewer than that many slots are found (defaults to 0 — no skip). */
export async function navigateToSlotSelection(
  page: Page,
  options?: {
    title?: string;
    availability?: string;
    requireSlots?: number;
  },
): Promise<number> {
  await navigateToConfirmation(page, options);
  await confirmAvailability(page);
  const count = await waitForSlotsOrEmpty(page);
  const min = options?.requireSlots ?? 0;
  if (min > 0 && count < min) {
    test.skip(true, `Need ${min} slot(s) but found ${count}`);
  }
  return count;
}

/** Navigate from the booking page through to the contact info step.
 *  Selects the first available slot. Skips the test automatically if
 *  no slots are available. */
export async function navigateToContactInfo(
  page: Page,
  options?: { title?: string; availability?: string },
): Promise<void> {
  await navigateToSlotSelection(page, { ...options, requireSlots: 1 });
  await selectFirstSlot(page);
  await expect(page.getByLabel(/name/i)).toBeVisible();
}

/** Navigate from the booking page through to the booking confirmation step
 *  (the final summary before confirming). Skips the test automatically
 *  if no slots are available. */
export async function navigateToBookingConfirmation(
  page: Page,
  options?: {
    title?: string;
    availability?: string;
    contact?: { name: string; email: string; phone?: string };
  },
): Promise<void> {
  await navigateToContactInfo(page, options);
  await completeContactInfo(
    page,
    options?.contact ?? {
      name: "Jane Doe",
      email: "jane@test.example.com",
      phone: "555-123-4567",
    },
  );
  await expect(
    page.getByRole("button", { name: /confirm booking/i }),
  ).toBeVisible();
}

/** Click the back button. */
export async function clickBack(page: Page): Promise<void> {
  await page.getByRole("button", { name: /back/i }).click();
}

/** Probe whether the LLM is available by making a test parse request
 *  using Playwright's `APIRequestContext`. This respects any proxy or
 *  base URL configuration from the Playwright config and manages cookies
 *  automatically.
 *
 *  Called once from `global-setup.ts`; spec files should read the cached
 *  result via `llmIsAvailable()` instead. */
export async function isLlmAvailable(
  request: import("@playwright/test").APIRequestContext,
): Promise<boolean> {
  try {
    // Fetch CSRF token — the context handles cookies automatically
    const csrfResp = await request.get("/api/csrf-token");
    if (!csrfResp.ok()) return false;
    const csrfBody: unknown = await csrfResp.json();
    if (
      !csrfBody ||
      typeof csrfBody !== "object" ||
      !("token" in csrfBody) ||
      typeof (csrfBody as Record<string, unknown>).token !== "string"
    )
      return false;
    const token = (csrfBody as { token: string }).token;

    const resp = await request.post("/api/parse", {
      headers: { "X-CSRF-Token": token },
      data: {
        message: "tomorrow 2pm to 5pm",
        timezone: "UTC",
        previousMessages: [],
      },
    });
    return resp.status() !== 500;
  } catch {
    return false;
  }
}

/** Read the cached LLM availability flag set by `global-setup.ts`. */
export function llmIsAvailable(): boolean {
  return process.env.LLM_AVAILABLE === "true";
}
