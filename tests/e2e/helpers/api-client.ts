// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

export function getBaseURL(): string {
  return process.env.MICHAEL_TEST_URL ?? "http://localhost:8000";
}

export function getAdminPassword(): string {
  const pw = process.env.MICHAEL_TEST_ADMIN_PASSWORD;
  if (!pw) {
    throw new Error(
      "MICHAEL_TEST_ADMIN_PASSWORD is required but not set. " +
        "Set it to the admin password of the target server.",
    );
  }
  return pw;
}

// ---------------------------------------------------------------------------
// Test mode
// ---------------------------------------------------------------------------

type TestMode = "all" | "safe";

export function getTestMode(): TestMode {
  const mode = process.env.MICHAEL_TEST_MODE ?? "all";
  if (mode !== "all" && mode !== "safe") {
    throw new Error(
      `Invalid MICHAEL_TEST_MODE: "${mode}". Must be "all" or "safe".`,
    );
  }
  return mode;
}

export function isDestructiveSkipped(): boolean {
  return getTestMode() === "safe";
}

// ---------------------------------------------------------------------------
// CSRF helper
// ---------------------------------------------------------------------------

export interface CsrfContext {
  /** The CSRF token value for the X-CSRF-Token header. */
  token: string;
  /** The raw Set-Cookie header for forwarding in subsequent requests. */
  cookieHeader: string;
}

/**
 * Fetch a CSRF token from the server. Returns the token and the cookie header
 * needed for authenticated POST requests.
 */
export async function fetchCsrfToken(): Promise<CsrfContext> {
  const resp = await fetch(`${getBaseURL()}/api/csrf-token`);
  if (!resp.ok) {
    throw new Error(`CSRF token request failed: ${resp.status}`);
  }
  const body = (await resp.json()) as { token: string };
  const setCookie = resp.headers.get("set-cookie") ?? "";

  // Extract just the cookie key=value pair for forwarding
  const match = setCookie.match(/(michael_csrf=[^;]+)/);
  const cookieHeader = match ? match[1] : "";

  return { token: body.token, cookieHeader };
}

/**
 * Make a POST request with CSRF token and cookie.
 */
export async function postWithCsrf(
  path: string,
  body: unknown,
  csrf: CsrfContext,
): Promise<Response> {
  return fetch(`${getBaseURL()}${path}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-Token": csrf.token,
      Cookie: csrf.cookieHeader,
    },
    body: JSON.stringify(body),
  });
}

// ---------------------------------------------------------------------------
// Admin session helper
// ---------------------------------------------------------------------------

/**
 * Log in as admin. Returns the session cookie header for subsequent requests.
 * Requires MICHAEL_TEST_ADMIN_PASSWORD to be set.
 */
export async function adminLogin(): Promise<string> {
  const password = getAdminPassword();
  const resp = await fetch(`${getBaseURL()}/api/admin/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ password }),
  });
  if (!resp.ok) {
    throw new Error(`Admin login failed: ${resp.status}`);
  }
  const setCookie = resp.headers.get("set-cookie") ?? "";
  return setCookie;
}
