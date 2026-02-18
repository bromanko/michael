# EARS Booking Spec — Black-Box Test Observations

Observations from running Playwright browser tests against the live booking
flow, written purely from the EARS spec without examining source code.

**Date:** 2026-02-17

---

## 1. Submit button text varies by step

The EARS spec uses generic phrasing like "submit" and "advance." The actual UI
uses step-specific button labels:

| Step | Button text (enabled) | Button text (disabled) |
|------|----------------------|----------------------|
| Title | OK | OK |
| Duration | OK press Enter | OK |
| Availability | Find slots press Enter | Finding slots... |
| Confirmation | Looks good press Enter | — |
| Contact info | OK press Enter | OK |
| Booking confirm | Confirm booking | Booking... |

The "press Enter" hint appears as secondary text inside enabled submit buttons,
indicating the keyboard shortcut. This is good UX — the spec simply didn't
prescribe specific labels for these buttons.

## 2. Time display omits `:00` for round hours

The spec example shows `"9:00 AM – 5:00 PM"` (DSP-002). The implementation
displays `"9 AM – 5 PM"` for round hours, only showing minutes when non-zero
(e.g., `"9:30 AM"`). Both are human-readable; the implementation is arguably
cleaner.

## 3. Empty slot view uses "Try different times" instead of "Back"

When no overlapping slots exist (SSE-020), the slot selection step shows a
"Try different times" button instead of the standard "Back" button present on
other steps. This is a reasonable UX choice — "Try different times" is more
descriptive than a generic back action in this context. The EARS spec (NAV-009)
says "the booking system shall return to the immediately preceding step" when
back is clicked, but does not strictly require the button be labeled "Back."

## 4. Timezone selector button shows current timezone, not "Timezone"

The timezone selector button displays as `"🌐 UTC ▾"` (or whichever timezone
is active). It does not contain the word "timezone" in its accessible name —
tests must match on the current timezone value. See ticket for improving the
accessible name.

## 5. Rate limiting affects LLM-dependent tests

Running many concurrent browser tests that hit the `/api/parse` endpoint
triggers 429 rate limits. Tests that depend on the LLM (confirmation step
onward) should either run serially or use longer timeouts. The frontend
correctly displays `"Server error (429)"` when this happens.
