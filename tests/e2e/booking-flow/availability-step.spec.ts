import { test, expect } from "../helpers/fixtures";
import { goToBookingPage, completeTitle, clickOk, apiRoute } from "./helpers";

// ---------------------------------------------------------------------------
// Availability Input Step
//
// EARS requirements: NAV-003, NAV-004, AVL-001..AVL-004, AVL-010, AVL-020,
//                    A11-010
// ---------------------------------------------------------------------------

test.describe("Availability step", () => {
  test.beforeEach(async ({ page }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Test Meeting");
    // Wait for the availability step
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeVisible();
  });

  test("AVL-001: availability text area is focused", async ({ page }) => {
    const textarea = page.getByRole("textbox").first();
    await expect(textarea).toBeFocused();
  });

  test("AVL-010: submitting empty text shows error", async ({ page }) => {
    const textarea = page.getByRole("textbox").first();
    await textarea.fill("");
    await textarea.press("Enter");

    await expect(
      page.getByText("Please describe your availability"),
    ).toBeVisible();
  });

  test("AVL-010: submitting whitespace-only text shows error", async ({
    page,
  }) => {
    const textarea = page.getByRole("textbox").first();
    await textarea.fill("   ");
    await textarea.press("Enter");

    await expect(
      page.getByText("Please describe your availability"),
    ).toBeVisible();
  });

  test("AVL-010: textarea remains functional after validation error", async ({
    page,
  }) => {
    const textarea = page.getByRole("textbox").first();

    // Enter text, then clear and submit empty to trigger an error
    await textarea.fill("Some partial text");
    await textarea.fill("");
    await textarea.press("Enter");
    await expect(
      page.getByText("Please describe your availability"),
    ).toBeVisible();

    // Textarea should still be on the same step and accept new input
    await textarea.fill("Monday 9am to 5pm");
    await expect(textarea).toHaveValue("Monday 9am to 5pm");
  });

  test("AVL-004: Shift+Enter inserts newline without submitting", async ({
    page,
  }) => {
    const textarea = page.getByRole("textbox").first();
    await textarea.fill("Monday 2pm");
    await textarea.press("Shift+Enter");
    await textarea.type("Tuesday 3pm");

    // The textarea should contain both lines (newline preserved)
    const value = await textarea.inputValue();
    expect(value).toContain("Monday 2pm");
    expect(value).toContain("Tuesday 3pm");
    expect(value).toContain("\n");

    // Should still be on the availability step (not advanced)
    await expect(textarea).toBeVisible();
  });

  test("AVL-003: Enter submits the availability input", async ({ page }) => {
    const textarea = page.getByRole("textbox").first();
    await textarea.fill("Tomorrow 2pm to 5pm");
    await textarea.press("Enter");

    // Either the loading state appears (AVL-020) or we advance to
    // confirmation. Both indicate submission happened.
    const loadingOrNext = page
      .getByText(/finding slots|loading|parsing/i)
      .or(page.getByRole("button", { name: /confirm|find slots|looks good/i }))
      .or(page.getByText(/could not parse/i))
      .or(page.getByText(/network error|server error|timed out/i));

    await expect(loadingOrNext).toBeVisible({ timeout: 60_000 });
  });

  test("AVL-020: shows loading state while parse request is in progress", async ({
    page,
  }) => {
    // Delay the parse response so the loading state is reliably visible
    await page.route(apiRoute("/api/parse"), async (route) => {
      await new Promise((r) => setTimeout(r, 500));
      await route.continue();
    });
    try {
      const textarea = page.getByRole("textbox").first();
      await textarea.fill("Tomorrow 2pm to 5pm");
      await textarea.press("Enter");

      // The submit button should show "Finding slots..." while the request is pending
      await expect(
        page.getByRole("button", { name: /finding slots/i }),
      ).toBeVisible({ timeout: 5_000 });
    } finally {
      await page.unroute(apiRoute("/api/parse"));
    }
  });

  test("NAV-009: back button returns to title step", async ({ page }) => {
    await page.getByRole("button", { name: /back/i }).click();

    // Should see the title input with preserved value
    const textbox = page.getByRole("textbox").first();
    await expect(textbox).toBeVisible();
    await expect(textbox).toHaveValue("Test Meeting");
  });
});
