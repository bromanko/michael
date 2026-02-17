module Michael.Tests.HandlerTests

open Expecto
open NodaTime
open Michael.Handlers

[<Tests>]
let isValidEmailTests =
    testList
        "isValidEmail"
        [ test "accepts valid email" { Expect.isTrue (isValidEmail "alice@example.com") "standard email" }

          test "accepts email with subdomain" { Expect.isTrue (isValidEmail "user@mail.example.com") "subdomain email" }

          test "rejects empty string" { Expect.isFalse (isValidEmail "") "empty string" }

          test "rejects whitespace" { Expect.isFalse (isValidEmail "   ") "whitespace only" }

          test "rejects null" { Expect.isFalse (isValidEmail null) "null" }

          test "rejects missing @" { Expect.isFalse (isValidEmail "aliceexample.com") "no @ sign" }

          test "rejects missing domain" { Expect.isFalse (isValidEmail "alice@") "no domain" }

          test "rejects missing local part" { Expect.isFalse (isValidEmail "@example.com") "no local part" }

          test "rejects domain without dot" { Expect.isFalse (isValidEmail "alice@localhost") "no dot in domain" }

          test "rejects domain ending with dot" {
              Expect.isFalse (isValidEmail "alice@example.") "domain ends with dot"
          }

          test "rejects multiple @ signs" { Expect.isFalse (isValidEmail "alice@bob@example.com") "multiple @" } ]

[<Tests>]
let tryParseOdtTests =
    testList
        "tryParseOdt"
        [ test "parses valid ISO-8601 with offset" {
              let result = tryParseOdt "start" "2026-02-15T14:00:00-05:00"
              Expect.isOk result "should parse successfully"
          }

          test "parses UTC offset" {
              let result = tryParseOdt "start" "2026-02-15T14:00:00Z"
              Expect.isOk result "should parse UTC"
          }

          test "returns error for invalid format" {
              let result = tryParseOdt "start" "not-a-date"
              Expect.isError result "should fail on invalid input"
          }

          test "returns error for date without time" {
              let result = tryParseOdt "start" "2026-02-15"
              Expect.isError result "should fail on date-only"
          }

          test "returns error for empty string" {
              let result = tryParseOdt "start" ""
              Expect.isError result "should fail on empty"
          }

          test "error message includes field name" {
              let result = tryParseOdt "Slot.Start" "bad"

              match result with
              | Error msg -> Expect.stringContains msg "Slot.Start" "error mentions field name"
              | Ok _ -> failtest "expected error"
          }

          test "error message includes invalid value" {
              let result = tryParseOdt "Slot.Start" "not-a-date"

              match result with
              | Error msg -> Expect.stringContains msg "not-a-date" "error mentions invalid value"
              | Ok _ -> failtest "expected error"
          }

          test "error message includes both field name and value" {
              let result = tryParseOdt "AvailabilityWindows[0].End" "12:00"

              match result with
              | Error msg ->
                  Expect.stringContains msg "AvailabilityWindows[0].End" "error mentions field"
                  Expect.stringContains msg "12:00" "error mentions value"
              | Ok _ -> failtest "expected error"
          }

          test "parses shortened offset -08" {
              let result = tryParseOdt "start" "2026-02-20T13:00:00-08"
              Expect.isOk result "should accept shortened offset"

              let odt = Result.defaultValue Unchecked.defaultof<_> result
              Expect.equal odt.Offset (Offset.FromHours(-8)) "offset is -08:00"
          }

          test "parses half-hour offset +05:30" {
              let result = tryParseOdt "start" "2026-03-10T08:00:00+05:30"
              Expect.isOk result "should accept half-hour offset"

              let odt = Result.defaultValue Unchecked.defaultof<_> result
              Expect.equal odt.Offset (Offset.FromHoursAndMinutes(5, 30)) "offset is +05:30"
          }

          test "parsed value has correct date and time" {
              let result = tryParseOdt "start" "2026-07-15T10:30:00+09:00"
              Expect.isOk result "should parse"

              let odt = Result.defaultValue Unchecked.defaultof<_> result
              Expect.equal odt.Year 2026 "year"
              Expect.equal odt.Month 7 "month"
              Expect.equal odt.Day 15 "day"
              Expect.equal odt.Hour 10 "hour"
              Expect.equal odt.Minute 30 "minute"
              Expect.equal odt.Second 0 "second"
          }

          test "returns error for null value" {
              let result = tryParseOdt "start" null
              Expect.isError result "should fail on null"
          }

          test "returns error for datetime without offset" {
              let result = tryParseOdt "start" "2026-02-15T14:00:00"
              Expect.isError result "should fail on datetime without offset"
          }

          test "error truncates very long invalid value" {
              let longValue = String.replicate 200 "x"
              let result = tryParseOdt "start" longValue

              match result with
              | Error msg ->
                  // Should contain first 80 chars + ellipsis, not the full 200
                  Expect.stringContains msg (String.replicate 80 "x") "contains truncated prefix"
                  Expect.stringContains msg "…" "contains ellipsis"
                  Expect.isFalse (msg.Contains(String.replicate 81 "x")) "does not contain chars beyond limit"
              | Ok _ -> failtest "expected error"
          } ]

[<Tests>]
let tryResolveTimezoneTests =
    testList
        "tryResolveTimezone"
        [ test "resolves valid IANA timezone" {
              let result = tryResolveTimezone "America/New_York"
              Expect.isOk result "should resolve"
          }

          test "resolves UTC" {
              let result = tryResolveTimezone "UTC"
              Expect.isOk result "should resolve UTC"
          }

          test "returns error for invalid timezone" {
              let result = tryResolveTimezone "Fake/Timezone"
              Expect.isError result "should fail on invalid tz"
          }

          test "returns error for empty string" {
              let result = tryResolveTimezone ""
              Expect.isError result "should fail on empty"
          }

          test "error message includes timezone id" {
              let result = tryResolveTimezone "Bad/Zone"

              match result with
              | Error msg -> Expect.stringContains msg "Bad/Zone" "error mentions tz id"
              | Ok _ -> failtest "expected error"
          }

          test "error truncates very long timezone id" {
              let longTz = String.replicate 200 "A"
              let result = tryResolveTimezone longTz

              match result with
              | Error msg ->
                  Expect.stringContains msg (String.replicate 80 "A") "contains truncated prefix"
                  Expect.stringContains msg "…" "contains ellipsis"
                  Expect.isFalse (msg.Contains(String.replicate 81 "A")) "does not contain chars beyond limit"
              | Ok _ -> failtest "expected error"
          } ]

[<Tests>]
let isValidDurationMinutesTests =
    testList
        "isValidDurationMinutes"
        [ test "4 is invalid (below lower bound)" { Expect.isFalse (isValidDurationMinutes 4) "4 minutes too short" }

          test "5 is valid (lower bound)" { Expect.isTrue (isValidDurationMinutes 5) "5 minutes is minimum" }

          test "6 is valid (just above lower bound)" { Expect.isTrue (isValidDurationMinutes 6) "6 minutes is valid" }

          test "30 is valid (typical)" { Expect.isTrue (isValidDurationMinutes 30) "30 minutes" }

          test "60 is valid (typical)" { Expect.isTrue (isValidDurationMinutes 60) "60 minutes" }

          test "479 is valid (just below upper bound)" {
              Expect.isTrue (isValidDurationMinutes 479) "479 minutes is valid"
          }

          test "480 is valid (upper bound)" { Expect.isTrue (isValidDurationMinutes 480) "480 minutes is maximum" }

          test "481 is invalid (above upper bound)" {
              Expect.isFalse (isValidDurationMinutes 481) "481 minutes too long"
          }

          test "0 is invalid" { Expect.isFalse (isValidDurationMinutes 0) "zero" }

          test "-1 is invalid" { Expect.isFalse (isValidDurationMinutes -1) "negative" }

          test "Int32.MinValue is invalid" { Expect.isFalse (isValidDurationMinutes System.Int32.MinValue) "min int" }

          test "Int32.MaxValue is invalid" { Expect.isFalse (isValidDurationMinutes System.Int32.MaxValue) "max int" } ]
