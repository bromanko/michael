// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

/** Per-request timeout in milliseconds. Prevents individual HTTP calls from
 *  hanging indefinitely (e.g. during concurrent slot probes). */
const REQUEST_TIMEOUT_MS = 10_000;

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
  const resp = await fetch(`${getBaseURL()}/api/csrf-token`, {
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  if (!resp.ok) {
    throw new Error(`CSRF token request failed: ${resp.status}`);
  }
  const body = (await resp.json()) as { token: string };
  const setCookie = resp.headers.get("set-cookie") ?? "";

  // Extract just the cookie key=value pair for forwarding.
  // Validate the token value matches expected base64/alphanumeric format.
  const match = setCookie.match(/michael_csrf=([A-Za-z0-9_\-+/=]+)/);
  const cookieHeader = match ? `michael_csrf=${match[1]}` : "";

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
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
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
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  });
  if (!resp.ok) {
    throw new Error(`Admin login failed: ${resp.status}`);
  }
  const setCookie = resp.headers.get("set-cookie") ?? "";
  return setCookie;
}

// ---------------------------------------------------------------------------
// Concurrency limiter
// ---------------------------------------------------------------------------

/** Default concurrency for bulk probe patterns (slot search loops). */
export const PROBE_CONCURRENCY = 4;

/**
 * Map over `items` with at most `limit` concurrent `fn` invocations.
 * Preserves result order. Use instead of `Promise.all(items.map(fn))` when
 * the item count is large enough to overwhelm the server.
 */
export async function mapWithConcurrency<T, R>(
  items: T[],
  limit: number,
  fn: (item: T) => Promise<R>,
): Promise<R[]> {
  const results: R[] = new Array(items.length);
  let index = 0;
  const workers = Array.from(
    { length: Math.min(limit, items.length) },
    async () => {
      while (index < items.length) {
        const i = index++;
        results[i] = await fn(items[i]);
      }
    },
  );
  await Promise.all(workers);
  return results;
}
