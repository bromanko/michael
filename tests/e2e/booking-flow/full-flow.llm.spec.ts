import { test, expect } from "../helpers/fixtures";
import { destructive } from "../helpers/tags";
import {
  goToBookingPage,
  completeTitle,
  clickOk,
  clickStepSubmit,
  completeAvailability,
  waitForConfirmationStep,
  confirmAvailability,
  completeContactInfo,
  confirmBooking,
  llmIsAvailable,
  navigateToConfirmation,
  navigateToSlotSelection,
  navigateToContactInfo,
  navigateToBookingConfirmation,
  TIME_SLOT_PATTERN,
  apiRoute,
} from "./helpers";

// ---------------------------------------------------------------------------
// Full Booking Flow — End-to-End Happy Path
//
// These tests drive the entire booking flow from title to completion.
// They require the LLM to be available for parsing availability text.
//
// Tests that share the same starting step and don't mutate flow state are
// combined into single tests to minimise LLM round-trips. Each combined
// test documents the EARS requirement IDs it covers.
//
// EARS requirements: NAV-001..NAV-008, ACF-001..ACF-003, SSE-001..SSE-003,
//                    CTI-001..CTI-013, BCF-001..BCF-004, BCF-010,
//                    CMP-001..CMP-002, NAV-020
// ---------------------------------------------------------------------------

const FLOW_TITLE = "E2E Full Flow Meeting";
const FLOW_AVAILABILITY =
  "I am free next Tuesday from 9am to 5pm and next Wednesday from 10am to 4pm";
const FLOW_OPTS = { title: FLOW_TITLE, availability: FLOW_AVAILABILITY };

test.describe("Availability confirmation step", () => {
  test.beforeEach(async ({}, testInfo) => {
    if (!llmIsAvailable()) testInfo.skip();
  });

  test("ACF-001: displays parsed availability windows with date and time", async ({
    page,
  }) => {
    await navigateToConfirmation(page, FLOW_OPTS);

    // Should show at least one date-like string and time range
    // NOTE: Implementation uses "9 AM" for round hours, not "9:00 AM"
    const timeText = page.getByText(TIME_SLOT_PATTERN);
    await expect(timeText.first()).toBeVisible();
  });

  test("ACF-003: back button returns to availability with text preserved", async ({
    page,
  }) => {
    await navigateToConfirmation(page, FLOW_OPTS);

    await page.getByRole("button", { name: /back/i }).click();

    // Should be back on the availability step with text preserved
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeVisible();
    const value = await textarea.inputValue();
    expect(value).toContain("free next Tuesday");
  });
});

test.describe("Slot selection step", () => {
  test.beforeEach(async ({}, testInfo) => {
    if (!llmIsAvailable()) testInfo.skip();
  });

  // SSE-001 and SSE-003 combined: both start at slot selection. SSE-001
  // makes read-only assertions, then SSE-003 clicks through to verify
  // advancement. One LLM round-trip instead of two.
  test("SSE-001 / SSE-003 / NAV-006: slots are selectable and clicking one advances to contact info", async ({
    page,
  }) => {
    const slotCount = await navigateToSlotSelection(page, FLOW_OPTS);

    if (slotCount === 0) {
      // No slots — check the empty state message instead (SSE-001 alternate)
      await expect(page.getByText(/no overlapping slots/i)).toBeVisible();
      return;
    }

    const slotButtons = page.getByRole("button", {
      name: TIME_SLOT_PATTERN,
    });

    // SSE-001: at least one slot is a clickable button with time info
    await expect(slotButtons.first()).toBeVisible();
    await expect(slotButtons.first()).toBeEnabled();

    // SSE-003 / NAV-006: clicking a slot advances to contact info step
    await slotButtons.first().click();
    await expect(page.getByLabel(/name/i)).toBeVisible();
  });

  test("SSE-003: double-clicking a slot does not cause duplicate navigation", async ({
    page,
  }) => {
    const slotCount = await navigateToSlotSelection(page, FLOW_OPTS);
    if (slotCount === 0) {
      test.skip();
      return;
    }

    const slotButtons = page.getByRole("button", {
      name: TIME_SLOT_PATTERN,
    });

    // Double-click rapidly — should not cause broken state
    await slotButtons.first().dblclick();

    // Should land on contact info step normally
    await expect(page.getByLabel(/name/i)).toBeVisible();

    // Should not show any error
    const error = page.getByText(/error/i);
    await expect(error).toHaveCount(0);
  });

  test("SSE-003: rapidly clicking different slots does not cause broken state", async ({
    page,
  }) => {
    const slotCount = await navigateToSlotSelection(page, FLOW_OPTS);
    if (slotCount < 2) {
      test.skip();
      return;
    }

    const slotButtons = page.getByRole("button", {
      name: TIME_SLOT_PATTERN,
    });

    // Click two different slots in rapid succession
    await slotButtons.nth(0).click({ delay: 0 });
    await slotButtons.nth(1).click({ delay: 0 });

    // Should land on contact info step — not error or broken state
    await expect(page.getByLabel(/name/i)).toBeVisible();
  });

  test("SSE-020: empty slots shows message and try-different-times link", async ({
    page,
  }) => {
    // Navigate with availability that likely won't overlap host hours
    await goToBookingPage(page);
    await completeTitle(page, "Weekend Meeting");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Ask for a weekend which likely has no host availability
    await completeAvailability(
      page,
      "I am only free this Saturday from midnight to 3am",
    );

    // Wait for parsing
    await waitForConfirmationStep(page);
    await confirmAvailability(page);

    // Wait for the response
    const result = page
      .getByText(/no overlapping slots/i)
      .or(page.getByRole("button", { name: TIME_SLOT_PATTERN }).first());
    await expect(result).toBeVisible({ timeout: 30_000 });

    // If we got no slots, verify the empty state
    const noSlotsMessage = page.getByText(/no overlapping slots/i);
    if ((await noSlotsMessage.count()) > 0) {
      await expect(noSlotsMessage).toBeVisible();
      // Should have a "try different times" link
      await expect(
        page
          .getByText(/try different times/i)
          .or(page.getByRole("link", { name: /try different/i }))
          .or(page.getByRole("button", { name: /try different/i })),
      ).toBeVisible();
    }
  });
});

test.describe("Contact information step", () => {
  test.beforeEach(async ({}, testInfo) => {
    if (!llmIsAvailable()) testInfo.skip();
  });

  // CTI-001, CTI-010, CTI-011, CTI-012 (invalid) combined: all operate on
  // the contact info step without advancing. Validation errors keep us on the
  // same step, so we can test them sequentially with one LLM round-trip.
  test("CTI-001 / CTI-010 / CTI-011 / CTI-012: focus, name validation, email validation (invalid and valid edge cases)", async ({
    page,
  }) => {
    if (!(await navigateToContactInfo(page, FLOW_OPTS))) {
      test.skip();
      return;
    }

    const nameInput = page.getByLabel(/name/i);
    const emailInput = page.getByLabel(/email/i);

    // CTI-001: name input is focused on the contact step
    await expect(nameInput).toBeFocused();

    // CTI-010: empty name shows error
    await nameInput.fill("");
    await emailInput.fill("test@example.com");
    await clickStepSubmit(page);
    await expect(page.getByText("Please enter your name")).toBeVisible();

    // CTI-011: empty email shows error
    await nameInput.fill("Jane");
    await emailInput.fill("");
    await clickStepSubmit(page);
    await expect(
      page.getByText("Please enter your email address"),
    ).toBeVisible();

    // CTI-012: invalid emails show error
    const invalidEmails = ["bad", "@no.com", "a@b", "a@b."];
    for (const invalid of invalidEmails) {
      await emailInput.fill(invalid);
      await clickStepSubmit(page);
      await expect(
        page.getByText("Please enter a valid email address"),
      ).toBeVisible();
    }

    // CTI-012: valid edge-case emails are accepted (no validation error).
    // These are commonly rejected by overly strict regexes.
    const validEdgeCaseEmails = [
      "user+tag@example.com",
      "user@sub.example.com",
      "user@example.co.uk",
    ];
    for (const valid of validEdgeCaseEmails) {
      await emailInput.fill(valid);
      await clickStepSubmit(page);
      await expect(
        page.getByText("Please enter a valid email address"),
      ).not.toBeVisible();

      // Submission advanced to the next step — go back for the next iteration
      await page.getByRole("button", { name: /back/i }).click();
      await expect(nameInput).toBeVisible();
    }
  });

  // CTI-002, CTI-012 (valid), CTI-013 combined: all test that valid contact
  // info advances to the confirmation step. One navigation covers valid email
  // acceptance, optional phone, and step advancement.
  test("CTI-002 / CTI-012 / CTI-013 / NAV-007: valid contact info advances, phone is optional", async ({
    page,
  }) => {
    if (!(await navigateToContactInfo(page, FLOW_OPTS))) {
      test.skip();
      return;
    }

    // CTI-013: phone is optional — submit without it
    await page.getByLabel(/name/i).fill("Jane Doe");
    await page.getByLabel(/email/i).fill("jane@example.com");
    // Leave phone empty

    await clickStepSubmit(page);

    // CTI-002 / CTI-012 / NAV-007: valid input advances to confirmation step
    await expect(
      page.getByRole("button", { name: /confirm booking/i }),
    ).toBeVisible();
  });
});

test.describe("Booking confirmation step", () => {
  test.beforeEach(async ({}, testInfo) => {
    if (!llmIsAvailable()) testInfo.skip();
  });

  // BCF-001 and BCF-002 combined: both make read-only assertions on the same
  // confirmation page state (navigated with phone provided).
  test("BCF-001 / BCF-002: confirmation shows all booking details including phone", async ({
    page,
  }) => {
    if (!(await navigateToBookingConfirmation(page, FLOW_OPTS))) {
      test.skip();
      return;
    }

    // BCF-001: confirmation summary displays key booking details
    await expect(page.getByText(FLOW_TITLE)).toBeVisible();
    await expect(page.getByText(/30 min/)).toBeVisible();
    await expect(page.getByText("Jane Doe")).toBeVisible();
    await expect(page.getByText("jane@test.example.com")).toBeVisible();

    // Should show a time range
    await expect(page.getByText(TIME_SLOT_PATTERN).first()).toBeVisible();

    // BCF-002: phone number is displayed when provided
    await expect(page.getByText("555-123-4567")).toBeVisible();
  });

  test("BCF-003: confirmation omits phone when not provided", async ({
    page,
  }) => {
    // Navigate to contact info, then submit without phone
    if (!(await navigateToContactInfo(page, FLOW_OPTS))) {
      test.skip();
      return;
    }
    await completeContactInfo(page, {
      name: "No Phone Person",
      email: "nophone@test.example.com",
    });

    await expect(
      page.getByRole("button", { name: /confirm booking/i }),
    ).toBeVisible();

    // Phone number text should not be present
    const phoneText = page.getByText(/phone/i);
    const phoneCount = await phoneText.count();
    if (phoneCount > 0) {
      const content = await phoneText.first().textContent();
      expect(content).not.toContain("555");
    }
  });

  test("BCF-004: back from booking confirmation preserves contact info", async ({
    page,
  }) => {
    if (!(await navigateToBookingConfirmation(page, FLOW_OPTS))) {
      test.skip();
      return;
    }

    // Go back from booking confirmation to contact info
    await page.getByRole("button", { name: /back/i }).click();

    // All contact fields should retain their values
    await expect(page.getByLabel(/name/i)).toHaveValue("Jane Doe");
    await expect(page.getByLabel(/email/i)).toHaveValue(
      "jane@test.example.com",
    );
    await expect(page.getByLabel(/phone/i)).toHaveValue("555-123-4567");
  });

  test("BCF-010: confirm button shows loading state while booking", async ({
    page,
  }) => {
    if (!(await navigateToBookingConfirmation(page, FLOW_OPTS))) {
      test.skip();
      return;
    }

    // Delay the book response so the loading state is reliably visible
    await page.route(apiRoute("/api/book"), async (route) => {
      await new Promise((r) => setTimeout(r, 500));
      await route.continue();
    });
    try {
      const confirmBtn = page.getByRole("button", {
        name: /confirm booking/i,
      });
      await confirmBtn.click();

      // The button should show "Booking..." while the request is pending
      await expect(
        page.getByRole("button", { name: /booking\.\.\./i }),
      ).toBeVisible({ timeout: 5_000 });
    } finally {
      await page.unroute(apiRoute("/api/book"));
    }

    // After the response, the flow should proceed to completion or error
    const completion = page
      .getByText(/you're booked|booking confirmed|booked/i)
      .or(page.getByText(/slot.*no longer available/i))
      .or(page.getByText(/failed/i));
    await expect(completion).toBeVisible({ timeout: 30_000 });
  });
});

test.describe("Completion step", () => {
  // These are destructive — they create real bookings.
  // CMP-001, CMP-002, and NAV-022 are combined: all make read-only
  // assertions on the completion page. One booking instead of three.
  destructive.describe("booking completion", () => {
    destructive.beforeEach(async ({}, testInfo) => {
      if (!llmIsAvailable()) testInfo.skip();
    });

    destructive(
      "CMP-001 / CMP-002 / NAV-022: completion shows booking ID, email confirmation, and no back button",
      async ({ page }) => {
        if (!(await navigateToBookingConfirmation(page, FLOW_OPTS))) {
          test.skip();
          return;
        }
        await confirmBooking(page);

        // CMP-001 / NAV-008: successful booking shows completion message
        const completion = page
          .getByText(/you're booked/i)
          .or(page.getByText(/booking confirmed/i));
        await expect(completion).toBeVisible({ timeout: 30_000 });

        // CMP-001: displays a booking ID (UUID-like)
        await expect(
          page.getByText(
            /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i,
          ),
        ).toBeVisible();

        // CMP-002: shows email confirmation message
        await expect(
          page
            .getByText(/confirmation email/i)
            .or(page.getByText(/email is on its way/i)),
        ).toBeVisible();

        // NAV-022: no back button on completion step
        const backButton = page.getByRole("button", { name: /back/i });
        await expect(backButton).toHaveCount(0);
      },
    );
  });
});
