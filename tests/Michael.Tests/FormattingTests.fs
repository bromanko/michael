module Michael.Tests.FormattingTests

open Expecto
open NodaTime
open NodaTime.Text
open Michael.Formatting

/// The parse pattern used by Handlers.fs for input.
let private parsePattern = OffsetDateTimePattern.ExtendedIso

[<Tests>]
let formattingTests =
    testList
        "Formatting"
        [ testList
              "odtFormatPattern"
              [ test "negative offset formats as -HH:MM" {
                    let odt = OffsetDateTime(LocalDateTime(2026, 2, 20, 13, 0, 0), Offset.FromHours(-8))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-02-20T13:00:00-08:00" "full negative offset"
                }

                test "positive offset formats as +HH:MM" {
                    let odt = OffsetDateTime(LocalDateTime(2026, 7, 15, 10, 30, 0), Offset.FromHours(5))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-07-15T10:30:00+05:00" "positive offset"
                }

                test "UTC formats as +00:00" {
                    let odt = OffsetDateTime(LocalDateTime(2026, 1, 1, 0, 0, 0), Offset.Zero)

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-01-01T00:00:00+00:00" "UTC zero offset"
                }

                test "half-hour offset formats correctly" {
                    let odt =
                        OffsetDateTime(LocalDateTime(2026, 3, 10, 8, 0, 0), Offset.FromHoursAndMinutes(5, 30))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-03-10T08:00:00+05:30" "India +05:30"
                }

                test "45-minute offset formats correctly" {
                    let odt =
                        OffsetDateTime(LocalDateTime(2026, 6, 1, 12, 0, 0), Offset.FromHoursAndMinutes(5, 45))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-06-01T12:00:00+05:45" "Nepal +05:45"
                }

                test "negative half-hour offset formats correctly" {
                    let odt =
                        OffsetDateTime(LocalDateTime(2026, 4, 1, 9, 0, 0), Offset.FromHoursAndMinutes(-9, -30))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-04-01T09:00:00-09:30" "Marquesas -09:30"
                }

                test "midnight with negative offset" {
                    let odt = OffsetDateTime(LocalDateTime(2026, 12, 31, 0, 0, 0), Offset.FromHours(-5))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-12-31T00:00:00-05:00" "midnight EST"
                }

                test "end of day with positive offset" {
                    let odt =
                        OffsetDateTime(LocalDateTime(2026, 8, 15, 23, 59, 59), Offset.FromHours(12))

                    let result = odtFormatPattern.Format(odt)
                    Expect.equal result "2026-08-15T23:59:59+12:00" "end of day NZST"
                } ]

          testList
              "format→parse roundtrip"
              [ let roundtripCase name odt =
                    test name {
                        let formatted = odtFormatPattern.Format(odt)
                        let parsed = parsePattern.Parse(formatted)
                        Expect.isTrue parsed.Success $"should parse formatted value: {formatted}"
                        Expect.equal parsed.Value odt "roundtrip must preserve value"
                    }

                roundtripCase
                    "negative whole-hour offset"
                    (OffsetDateTime(LocalDateTime(2026, 2, 20, 13, 0, 0), Offset.FromHours(-5)))

                roundtripCase
                    "positive whole-hour offset"
                    (OffsetDateTime(LocalDateTime(2026, 7, 15, 10, 30, 0), Offset.FromHours(9)))

                roundtripCase "UTC zero offset" (OffsetDateTime(LocalDateTime(2026, 1, 1, 0, 0, 0), Offset.Zero))

                roundtripCase
                    "half-hour offset (+05:30)"
                    (OffsetDateTime(LocalDateTime(2026, 3, 10, 8, 0, 0), Offset.FromHoursAndMinutes(5, 30)))

                roundtripCase
                    "45-minute offset (+05:45)"
                    (OffsetDateTime(LocalDateTime(2026, 6, 1, 12, 0, 0), Offset.FromHoursAndMinutes(5, 45)))

                roundtripCase
                    "negative half-hour offset (-09:30)"
                    (OffsetDateTime(LocalDateTime(2026, 4, 1, 9, 0, 0), Offset.FromHoursAndMinutes(-9, -30)))

                roundtripCase
                    "midnight boundary"
                    (OffsetDateTime(LocalDateTime(2026, 12, 31, 0, 0, 0), Offset.FromHours(-12)))

                roundtripCase
                    "max positive offset (+14:00)"
                    (OffsetDateTime(LocalDateTime(2026, 1, 1, 12, 0, 0), Offset.FromHours(14))) ]

          testList
              "parse pattern compatibility"
              [ test "ExtendedIso accepts shortened offset -08" {
                    let parsed = parsePattern.Parse("2026-02-20T13:00:00-08")
                    Expect.isTrue parsed.Success "ExtendedIso should accept shortened offset"

                    Expect.equal
                        parsed.Value
                        (OffsetDateTime(LocalDateTime(2026, 2, 20, 13, 0, 0), Offset.FromHours(-8)))
                        "parsed value matches"
                }

                test "ExtendedIso accepts shortened offset +05" {
                    let parsed = parsePattern.Parse("2026-07-15T10:30:00+05")
                    Expect.isTrue parsed.Success "should accept shortened positive offset"

                    Expect.equal
                        parsed.Value
                        (OffsetDateTime(LocalDateTime(2026, 7, 15, 10, 30, 0), Offset.FromHours(5)))
                        "parsed value matches"
                }

                test "ExtendedIso accepts full offset -08:00" {
                    let parsed = parsePattern.Parse("2026-02-20T13:00:00-08:00")
                    Expect.isTrue parsed.Success "should accept full offset"

                    Expect.equal
                        parsed.Value
                        (OffsetDateTime(LocalDateTime(2026, 2, 20, 13, 0, 0), Offset.FromHours(-8)))
                        "parsed value matches"
                }

                test "shortened offset parses to same value as full offset" {
                    let short = parsePattern.Parse("2026-02-20T13:00:00-08")
                    let full = parsePattern.Parse("2026-02-20T13:00:00-08:00")
                    Expect.isTrue short.Success "short should parse"
                    Expect.isTrue full.Success "full should parse"
                    Expect.equal short.Value full.Value "both forms produce same value"
                } ] ]
