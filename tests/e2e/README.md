# E2E Tests

Black-box end-to-end tests for the Michael booking flow.

- **API tests** use [Vitest](https://vitest.dev/) with native `fetch` — fast,
  no browser required.
- **Browser tests** use [Playwright](https://playwright.dev/) for UI
  automation.

Tests run against a live server instance — no mocks, no in-process hosting.

## Prerequisites

The Nix dev shell provides Node.js, Playwright, and Chromium. Enter the shell
via `direnv` (automatic) or `nix develop`, then install npm dependencies:

```sh
cd tests/e2e
npm install
```

## Running

Start the dev server in one terminal, run tests in another:

```sh
# Terminal 1
make dev

# Terminal 2 — all tests (API + browser)
make e2e

# Only safe (non-destructive) tests
make e2e-safe

# Only API tests (vitest)
make e2e-api

# Only browser booking-flow tests (Playwright)
make e2e-booking
```

## Configuration

| Variable | Description | Default |
|---|---|---|
| `MICHAEL_TEST_URL` | Base URL of the running server | `http://localhost:8000` |
| `MICHAEL_TEST_ADMIN_PASSWORD` | Admin password for admin API calls | — |
| `MICHAEL_TEST_MODE` | `safe` (skip destructive) or `all` | `all` |

## Test Tags

- **safe** — read-only, triggers validation errors, exercises frontend
  behavior. No persistent state created.
- **destructive** — creates real bookings or modifies server state. Skipped
  when `MICHAEL_TEST_MODE=safe`.

## Structure

```
api/                  API tests (vitest, *.test.ts)
booking-flow/         Browser tests (Playwright, *.spec.ts)
helpers/
  api-client.ts       Shared fetch helpers: CSRF tokens, admin login
  fixtures.ts         Playwright fixtures (browser tests)
  tags.ts             Safe/destructive tagging (Playwright)
```
