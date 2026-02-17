# EARS Booking Spec — E2E Test Observations

Findings from implementing black-box e2e API tests against the running server,
using only `docs/ears-booking.md` and `docs/nl-spec/` as input.

---

## Resolved

### 1. CSRF error message text (CSR-010, CSR-011)

**Spec said:** "Invalid CSRF token."
**Server returns:** `{"error":"Forbidden."}`

**Resolution:** Updated spec to match server. The vaguer "Forbidden." message
is more secure — it doesn't reveal the protection mechanism to an attacker.

### 2. Offset datetime format in slot responses (SLT-005, PRS-002)

**Spec said:** "ISO-8601 with offset" (ambiguous about shortened form)
**Server returned:** `-08` instead of `-08:00`

**Resolution:** Fixed server (`Handlers.fs`, `AdminHandlers.fs`) to use a
custom NodaTime format pattern that always emits full `±HH:MM` offsets.
Updated spec to explicitly require `±HH:MM` format.

### 3. Whitespace-only input handling (PRS-010, PRS-011)

**Spec said:** "empty message" / "empty or missing timezone"
**Server rejects:** whitespace-only values too

**Resolution:** Updated PRS-010 and PRS-011 to say "empty or whitespace-only",
consistent with BKC-010, BKC-012, TTL-010, and AVL-010.

### 4. Undocumented fields in parse response

- `description` field in `parseResult` — AI-generated context about parsing
- Per-window `timezone` field in availability windows
- `ok` field in CSRF token response
- `previousMessages` request field behavior

**Resolution:** Updated EARS spec (PRS-001, PRS-002, PRS-003, CSR-001) and
NL API spec (`michael-api-spec.md`) to document all fields.

### 5. Slot alignment after gaps (SLT-004)

**Resolution:** Clarified SLT-004 to note that slots start from the beginning
of each available interval, and gaps from bookings/events shift subsequent
start times.

---

## Untestable Requirements (from outside the API)

These EARS requirements can't be verified via black-box HTTP tests:

| ID | Reason |
|----|--------|
| BKC-003 | `BEGIN IMMEDIATE` transaction — requires code inspection |
| BKC-004 | Logging of booking ID and email — requires log access |
| BKC-017 | Database write failure — can't trigger externally |
| PRS-013 | AI model failure — can't reliably trigger (tested opportunistically when LLM is down) |
| SLT-003 | Calendar event exclusion — requires calendar source setup and cached events |
| CSR-020 | Constant-time comparison — requires code inspection |
| CSR-022 | 64-char hex validation in frontend — requires Elm code inspection |
| HAV-002 | Default seed data — requires fresh database |
| HAV-010 | DST transition handling — requires testing across DST boundary dates |

---

## Test Coverage Summary

| Test File | Requirements Covered | Tests |
|-----------|---------------------|-------|
| `csrf.test.ts` | CSR-001, CSR-002, CSR-010, CSR-011, CSR-021 | 14 |
| `parse.test.ts` | PRS-001–004, PRS-010–013 | 11 |
| `slots.test.ts` | SLT-001, SLT-004–007, SLT-010–013, VAL-001–002, VAL-004 | 20 |
| `book.test.ts` | BKC-001–002, BKC-010–016, SLT-002, VAL-001–004 | 26 |
| **Total** | | **71** |
