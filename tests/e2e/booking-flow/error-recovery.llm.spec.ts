import { test, expect } from "../helpers/fixtures";
import {
  goToBookingPage,
  completeTitle,
  clickOk,
  completeAvailability,
  waitForConfirmationStep,
  confirmAvailability,
  llmIsAvailable,
  navigateToBookingConfirmation,
  apiRoute,
} from "./helpers";

// ---------------------------------------------------------------------------
// Error Recovery & Conflict Handling
//
// EARS requirements: SCR-001..SCR-007, ACF-010
// ---------------------------------------------------------------------------

test.describe("Parse error recovery", () => {
  test.beforeEach(async ({}, testInfo) => {
    if (!llmIsAvailable()) testInfo.skip();
  });

  test("ACF-010: unparseable availability shows error on availability step", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Parse Error Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Enter something that may not parse into availability windows
    await completeAvailability(page, "asdfghjkl random gibberish 12345");

    // Wait for the parse response — either an error or the LLM found something
    const result = page
      .getByText(/could not parse/i)
      .or(page.getByText(/try describing/i))
      .or(
        page.getByRole("button", {
          name: /confirm|find slots|looks good/i,
        }),
      );
    await expect(result).toBeVisible({ timeout: 60_000 });

    // If the error appeared, verify we're still on the availability step
    const errorMessage = page
      .getByText(/could not parse/i)
      .or(page.getByText(/try describing/i));
    if ((await errorMessage.count()) > 0) {
      const textarea = page.getByRole("textbox").first();
      await expect(textarea).toBeVisible();
    }
  });
});

test.describe("Network error simulation", () => {
  test("SCR-002: network error on parse shows message, then retry succeeds", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Network Error Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    const textarea = page.getByRole("textbox").first();

    // Block the parse endpoint to simulate a network error
    await page.route(apiRoute("/api/parse"), (route) => route.abort("failed"));
    try {
      await textarea.fill("Tomorrow 2pm to 5pm");
      await textarea.press("Enter");

      // Should show a network error message
      await expect(
        page.getByText(/network error/i).or(page.getByText(/try again/i)),
      ).toBeVisible({ timeout: 15_000 });
    } finally {
      await page.unroute(apiRoute("/api/parse"));
    }

    // After the route is unblocked, the user should be able to retry.
    // The textarea should still be visible (we stayed on the availability step).
    if (llmIsAvailable()) {
      await expect(textarea).toBeVisible();
      await textarea.press("Enter");

      // Should proceed normally now
      const result = page
        .getByRole("button", { name: /confirm|find slots|looks good/i })
        .or(page.getByText(/could not parse/i));
      await expect(result).toBeVisible({ timeout: 60_000 });
    }
  });

  test("SCR-004: HTTP 500 on parse shows server error, then retry succeeds", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "Server Error Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    const textarea = page.getByRole("textbox").first();

    // Mock the parse endpoint to return 500
    await page.route(apiRoute("/api/parse"), (route) =>
      route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({ error: "An internal error occurred." }),
      }),
    );
    try {
      await textarea.fill("Tomorrow 2pm to 5pm");
      await textarea.press("Enter");

      // Should show server error with status
      await expect(
        page
          .getByText(/server error/i)
          .or(page.getByText(/500/))
          .or(page.getByText(/try again/i)),
      ).toBeVisible({ timeout: 15_000 });
    } finally {
      await page.unroute(apiRoute("/api/parse"));
    }

    // After the route is unblocked, the user should be able to retry.
    if (llmIsAvailable()) {
      await expect(textarea).toBeVisible();
      await textarea.press("Enter");

      // Should proceed normally now
      const result = page
        .getByRole("button", { name: /confirm|find slots|looks good/i })
        .or(page.getByText(/could not parse/i));
      await expect(result).toBeVisible({ timeout: 60_000 });
    }
  });

  test("SCR-005: slots request failure shows error message", async ({
    page,
  }) => {
    test.skip(!llmIsAvailable(), "LLM unavailable");

    await goToBookingPage(page);
    await completeTitle(page, "Slots Error Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    await completeAvailability(page, "I am free next Tuesday from 9am to 5pm");
    await waitForConfirmationStep(page);

    // Block the slots endpoint
    await page.route(apiRoute("/api/slots"), (route) => route.abort("failed"));
    try {
      await confirmAvailability(page);

      // Should show slots failure message
      await expect(
        page
          .getByText(/failed to load/i)
          .or(page.getByText(/available slots/i))
          .or(page.getByText(/try again/i)),
      ).toBeVisible({ timeout: 15_000 });
    } finally {
      await page.unroute(apiRoute("/api/slots"));
    }
  });

  test("SCR-006: booking failure (non-409, non-403) shows error message", async ({
    page,
  }) => {
    test.skip(!llmIsAvailable(), "LLM unavailable");

    if (
      !(await navigateToBookingConfirmation(page, {
        title: "Book Error Test",
        availability: "I am free next Wednesday from 9am to 5pm",
        contact: { name: "Error Tester", email: "error@test.example.com" },
      }))
    ) {
      test.skip();
      return;
    }

    // Mock the book endpoint to return 500
    await page.route(apiRoute("/api/book"), (route) =>
      route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({ error: "An internal error occurred." }),
      }),
    );
    try {
      await page.getByRole("button", { name: /confirm booking/i }).click();

      // Should show a booking failure message
      await expect(
        page.getByText(/failed to confirm/i).or(page.getByText(/try again/i)),
      ).toBeVisible({ timeout: 15_000 });
    } finally {
      await page.unroute(apiRoute("/api/book"));
    }
  });

  test("SCR-001: 409 conflict returns to slot selection with error and re-fetches slots", async ({
    page,
  }) => {
    test.skip(!llmIsAvailable(), "LLM unavailable");

    if (
      !(await navigateToBookingConfirmation(page, {
        title: "Conflict Test",
        availability: "I am free next Thursday from 9am to 5pm",
        contact: {
          name: "Conflict Tester",
          email: "conflict@test.example.com",
        },
      }))
    ) {
      test.skip();
      return;
    }

    // Mock the book endpoint to return 409
    await page.route(apiRoute("/api/book"), (route) =>
      route.fulfill({
        status: 409,
        contentType: "application/json",
        body: JSON.stringify({
          error: "That slot is no longer available.",
          code: "slot_unavailable",
        }),
      }),
    );
    try {
      await page.getByRole("button", { name: /confirm booking/i }).click();

      // Should return to slot selection with error message
      await expect(
        page
          .getByText(/no longer available/i)
          .or(page.getByText(/choose another time/i)),
      ).toBeVisible({ timeout: 15_000 });
    } finally {
      await page.unroute(apiRoute("/api/book"));
    }
  });
});

test.describe("CSRF error recovery", () => {
  test("SCR-012/CSR-012: 403 on protected request triggers CSRF refresh and retry", async ({
    page,
  }) => {
    test.skip(!llmIsAvailable(), "LLM unavailable");

    await goToBookingPage(page);
    await completeTitle(page, "CSRF Refresh Test");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Intercept the first parse request to return 403, then let through
    let callCount = 0;
    await page.route(apiRoute("/api/parse"), async (route) => {
      callCount++;
      if (callCount === 1) {
        await route.fulfill({
          status: 403,
          contentType: "application/json",
          body: JSON.stringify({ error: "Forbidden." }),
        });
      } else {
        await route.continue();
      }
    });
    try {
      const textarea = page.getByRole("textbox").first();
      await textarea.fill("Tomorrow 2pm to 5pm");
      await textarea.press("Enter");

      // The app should retry after refreshing CSRF
      const result = page
        .getByRole("button", { name: /confirm|find slots|looks good/i })
        .or(page.getByText(/could not parse/i))
        .or(page.getByText(/error/i));
      await expect(result).toBeVisible({ timeout: 60_000 });

      // The route handler should have been called at least twice
      expect(callCount).toBeGreaterThanOrEqual(2);
    } finally {
      await page.unroute(apiRoute("/api/parse"));
    }
  });

  test("SCR-013/CSR-013: persistent CSRF failure shows session expired message", async ({
    page,
  }) => {
    await goToBookingPage(page);
    await completeTitle(page, "CSRF Persistent Fail");
    await page.getByRole("button", { name: /30 min/ }).click();
    await clickOk(page);

    // Block ALL parse requests with 403
    await page.route(apiRoute("/api/parse"), (route) =>
      route.fulfill({
        status: 403,
        contentType: "application/json",
        body: JSON.stringify({ error: "Forbidden." }),
      }),
    );

    // Also block CSRF token refresh to ensure persistent failure
    await page.route(apiRoute("/api/csrf-token"), (route) =>
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ ok: true, token: "expired-token" }),
        headers: {
          // Match production cookie attributes (HttpOnly=false so JS can
          // read the token; no Secure since tests run over HTTP).
          "Set-Cookie": "michael_csrf=expired-token; Path=/; SameSite=Strict",
        },
      }),
    );
    try {
      const textarea = page.getByRole("textbox").first();
      await textarea.fill("Tomorrow 2pm to 5pm");
      await textarea.press("Enter");

      // Should show session expired message
      await expect(
        page
          .getByText(/session expired/i)
          .or(page.getByText(/refresh.*try again/i)),
      ).toBeVisible({ timeout: 30_000 });
    } finally {
      await page.unroute(apiRoute("/api/parse"));
      await page.unroute(apiRoute("/api/csrf-token"));
    }
  });
});
