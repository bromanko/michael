module Michael.Tests.HandlerTests

open Expecto
open Michael.Handlers

[<Tests>]
let isValidEmailTests =
    testList "isValidEmail" [
        test "accepts valid email" {
            Expect.isTrue (isValidEmail "alice@example.com") "standard email"
        }

        test "accepts email with subdomain" {
            Expect.isTrue (isValidEmail "user@mail.example.com") "subdomain email"
        }

        test "rejects empty string" {
            Expect.isFalse (isValidEmail "") "empty string"
        }

        test "rejects whitespace" {
            Expect.isFalse (isValidEmail "   ") "whitespace only"
        }

        test "rejects null" {
            Expect.isFalse (isValidEmail null) "null"
        }

        test "rejects missing @" {
            Expect.isFalse (isValidEmail "aliceexample.com") "no @ sign"
        }

        test "rejects missing domain" {
            Expect.isFalse (isValidEmail "alice@") "no domain"
        }

        test "rejects missing local part" {
            Expect.isFalse (isValidEmail "@example.com") "no local part"
        }

        test "rejects domain without dot" {
            Expect.isFalse (isValidEmail "alice@localhost") "no dot in domain"
        }

        test "rejects domain ending with dot" {
            Expect.isFalse (isValidEmail "alice@example.") "domain ends with dot"
        }

        test "rejects multiple @ signs" {
            Expect.isFalse (isValidEmail "alice@bob@example.com") "multiple @"
        }
    ]

[<Tests>]
let tryParseOdtTests =
    testList "tryParseOdt" [
        test "parses valid ISO-8601 with offset" {
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
    ]

[<Tests>]
let tryResolveTimezoneTests =
    testList "tryResolveTimezone" [
        test "resolves valid IANA timezone" {
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
    ]
