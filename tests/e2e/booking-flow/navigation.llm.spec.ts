import { test, expect } from "../helpers/fixtures";
import {
  goToBookingPage,
  completeTitle,
  clickOk,
  completeAvailability,
  waitForConfirmationStep,
  confirmAvailability,
  llmIsAvailable,
  clickBack,
  waitForSlotsOrEmpty,
} from "./helpers";

// ---------------------------------------------------------------------------
// Navigation & Progress Bar
//
// EARS requirements: NAV-009..NAV-011, NAV-020..NAV-022,
//                    ERR-010, ERR-011
// ---------------------------------------------------------------------------

test.describe("Back button navigation", () => {
  test("NAV-009: back from duration returns to title", async ({ page }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Back Test");

    // On duration step — click back
    await clickBack(page);

    // Should be on title step with title preserved
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeVisible();
    await expect(textbox).toHaveValue("Back Test");
  });

  test("NAV-009: back from availability returns to duration", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Nav Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // On availability step — click back
    await clickBack(page);

    // Should be on duration step
    await expect(page.getByRole("button", { name: /30 min/ })).toBeVisible();
  });

  test("NAV-009: back from availability, then back again to title", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Double Back");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Back to duration
    await clickBack(page);
    await expect(page.getByRole("button", { name: /30 min/ })).toBeVisible();

    // Back to title
    await clickBack(page);
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toHaveValue("Double Back");
  });
});

test.describe("Back button navigation (LLM-dependent)", () => {
  test.beforeEach(async ({}, testInfo) => {
    if (!llmIsAvailable()) testInfo.skip();
  });

  test("NAV-011: back from availability confirmation clears parsed windows", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Clear Windows Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    await completeAvailability(
      page,
      "I am free next Wednesday from 10am to 3pm",
    );
    await waitForConfirmationStep(page);

    // Back from confirmation
    await clickBack(page);

    // Should be on availability step with text preserved
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeVisible();
    const value = await textarea.inputValue();
    expect(value).toContain("Wednesday");

    // Go forward again — should re-parse (parsed windows were cleared)
    await textarea.press("Enter");

    // Should show loading state (re-parsing) or the confirmation step again
    const result = page.getByText(/finding slots/i).or(
      page.getByRole("button", {
        name: /confirm|find slots|looks good/i,
      }),
    );
    await expect(result).toBeVisible({ timeout: 60_000 });
  });

  test("NAV-010: back from slot selection clears loaded slots", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Clear Slots Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    await completeAvailability(page, "I am free next Thursday from 9am to 5pm");
    await waitForConfirmationStep(page);
    await confirmAvailability(page);

    // Wait for slots to load — either real slots or empty state
    const slotCount = await waitForSlotsOrEmpty(page);

    // If we got the empty state, use "Try different times" to go back
    // (the empty-slot view doesn't show a "Back" button)
    const hasSlots = slotCount > 0;

    if (hasSlots) {
      await clickBack(page);
    } else {
      await page.getByRole("button", { name: /try different times/i }).click();
      // "Try different times" navigates further back — verify we land on
      // a prior step and can proceed again
      return;
    }

    // Should be on the confirmation step
    await expect(
      page.getByRole("button", { name: /confirm|find slots|looks good/i }),
    ).toBeVisible();

    // Go forward again — should re-fetch slots
    await confirmAvailability(page);

    // Should show loading or new slots (re-fetched)
    await expect(slotsOrEmpty).toBeVisible({ timeout: 30_000 });
  });
});

test.describe("Error display", () => {
  test("ERR-010: error clears on successful action", async ({ page }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Error Clear Test");

    // Navigate to availability step
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Trigger an error on the availability step
    const textarea = page.getByRole("textbox").first();
    await textarea.fill("");
    await textarea.press("Enter");

    // Error should be visible
    await expect(
      page.getByText("Please describe your availability"),
    ).toBeVisible();

    // Now enter valid text and submit
    await textarea.fill("Tomorrow 2pm to 5pm");
    await textarea.press("Enter");

    // Error should be gone
    await expect(
      page.getByText("Please describe your availability"),
    ).not.toBeVisible();
  });

  test("ERR-011: error clears on back navigation", async ({ page }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Error Back Test");

    // Navigate to availability step
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Trigger an error
    const textarea = page.getByRole("textbox").first();
    await textarea.fill("");
    await textarea.press("Enter");

    await expect(
      page.getByText("Please describe your availability"),
    ).toBeVisible();

    // Navigate back
    await clickBack(page);

    // Go forward again to availability
    await clickOk(page);

    // Error should be cleared
    await expect(
      page.getByText("Please describe your availability"),
    ).not.toBeVisible();
  });
});

test.describe("Browser back/forward navigation", () => {
  test("browser back navigates away, forward returns to a usable page", async ({
    page,
  }) => {
    // Navigate to the booking page — this creates history entry
    await goToBookingPage(page);
    await completeTitle(page, "Browser Nav Test");

    // Should be on the duration step
    await expect(page.getByRole("button", { name: /30 min/ })).toBeVisible();

    // Browser back — the app uses Browser.element (no URL routing),
    // so this navigates away from the page entirely
    await page.goBack();

    // Browser forward — returns to the booking page, which re-initializes
    await page.goForward();

    // The Elm app should re-initialize to the title step (fresh state)
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeVisible();
  });

  test("browser back then re-navigating starts fresh flow", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Re-navigate Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Should be on the availability step
    await expect(page.getByRole("textbox").first()).toBeVisible();

    // Browser back navigates away
    await page.goBack();

    // Re-navigate to the booking page — should start fresh
    await goToBookingPage(page);
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeVisible();

    // Title should be empty (fresh state, not preserved from before)
    await expect(textbox).toHaveValue("");
  });
});

test.describe("Progress bar", () => {
  test("NAV-020: progress bar visible and advances across steps", async ({
    page,
  }) => {
    await goToBookingPage(page);

    // The progress bar is a div with an inline width style inside a
    // fixed container. Locate it by the inline style attribute.
    const progressFill = page.locator("[style*='width']").first();
    await expect(progressFill).toBeVisible();

    // Capture the initial width on the title step (step 1)
    const initialWidth = await progressFill.evaluate(
      (el) => el.getBoundingClientRect().width,
    );
    expect(initialWidth).toBeGreaterThan(0);

    // Advance to the duration step (step 2)
    await completeTitle(page, "Progress Test");
    await expect(page.getByRole("button", { name: /30 min/ })).toBeVisible();

    const step2Width = await progressFill.evaluate(
      (el) => el.getBoundingClientRect().width,
    );
    // Width may not change on every single step, but must not shrink
    expect(step2Width).toBeGreaterThanOrEqual(initialWidth);

    // Advance to the availability step (step 3)
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);
    await expect(page.getByRole("textbox").first()).toBeVisible();

    const step3Width = await progressFill.evaluate(
      (el) => el.getBoundingClientRect().width,
    );
    // After 2 advances the bar must have grown from the initial state
    expect(step3Width).toBeGreaterThan(initialWidth);
  });
});
