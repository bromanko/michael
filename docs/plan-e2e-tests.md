# Black-Box E2E Test Plan

## Overview

Black-box tests for the booking flow, driven by the EARS requirements in
`docs/ears-booking.md`. Tests run against any live instance of the system —
local dev or production — via a configurable base URL. No mocks, no in-process
hosting, no test doubles.

**Source requirements:** `docs/ears-booking.md` (~110 requirements across 22
sections)

## Tooling

- **Vitest** for API tests — lightweight, fast, native `fetch`
- **Playwright** for browser tests — booking flow UI automation
- Single configurable `MICHAEL_TEST_URL` (defaults to `http://localhost:8000`)
- Admin credentials via env vars for reading host settings
- Tests tagged `safe` or `destructive`

## Configuration

| Variable | Description | Default |
|---|---|---|
| `MICHAEL_TEST_URL` | Base URL of the running server | `http://localhost:8000` |
| `MICHAEL_TEST_ADMIN_PASSWORD` | Admin password for reading settings via admin API | — |
| `MICHAEL_TEST_MODE` | `safe` (skip destructive tests) or `all` | `all` |

## Test Tagging

- **`safe`**: Only reads data, triggers validation errors, or exercises
  frontend behavior. Creates no persistent state.
- **`destructive`**: Creates real bookings or modifies server state.

`MICHAEL_TEST_MODE=safe` skips all destructive tests. Use this when running
against production to avoid cluttering the database.

## Project Structure

```
tests/e2e/
  package.json
  vitest.config.ts
  playwright.config.ts
  README.md
  api/
    csrf.test.ts          # CSR-001, 010, 011
    parse.test.ts         # PRS-001–004, 010–013
    slots.test.ts         # SLT-001–007, 010–013, SCH-001–002
    book.test.ts          # BKC-001–002, 010–017
    validation.test.ts    # VAL-001–004 (cross-endpoint)
  booking-flow/
    happy-path.spec.ts    # NAV-001–008, full flow end-to-end
    navigation.spec.ts    # NAV-009–011, 020–022, back buttons, progress bar
    title.spec.ts         # TTL-001–002, 010, 020
    duration.spec.ts      # DUR-001–005, 010–012, 020
    availability.spec.ts  # AVL-001–004, 010, 020
    confirmation.spec.ts  # ACF-001–003, 010
    slots.spec.ts         # SSE-001–003, 010, 020
    contact.spec.ts       # CTI-001–002, 010–013
    booking.spec.ts       # BCF-001–004, 010, CMP-001–002
    timezone.spec.ts      # TZ-001–005, 010–011, 020
    errors.spec.ts        # SCR-001–007, ERR-001, 010–011, CSR-012–013
    accessibility.spec.ts # A11-001, 010–012
  helpers/
    api-client.ts         # shared fetch helpers: CSRF tokens, admin login
    tags.ts               # safe/destructive tagging utilities (Playwright)
    fixtures.ts           # Playwright fixtures (browser tests)
```

## Running Tests

Tests assume a server is already running. Start the dev server in one terminal,
run tests in another:

```sh
# Terminal 1: start the server
make dev

# Terminal 2: run all tests against localhost
make e2e

# Run only safe (non-destructive) tests
make e2e-safe

# Run against production
MICHAEL_TEST_URL=https://michael.example.com make e2e-safe

# Run only API tests
make e2e-api

# Run only browser tests
make e2e-booking
```

### Makefile Targets

| Target | Description |
|---|---|
| `make e2e` | Run all E2E tests (safe + destructive) |
| `make e2e-safe` | Run only safe tests (no bookings created) |
| `make e2e-api` | Run only API spec files (vitest) |
| `make e2e-booking` | Run only browser booking-flow spec files (Playwright) |

## Test Approach by Section

### API Tests (Vitest)

| Section | Spec file | Tag | Notes |
|---|---|---|---|
| CSRF (§14) | `csrf.test.ts` | safe | Fetch token, verify cookie; POST without/mismatched header → 403 |
| Parse (§5) | `parse.test.ts` | safe | Structural assertions on response shape, ISO-8601 format, field presence. Validation errors (empty msg, bad tz) are exact match. No exact AI output assertions — see below. |
| Slots (§7) | `slots.test.ts` | safe | Post valid windows, assert slot structure. Read host settings via admin API to construct meaningful windows. Verify scheduling constraints relative to queried settings. |
| Book (§11) | `book.test.ts` | mixed | Validation errors (400s) are safe. Successful booking + conflict (409) are destructive. |
| Validation (§18) | `validation.test.ts` | safe | Cross-endpoint: malformed datetimes, invalid timezones, duration boundaries. |
| Host availability (§16) | Covered in `slots.test.ts` | safe | Query via admin API, verify defaults on fresh DB. |
| Scheduling settings (§17) | Covered in `slots.test.ts` | safe | Verified through slots endpoint behavior relative to queried settings. |

### Browser Tests (Playwright)

| Section | Spec file | Tag | Notes |
|---|---|---|---|
| Navigation (§1) | `navigation.spec.ts` | safe | Back buttons, progress bar, step ordering. Stops before final confirm. |
| Title (§2) | `title.spec.ts` | safe | Focus, empty validation, Enter key. |
| Duration (§3) | `duration.spec.ts` | safe | Preset/custom selection, validation errors. |
| Availability (§4) | `availability.spec.ts` | safe | Focus, Enter/Shift+Enter, empty validation, loading state. |
| Confirmation (§6) | `confirmation.spec.ts` | safe | Parsed windows display, back preserves text. |
| Slot selection (§8) | `slots.spec.ts` | safe | Slot rendering, focus, scrollable container, empty state. |
| Contact info (§9) | `contact.spec.ts` | safe | Focus, validation errors, optional phone. |
| Booking confirm (§10) | `booking.spec.ts` | mixed | Summary display = safe. Actual confirm click = destructive. |
| Completion (§12) | `booking.spec.ts` | destructive | Success display after real booking. |
| Timezone (§13) | `timezone.spec.ts` | safe | Selector appears, dropdown opens, timezone change triggers re-fetch. |
| Slot conflict (§15) | `errors.spec.ts` | destructive | Requires real double-booking to trigger 409. |
| Error display (§22) | `errors.spec.ts` | safe | Validation errors trigger banner; banner clears on valid action. |
| Error recovery (§15) | `errors.spec.ts` | mixed | Network/timeout errors = safe (via route interception). CSRF retry = safe. 409 recovery = destructive. |
| Accessibility (§19) | `accessibility.spec.ts` | safe | Focus management, Enter submission, semantic elements. |
| Display (§20) | Covered in `confirmation.spec.ts`, `slots.spec.ts` | safe | Date/time formatting checked as part of step views. |
| Agent accessibility (§21) | `accessibility.spec.ts` | safe | Assert `id` attributes, `label[for]`, `form` elements. |

## AI Parse Testing Strategy

The `/api/parse` endpoint calls a real AI model (Gemini). Tests cannot assert
exact parsed values. The approach:

- **Structural assertions** (deterministic, always run): response contains
  `parseResult` and `systemMessage`; `parseResult` has the correct field schema
  (`availabilityWindows`, `durationMinutes`, `title`, `name`, `email`, `phone`,
  `missingFields`); availability windows have valid ISO-8601 timestamps with
  UTC offset.
- **Validation error assertions** (deterministic): empty message → 400, empty
  timezone → 400, invalid timezone → 400. These are exact-match.
- **No exact extraction assertions**: we do not assert that a specific input
  produces specific extracted field values, since the AI model is
  non-deterministic.

The EARS requirements (PRS-003, PRS-004) are correctly specified as conditional
("when the AI model extracts..."), so the requirements themselves hold. The test
suite verifies the system honors the contract structurally.

## Approximate Test Counts

| Category | Safe | Destructive | Total |
|---|---|---|---|
| API tests | ~35 | ~6 | ~41 |
| Browser tests | ~60 | ~8 | ~68 |
| **Total** | **~95** | **~14** | **~109** |

## Nix Additions

- Add Playwright and Chromium to the flake's dev shell
- Pin Playwright browser version via Nix to avoid download-on-first-run

## CI

These tests are **not** part of `selfci check`. They are too slow (browser
spin-up, real AI calls, network round-trips) for the fast-feedback CI loop.

Run them manually via `make e2e` or `make e2e-safe` before deploys.

## Implementation Order

1. **Scaffold**: `tests/e2e/` with vitest + Playwright config, helpers,
   `package.json`
2. **Nix**: add Playwright + Chromium to the dev shell
3. **Makefile**: add `e2e`, `e2e-safe`, `e2e-api`, `e2e-booking` targets
4. **API tests**: csrf → parse → slots → book → validation (vitest)
5. **E2E happy path**: single test driving the full booking flow (Playwright)
6. **E2E step-by-step**: title → duration → availability → confirmation →
   slots → contact → booking → completion
7. **E2E edge cases**: timezone, errors, accessibility, navigation
