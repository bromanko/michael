import { describe, test, expect } from "vitest";
import { getBaseURL, fetchCsrfToken } from "../helpers/api-client";

describe("CSRF token endpoint", () => {
  test("GET /api/csrf-token returns a token and sets a cookie", async () => {
    const resp = await fetch(`${getBaseURL()}/api/csrf-token`);
    expect(resp.ok).toBe(true);

    const body = (await resp.json()) as { token: string };
    expect(body).toHaveProperty("token");
    expect(typeof body.token).toBe("string");
    expect(body.token.length).toBeGreaterThan(0);

    const setCookie = resp.headers.get("set-cookie") ?? "";
    expect(setCookie).toContain("michael_csrf=");
  });

  test("fetchCsrfToken helper returns token and cookie", async () => {
    const csrf = await fetchCsrfToken();
    expect(csrf.token.length).toBeGreaterThan(0);
    expect(csrf.cookieHeader).toContain("michael_csrf=");
  });
});
