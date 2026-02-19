import { test, expect } from "../helpers/fixtures";
import { goToBookingPage } from "./helpers";

// ---------------------------------------------------------------------------
// Title Step — Meeting Title Input
//
// EARS requirements: NAV-001, NAV-002, NAV-021, TTL-001, TTL-002,
//                    TTL-010, TTL-020, NAV-020, A11-010
// ---------------------------------------------------------------------------

test.describe("Title step", () => {
  test.beforeEach(async ({ page }) => {
    await goToBookingPage(page);
  });

  test("NAV-001: booking page loads with the title step displayed", async ({
    page,
  }) => {
    // The first step should show a text input for the meeting title
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeVisible();
  });

  test("TTL-001: title input is focused on load", async ({ page }) => {
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeFocused();
  });

  test("TTL-020: submit button is disabled when title is empty", async ({
    page,
  }) => {
    // The OK submit button should be disabled when the title is empty
    const submitButton = page.getByRole("button", { name: /^OK/ });
    await expect(submitButton).toBeDisabled();
  });

  test("TTL-020: submit button enables when title has non-whitespace content", async ({
    page,
  }) => {
    const textbox = page.getByRole("textbox").first();
    const submitButton = page.getByRole("button", { name: /^OK/ });

    // Initially disabled
    await expect(submitButton).toBeDisabled();

    // Type a title
    await textbox.fill("Planning Meeting");

    // Now it should be enabled
    await expect(submitButton).toBeEnabled();
  });

  test("TTL-010: submitting empty title does not advance (button disabled)", async ({
    page,
  }) => {
    // The OK button is disabled when the title is empty, so
    // pressing Enter on an empty field should not advance the step.
    const textbox = page.getByRole("textbox").first();
    await textbox.fill("");
    await textbox.press("Enter");

    // Should still be on the title step — textbox still visible
    await expect(textbox).toBeVisible();

    // Should NOT have advanced — no availability textarea should appear
    // (the title input is also a textbox, but we confirm we're still on
    // the same step by checking the submit button remains disabled)
    const submitButton = page.getByRole("button", { name: /^OK/ });
    await expect(submitButton).toBeDisabled();
  });

  test("TTL-002 / NAV-002: entering a title and pressing Enter advances to availability step", async ({
    page,
  }) => {
    const textbox = page.getByRole("textbox").first();
    await textbox.fill("Project Sync");
    await textbox.press("Enter");

    // Availability step should appear with a textarea
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeVisible();
    await expect(page.getByText(/when are you free/i)).toBeVisible();
  });

  test("NAV-002: clicking OK button advances to availability step", async ({
    page,
  }) => {
    const textbox = page.getByRole("textbox").first();
    await textbox.fill("Team Standup");

    const submitButton = page.getByRole("button", { name: /^OK/ });
    await submitButton.click();

    // Availability step should appear
    await expect(page.getByText(/when are you free/i)).toBeVisible();
  });

  test("TTL-010: whitespace-only title does not advance", async ({ page }) => {
    const textbox = page.getByRole("textbox").first();
    const submitButton = page.getByRole("button", { name: /^OK/ });

    await textbox.fill("   ");

    // Button should remain disabled for whitespace-only input
    await expect(submitButton).toBeDisabled();

    // Enter key should also not advance
    await textbox.press("Enter");
    await expect(textbox).toBeVisible();
    await expect(page.getByRole("button", { name: /^OK/ })).toBeDisabled();
  });

  test("TTL-020: submit button stays disabled for whitespace-only title", async ({
    page,
  }) => {
    const textbox = page.getByRole("textbox").first();
    const submitButton = page.getByRole("button", { name: /^OK/ });

    // Tab, newline-like whitespace characters
    await textbox.fill("\t  \t  ");
    await expect(submitButton).toBeDisabled();
  });

  test("title with special characters and emoji advances normally", async ({
    page,
  }) => {
    const textbox = page.getByRole("textbox").first();

    await textbox.fill("🎉 Project Kickoff <script>alert(1)</script>");
    await textbox.press("Enter");

    // Should advance to the availability step — special chars are valid title content
    await expect(page.getByText(/when are you free/i)).toBeVisible();
  });

  test("very long title (1000 chars) is accepted by the UI", async ({
    page,
  }) => {
    const textbox = page.getByRole("textbox").first();

    // The frontend has no max-length constraint; the backend truncates at 300.
    // The UI should accept the input and advance without crashing.
    await textbox.fill("A".repeat(1000));
    await textbox.press("Enter");

    // Should advance to the availability step
    await expect(page.getByText(/when are you free/i)).toBeVisible();
  });

  test("NAV-021: no back button on the title step", async ({ page }) => {
    const backButton = page.getByRole("button", { name: /back/i });
    await expect(backButton).toHaveCount(0);
  });

  test("NAV-020: progress bar is visible on the title step", async ({
    page,
  }) => {
    // There should be some visual progress indicator
    const progress = page.locator(
      '[role="progressbar"], [class*="progress"], [style*="width"]',
    );
    const count = await progress.count();
    expect(count).toBeGreaterThan(0);
  });
});
