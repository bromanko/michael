---
id: mwbp-8b9d
status: closed
deps: []
links: []
created: 2026-01-30T19:49:31Z
type: feature
priority: 2
assignee: Brian Romanko
---
# Add comprehensive Elm frontend tests

Add Elm test suite for the booking frontend. Priority targets:

1. **ModelTest.elm** — test mergeResult: merging into empty accumulator, later values override earlier ones, Nothing values dont clobber existing values, availabilityWindows replaced when non-empty/kept when empty.

2. **ApiTest.elm** — test JSON decoders: parseResultDecoder with full payload and missing optional fields, timeSlotDecoder, bookingConfirmationDecoder, each decoder with malformed JSON. Test encode/decode roundtrips.

3. **UpdateTest.elm** — test each Msg branch and state machine transitions: SendMessage with empty input (no-op), SendMessage with content, GotParseResponse Ok with/without missing fields (phase transition), GotParseResponse Err, ConfirmParse, RejectParse, SelectSlot, ConfirmBooking with complete/missing data, error recovery.

4. **Fuzz tests** — mergeResult invariants, formatSlotTime never crashes on any input, JSON roundtrips.

Requires adding elm-explorations/test to elm.json test-dependencies.


## Notes

**2026-02-21T22:25:24Z**

Wrote execution plan: docs/plans/plan-booking-elm-tests.md
