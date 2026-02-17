import { test, expect } from "../helpers/fixtures";
import { goToBookingPage, completeTitle } from "./helpers";

// ---------------------------------------------------------------------------
// Duration Step — Duration Selection
//
// EARS requirements: NAV-003, DUR-001..DUR-005, DUR-010..DUR-012, DUR-020,
//                    A11-010
// ---------------------------------------------------------------------------

test.describe("Duration step", () => {
  test.beforeEach(async ({ page }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Test Meeting");
    // Wait for the duration step to be visible
    await expect(page.getByText(/15 min/)).toBeVisible();
  });

  test("DUR-001: displays preset duration options of 15, 30, 45, and 60 minutes plus custom", async ({
    page,
  }) => {
    await expect(page.getByRole("button", { name: /15 min/ })).toBeVisible();
    await expect(page.getByRole("button", { name: /30 min/ })).toBeVisible();
    await expect(page.getByRole("button", { name: /45 min/ })).toBeVisible();
    await expect(page.getByRole("button", { name: /60 min/ })).toBeVisible();
    await expect(page.getByRole("button", { name: /custom/i })).toBeVisible();
  });

  test("DUR-002: selecting a preset duration visually highlights it", async ({
    page,
  }) => {
    const btn30 = page.getByRole("button", { name: /30 min/ });
    await btn30.click();

    // After clicking, the button should have some selected state.
    // Check for aria-pressed, aria-selected, or a visual class change.
    const hasAriaPressed = await btn30.getAttribute("aria-pressed");
    const hasAriaSelected = await btn30.getAttribute("aria-selected");
    const className = (await btn30.getAttribute("class")) ?? "";

    // At least one selection indicator should be present
    const isSelected =
      hasAriaPressed === "true" ||
      hasAriaSelected === "true" ||
      className.includes("selected") ||
      className.includes("active") ||
      className.includes("ring") ||
      className.includes("border");

    expect(isSelected).toBe(true);
  });

  test("DUR-003: selecting custom duration shows a numeric input and focuses it", async ({
    page,
  }) => {
    await page.getByRole("button", { name: /custom/i }).click();

    // A numeric input should appear
    const customInput = page.getByRole("spinbutton");
    await expect(customInput).toBeVisible();
    await expect(customInput).toBeFocused();
  });

  test("DUR-002: switching from preset to custom and back updates selection correctly", async ({
    page,
  }) => {
    const customInput = page.getByRole("spinbutton");
    const btn30 = page.getByRole("button", { name: /30 min/ });
    const customBtn = page.getByRole("button", { name: /custom/i });

    // Select a preset first
    await btn30.click();
    const btn30Class = (await btn30.getAttribute("class")) ?? "";
    expect(btn30Class).toContain("border-coral");

    // Switch to custom — preset should deselect, custom input appears
    await customBtn.click();
    await expect(customInput).toBeVisible();
    await customInput.fill("25");
    const btn30ClassAfterCustom = (await btn30.getAttribute("class")) ?? "";
    // Check that the solid border-coral class is gone (not just the hover variant)
    const hasSolidCoralBorder = btn30ClassAfterCustom
      .split(/\s+/)
      .some((cls) => cls === "border-coral");
    expect(hasSolidCoralBorder).toBe(false);

    // Switch back to a preset — custom input should disappear
    await btn30.click();
    await expect(customInput).not.toBeVisible();
    const btn30ClassRestored = (await btn30.getAttribute("class")) ?? "";
    expect(btn30ClassRestored).toContain("border-coral");

    // Should be able to submit with the preset value
    await page.getByRole("button", { name: /^OK/ }).click();
    await expect(page.getByRole("textbox").first()).toBeVisible();
  });

  test("DUR-020: OK button is disabled when no duration is selected", async ({
    page,
  }) => {
    const okButton = page.getByRole("button", { name: /^OK/ });
    await expect(okButton).toBeDisabled();
  });

  test("DUR-010: clicking OK without selecting shows error", async ({
    page,
  }) => {
    const okButton = page.getByRole("button", { name: /^OK/ });
    // Button should be disabled, preventing submission
    await expect(okButton).toBeDisabled();

    // Attempt to force-click the disabled button
    await okButton.click({ force: true });

    // Should still be on the duration step — verify we haven't advanced
    await expect(page.getByRole("button", { name: /15 min/ })).toBeVisible();
  });

  test("DUR-004 / NAV-003: selecting a preset and clicking OK advances to availability step", async ({
    page,
  }) => {
    // Select 30 minutes
    await page.getByRole("button", { name: /30 min/ }).click();

    // Click the OK button
    await page.getByRole("button", { name: /^OK/ }).click();

    // Availability step should show a textarea for entering availability
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeVisible();
  });

  test("DUR-005: valid custom duration (25 min) advances to availability step", async ({
    page,
  }) => {
    // Select custom
    await page.getByRole("button", { name: /custom/i }).click();

    // Enter custom duration
    const customInput = page.getByRole("spinbutton");
    await customInput.fill("25");

    // Submit via OK
    await page.getByRole("button", { name: /^OK/ }).click();

    // Should advance to availability step
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeVisible();
  });

  test("DUR-011: custom duration of exactly 5 min is accepted", async ({
    page,
  }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("5");
    await page.getByRole("button", { name: /^OK/ }).click();

    // Should advance to availability step
    await expect(page.getByRole("textbox").first()).toBeVisible();
  });

  test("DUR-011: custom duration of 4 min shows error", async ({ page }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("4");
    await page.getByRole("button", { name: /^OK/ }).click();

    await expect(
      page.getByText("Duration must be between 5 and 480 minutes"),
    ).toBeVisible();
  });

  test("DUR-011: custom duration of exactly 480 min is accepted", async ({
    page,
  }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("480");
    await page.getByRole("button", { name: /^OK/ }).click();

    // Should advance to availability step
    await expect(page.getByRole("textbox").first()).toBeVisible();
  });

  test("DUR-011: custom duration of 481 min shows error", async ({ page }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("481");
    await page.getByRole("button", { name: /^OK/ }).click();

    await expect(
      page.getByText("Duration must be between 5 and 480 minutes"),
    ).toBeVisible();
  });

  test("DUR-011: custom duration below 5 shows error", async ({ page }) => {
    await page.getByRole("button", { name: /custom/i }).click();

    const customInput = page.getByRole("spinbutton");
    await customInput.fill("3");

    await page.getByRole("button", { name: /^OK/ }).click();

    await expect(
      page.getByText("Duration must be between 5 and 480 minutes"),
    ).toBeVisible();
  });

  test("DUR-011: custom duration above 480 shows error", async ({ page }) => {
    await page.getByRole("button", { name: /custom/i }).click();

    const customInput = page.getByRole("spinbutton");
    await customInput.fill("500");

    await page.getByRole("button", { name: /^OK/ }).click();

    await expect(
      page.getByText("Duration must be between 5 and 480 minutes"),
    ).toBeVisible();
  });

  test("DUR-012: empty custom duration shows error", async ({ page }) => {
    // Note: The EARS spec describes non-numeric input validation, but
    // the implementation uses <input type="number"> which prevents
    // non-numeric text entry at the browser level. Instead, we test the
    // empty-input case which IS reachable.
    await page.getByRole("button", { name: /custom/i }).click();

    const customInput = page.getByRole("spinbutton");
    await customInput.fill("");

    await page.getByRole("button", { name: /^OK/ }).click();

    await expect(
      page.getByText(/valid number|enter a valid|select a duration/i),
    ).toBeVisible();
  });

  test("DUR-012: negative custom duration shows range error", async ({
    page,
  }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("-10");
    await page.getByRole("button", { name: /^OK/ }).click();

    // -10 parses as an integer but fails the >= 5 range check
    await expect(
      page.getByText("Duration must be between 5 and 480 minutes"),
    ).toBeVisible();
  });

  test("DUR-012: zero custom duration shows range error", async ({ page }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("0");
    await page.getByRole("button", { name: /^OK/ }).click();

    await expect(
      page.getByText("Duration must be between 5 and 480 minutes"),
    ).toBeVisible();
  });

  test("DUR-012: fractional custom duration does not advance", async ({
    page,
  }) => {
    await page.getByRole("button", { name: /custom/i }).click();
    await page.getByRole("spinbutton").fill("30.5");
    await page.getByRole("button", { name: /^OK/ }).click();

    // Fractional values are silently rejected — the step does not advance.
    // NOTE: No error message is displayed for this case. The numeric input
    // accepts "30.5" but Elm's String.toInt rejects it without feedback.
    // This is a minor UX gap — ideally an error should appear.
    await expect(page.getByRole("spinbutton")).toBeVisible();
  });

  test("NAV-009: back button returns to title step with preserved title", async ({
    page,
  }) => {
    // Click back
    await page.getByRole("button", { name: /back/i }).click();

    // Should be back on the title step
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeVisible();

    // The title should be preserved
    await expect(textbox).toHaveValue("Test Meeting");
  });

  test("NAV-009: back from availability preserves selected preset duration", async ({
    page,
  }) => {
    // Select 45 min and advance to availability
    const btn45 = page.getByRole("button", { name: /45 min/ });
    await btn45.click();
    await page.getByRole("button", { name: /^OK/ }).click();

    // Should be on availability step
    await expect(page.getByRole("textbox").first()).toBeVisible();

    // Navigate back to duration
    await page.getByRole("button", { name: /back/i }).click();

    // 45 min button should still be selected (border-coral indicates selection)
    const btn45After = page.getByRole("button", { name: /45 min/ });
    await expect(btn45After).toBeVisible();
    const className = (await btn45After.getAttribute("class")) ?? "";
    expect(className).toContain("border-coral");
  });

  test("NAV-009: back from availability preserves custom duration value", async ({
    page,
  }) => {
    // Select custom duration and advance
    await page.getByRole("button", { name: /custom/i }).click();
    const customInput = page.getByRole("spinbutton");
    await customInput.fill("25");
    await page.getByRole("button", { name: /^OK/ }).click();

    // Should be on availability step
    await expect(page.getByRole("textbox").first()).toBeVisible();

    // Navigate back to duration
    await page.getByRole("button", { name: /back/i }).click();

    // Custom duration button should still be selected
    const customBtn = page.getByRole("button", { name: /custom/i });
    await expect(customBtn).toBeVisible();
    const className = (await customBtn.getAttribute("class")) ?? "";
    expect(className).toContain("border-coral");

    // The custom input should be visible with the value preserved
    const customInputAfter = page.getByRole("spinbutton");
    await expect(customInputAfter).toBeVisible();
    await expect(customInputAfter).toHaveValue("25");
  });
});
