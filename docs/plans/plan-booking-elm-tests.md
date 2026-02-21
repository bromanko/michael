# Add comprehensive Elm frontend tests for the booking app

This ExecPlan is a living document. The sections Progress, Surprises & Discoveries,
Decision Log, and Outcomes & Retrospective must be kept up to date as work proceeds.


## Purpose / Big Picture

The booking frontend (`src/frontend/booking/`) has core behavior in model initialization,
JSON codecs, and a multi-step update state machine, but only `DateFormatTest.elm`
exists today. A change to parsing, CSRF retry logic, or step transitions can regress
behavior without an automated signal.

After this plan is complete, running `npx elm-test` from `src/frontend/booking/` will
execute a comprehensive suite covering model initialization and validation, JSON decoder
and encoder correctness, all meaningful `Msg` branches in `Update.update` (including
403 CSRF refresh and 409 slot-conflict recovery), and property-based invariant tests.
Developers will be able to change booking frontend logic and quickly detect regressions.


## Progress

- [ ] Milestone 1: `ModelTest.elm` — init defaults and CSRF token validation
- [ ] Milestone 2: `ApiCodecs.elm` extraction and `ApiTest.elm` codec tests
- [ ] Milestone 3: `UpdateTest.elm` — state-machine and error-path tests
- [ ] Milestone 4: `FuzzTest.elm` — property-based invariants
- [ ] Final CI pass: `selfci check` green


## Surprises & Discoveries

(None yet.)


## Decision Log

- Decision: Keep test files flat in `src/frontend/booking/tests/`.
  Rationale: Existing test discovery and current `DateFormatTest.elm` already use this
  layout, so no runner/config change is needed.
  Date: 2026-02-20

- Decision: Do not expose internal codec helpers directly from `Api.elm` for tests.
  Rationale: `elm-review` runs with `NoUnused.Exports`, and booking `elm.json` includes
  only `src/` as source directories. Exports used only from `tests/` can be flagged as
  unused. Extracting codecs into `ApiCodecs.elm` keeps `Api.elm` API focused while
  allowing direct codec testing from both production code and tests.
  Date: 2026-02-20

- Decision: In update tests, assert command presence (`Cmd.none` vs non-`Cmd.none`) and
  model state, but do not inspect command internals.
  Rationale: `Cmd` is opaque in Elm. Behavior is validated by state assertions and by
  dedicated codec tests for JSON correctness.
  Date: 2026-02-20

- Decision: Keep view testing out of this plan.
  Rationale: Current scope is model/codec/update/fuzz coverage. View tests are a
  separate concern and can be planned independently.
  Date: 2026-02-20


## Outcomes & Retrospective

(To be filled at major milestones and at completion.)


## Context and Orientation

Booking frontend code lives under `src/frontend/booking/`.

Key source modules:

- `src/frontend/booking/src/Types.elm` defines shared types (`FormStep`,
  `AvailabilityWindow`, `ParseResult`, `ParseResponse`, `TimeSlot`,
  `BookingConfirmation`).
- `src/frontend/booking/src/Model.elm` defines `Model`, `init`, and
  `validCsrfToken`.
- `src/frontend/booking/src/Api.elm` defines HTTP request functions and currently
  includes internal decoders/encoders.
- `src/frontend/booking/src/Update.elm` defines `Msg` and `update` state-machine
  logic.
- `src/frontend/booking/src/DateFormat.elm` formats date/time text.

Existing tests:

- `src/frontend/booking/tests/DateFormatTest.elm` (current only test file).

Tooling and constraints:

- `elm-test` is run in CI via `npx elm-test` from `src/frontend/booking/`.
- `elm-review` is mandatory in CI and uses `NoUnused.Exports`.
- Booking `elm.json` sets `"source-directories": ["src"]`, so tests do not count as
  source usage for `NoUnused.Exports`.

Because of that constraint, codec testability will be solved by extracting codecs to
`src/frontend/booking/src/ApiCodecs.elm`, then importing them in both `Api.elm` and
`ApiTest.elm`.


## Plan of Work

### Milestone 1: `ModelTest.elm`

Create `src/frontend/booking/tests/ModelTest.elm` with unit tests for:

- `Model.init` default model shape (step, empty fields, `loading`, `error`, `Cmd.none`).
- timezone validation behavior (`valid timezone` retained; invalid/empty/too-long -> `UTC`).
- CSRF token integration in `init` (`csrfToken = Just token` only for valid tokens).
- `Model.validCsrfToken` acceptance and rejection cases (part count, positive `issuedAt`,
  exact nonce/signature lengths, hex-only characters, uppercase-hex acceptance).

### Milestone 2: codec extraction + `ApiTest.elm`

Create new source module:

- `src/frontend/booking/src/ApiCodecs.elm`

Move codec definitions from `Api.elm` into `ApiCodecs.elm` and expose:

- `availabilityWindowDecoder`
- `bookingConfirmationDecoder`
- `csrfTokenDecoder`
- `encodeAvailabilityWindow`
- `parseResponseDecoder`
- `parseResultDecoder`
- `slotsResponseDecoder`
- `timeSlotDecoder`

Then update `src/frontend/booking/src/Api.elm`:

- Keep public API unchanged (`bookSlot`, `fetchCsrfToken`, `fetchSlots`, `parseMessage`).
- Import `ApiCodecs` decoders/encoders and use them in HTTP expectations/request bodies.

Create `src/frontend/booking/tests/ApiTest.elm` and test these codec behaviors directly
through `ApiCodecs`:

- `parseResultDecoder` full payload, optional fields missing, malformed payload failure.
- `parseResponseDecoder` required-field behavior.
- `availabilityWindowDecoder` timezone present/absent/null and missing required fields.
- `timeSlotDecoder` required fields and tolerance for extra JSON fields.
- `slotsResponseDecoder` normal list, empty list, missing-key failure.
- `bookingConfirmationDecoder` required-field behavior.
- `encodeAvailabilityWindow` round-trip with and without timezone.

### Milestone 3: `UpdateTest.elm`

Create `src/frontend/booking/tests/UpdateTest.elm` with focused tests for model
transitions and command/no-command expectations. Cover these message groups:

- field update messages (`TitleUpdated`, `AvailabilityTextUpdated`, `NameUpdated`,
  `EmailUpdated`, `PhoneUpdated`).
- step-completion validation (`TitleStepCompleted`, `AvailabilityStepCompleted`,
  `ContactInfoStepCompleted`) including whitespace and invalid email cases.
- parse success and failure paths (`ParseResponseReceived`) including 403-refresh logic,
  retry exhaustion, and network/timeout errors.
- slots fetch paths (`AvailabilityWindowsConfirmed`, `SlotsReceived`) including 403-refresh,
  retry exhaustion, and generic failure.
- slot selection and booking (`SlotSelected`, `BookingConfirmed`,
  `BookingResultReceived`) including 409 conflict behavior.
- CSRF refresh result messages (`CsrfTokenRefreshedForParse`,
  `CsrfTokenRefreshedForSlots`, `CsrfTokenRefreshedForBook`) for valid token,
  invalid token, and refresh error.
- timezone behavior (`TimezoneChanged`, `TimezoneDropdownToggled`) across wizard steps.
- back navigation (`BackStepClicked`) including state cleanup and boundary-step idempotence.
- `NoOp` invariants.

Add explicit no-CSRF tests where `withCsrfToken` is used:

- `AvailabilityWindowsConfirmed` with `csrfToken = Nothing`.
- `TimezoneChanged` on `AvailabilityConfirmStep` with missing token.
- `TimezoneChanged` on `SlotSelectionStep` with missing token.
- `BookingResultReceived (Err (Http.BadStatus 409))` with missing token.

### Milestone 4: `FuzzTest.elm`

Create `src/frontend/booking/tests/FuzzTest.elm` with property tests that carry strong
signal, not just “does not crash” checks.

- `validCsrfToken` acceptance invariant: generated valid-shape tokens
  (`positiveInt:32hex:64hex`) always return `Just originalToken`.
- `validCsrfToken` identity invariant: when result is `Just`, it always equals input.
- `encodeAvailabilityWindow` round-trip invariant with fuzzed strings and `Maybe String`.
- `Model.init` invariant: for all inputs, `currentStep = TitleStep`.
- `BackStepClicked` boundary invariant: applying it repeatedly from `TitleStep` and
  `CompleteStep` does not change those steps.
- `formatFriendlyTime` structured-input invariant: for generated ISO-like times,
  output always contains `AM` or `PM`, and includes `:` iff minutes are not `"00"`.


## Concrete Steps

Run all commands from repo root (`/home/bromanko.linux/Code/michael`) unless noted.

1. Create `src/frontend/booking/tests/ModelTest.elm`.
2. Create `src/frontend/booking/src/ApiCodecs.elm` and refactor `src/frontend/booking/src/Api.elm`.
3. Create `src/frontend/booking/tests/ApiTest.elm`.
4. Create `src/frontend/booking/tests/UpdateTest.elm`.
5. Create `src/frontend/booking/tests/FuzzTest.elm`.
6. Run booking frontend checks after each milestone:

    cd src/frontend/booking && elm make src/Main.elm --output=/dev/null
    cd src/frontend/booking && elm-review
    cd src/frontend/booking && npx elm-test

7. Run full repo CI before completion:

    selfci check


## Validation and Acceptance

Primary acceptance is behavior plus green CI.

From repo root:

    cd src/frontend/booking && npx elm-test

Expect all tests passing, including:

- `DateFormatTest`
- `ModelTest`
- `ApiTest`
- `UpdateTest`
- `FuzzTest`

Then run:

    selfci check

Expect all jobs green (`lint`, `build`, `frontend`, `test`), with `frontend` including
successful `elm-review booking` and `elm-test booking` steps.

Qualitative coverage sanity check (manual): temporarily break
`bookingConfirmationDecoder` field name in `ApiCodecs.elm`; `ApiTest` should fail.
Revert. Temporarily break `previousStep AvailabilityStep` behavior in `Update.elm`;
`UpdateTest` should fail. Revert.


## Idempotence and Recovery

All plan steps are safe to rerun. Re-running `elm make`, `elm-review`, `npx elm-test`,
and `selfci check` is idempotent. If a test file is partially implemented, continue
editing and rerun tests until green.

If formatting causes CI lint failures, run `treefmt` at repo root and rerun
`selfci check`.


## Artifacts and Notes

Follow existing test style in `src/frontend/booking/tests/DateFormatTest.elm`:

    module XxxTest exposing (suite)

    import Expect
    import Test exposing (Test, describe, test)

    suite : Test
    suite =
        describe "Xxx"
            [ test "..." <| \_ ->
                ... |> Expect.equal ...
            ]

Use a shared valid token in tests:

    validToken : String
    validToken =
        "1234567890:aabbccdd11223344aabbccdd11223344:aabbccdd11223344aabbccdd11223344aabbccdd11223344aabbccdd11223344"

Use a baseline model helper in `UpdateTest.elm`:

    initModel : Model
    initModel =
        let
            ( model, _ ) =
                Model.init { timezone = "America/New_York", csrfToken = validToken }
        in
        model


## Interfaces and Dependencies

No new package dependencies are required.

New module interface in `src/frontend/booking/src/ApiCodecs.elm`:

- `csrfTokenDecoder : Decoder String`
- `parseResponseDecoder : Decoder ParseResponse`
- `parseResultDecoder : Decoder ParseResult`
- `availabilityWindowDecoder : Decoder AvailabilityWindow`
- `encodeAvailabilityWindow : AvailabilityWindow -> Encode.Value`
- `slotsResponseDecoder : Decoder (List TimeSlot)`
- `timeSlotDecoder : Decoder TimeSlot`
- `bookingConfirmationDecoder : Decoder BookingConfirmation`

`src/frontend/booking/src/Api.elm` remains the HTTP boundary and keeps existing public
function signatures unchanged.


## Revision Note (2026-02-20)

Revised from the previous plan version and moved under `docs/plans/`.
The major change is replacing “export private decoders from `Api.elm`” with an
`ApiCodecs.elm` extraction to satisfy `elm-review` (`NoUnused.Exports`) while still
allowing direct codec tests. The update and fuzz milestones were also strengthened with
explicit no-CSRF scenarios and higher-signal property tests.