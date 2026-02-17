import { describe, test, expect, beforeAll } from "vitest";
import { getBaseURL, fetchCsrfToken, CsrfContext } from "../helpers/api-client";

// ---------------------------------------------------------------------------
// CSRF Token Endpoint & Protection
//
// Covers EARS requirements: CSR-001, CSR-010, CSR-011, CSR-021
// ---------------------------------------------------------------------------

describe("CSRF token endpoint", () => {
  test("CSR-001: GET /api/csrf-token returns a token and sets cookie", async () => {
    const resp = await fetch(`${getBaseURL()}/api/csrf-token`);
    expect(resp.ok).toBe(true);

    const body = (await resp.json()) as { token: string; ok: boolean };
    expect(body).toHaveProperty("token");
    expect(typeof body.token).toBe("string");
    expect(body.token.length).toBeGreaterThan(0);
    expect(body.ok).toBe(true);

    const setCookie = resp.headers.get("set-cookie") ?? "";
    expect(setCookie).toContain("michael_csrf=");
  });

  test("CSR-021: cookie has SameSite=Strict", async () => {
    const resp = await fetch(`${getBaseURL()}/api/csrf-token`);
    const setCookie = resp.headers.get("set-cookie") ?? "";
    expect(setCookie.toLowerCase()).toContain("samesite=strict");
  });

  test("CSR-021: cookie does not have HttpOnly (frontend needs to read it)", async () => {
    const resp = await fetch(`${getBaseURL()}/api/csrf-token`);
    const setCookie = resp.headers.get("set-cookie") ?? "";
    // HttpOnly=false means the httponly attribute should NOT be present
    expect(setCookie.toLowerCase()).not.toContain("httponly");
  });

  test("CSR-001: token has sufficient length", async () => {
    const resp = await fetch(`${getBaseURL()}/api/csrf-token`);
    const body = (await resp.json()) as { token: string };

    // Assert minimum length rather than exact structure — the meaningful
    // property is that the token carries enough entropy, not its format.
    expect(body.token.length).toBeGreaterThanOrEqual(32);
  });

  test("fetchCsrfToken helper returns token and cookie", async () => {
    const csrf = await fetchCsrfToken();
    expect(csrf.token.length).toBeGreaterThan(0);
    expect(csrf.cookieHeader).toContain("michael_csrf=");
  });

  test("CSR-001: successive requests return different tokens", async () => {
    const csrf1 = await fetchCsrfToken();
    const csrf2 = await fetchCsrfToken();

    // Tokens should be unique (contain different timestamps or nonces)
    expect(csrf1.token).not.toBe(csrf2.token);
  });
});

describe("CSRF protection on POST endpoints", () => {
  let csrf: CsrfContext;
  const endpoints = ["/api/parse", "/api/slots", "/api/book"];

  beforeAll(async () => {
    csrf = await fetchCsrfToken();
  });

  for (const endpoint of endpoints) {
    test(`CSR-010: ${endpoint} rejects request without X-CSRF-Token header`, async () => {
      // Send valid cookie but omit the header
      const resp = await fetch(`${getBaseURL()}${endpoint}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Cookie: csrf.cookieHeader,
        },
        body: JSON.stringify({}),
      });

      expect(resp.status).toBe(403);
      const body = (await resp.json()) as { error: string };
      expect(body.error).toBe("Forbidden.");
    });

    test(`CSR-011: ${endpoint} rejects mismatched CSRF token`, async () => {
      const resp = await fetch(`${getBaseURL()}${endpoint}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-Token": "wrong-token-value",
          Cookie: csrf.cookieHeader,
        },
        body: JSON.stringify({}),
      });

      expect(resp.status).toBe(403);
      const body = (await resp.json()) as { error: string };
      expect(body.error).toBe("Forbidden.");
    });
  }

  test("CSR-010: POST without any CSRF context returns 403", async () => {
    const resp = await fetch(`${getBaseURL()}/api/parse`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message: "test", timezone: "UTC" }),
    });

    expect(resp.status).toBe(403);
  });

  test("CSR-002: POST with matching header and cookie succeeds (past CSRF check)", async () => {
    // A valid CSRF context should get past CSRF and hit endpoint validation
    const resp = await fetch(`${getBaseURL()}/api/parse`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-Token": csrf.token,
        Cookie: csrf.cookieHeader,
      },
      body: JSON.stringify({ message: "", timezone: "UTC" }),
    });

    // Should get 400 (validation error), NOT 403 (CSRF rejection)
    expect(resp.status).toBe(400);
  });
});
