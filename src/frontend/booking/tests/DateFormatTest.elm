module DateFormatTest exposing (suite)

import DateFormat exposing (formatFriendlyDate, formatFriendlyTime)
import Expect
import Test exposing (Test, describe, test)


suite : Test
suite =
    describe "DateFormat"
        [ describe "formatFriendlyDate"
            [ test "formats a Monday" <|
                \_ ->
                    formatFriendlyDate "2026-02-09T09:00:00-05:00"
                        |> Expect.equal "Monday, Feb 9"
            , test "formats a Friday" <|
                \_ ->
                    formatFriendlyDate "2026-02-06T14:30:00-05:00"
                        |> Expect.equal "Friday, Feb 6"
            , test "formats a Sunday" <|
                \_ ->
                    formatFriendlyDate "2026-02-08T10:00:00-05:00"
                        |> Expect.equal "Sunday, Feb 8"
            , test "formats a Saturday" <|
                \_ ->
                    formatFriendlyDate "2026-02-07T10:00:00-05:00"
                        |> Expect.equal "Saturday, Feb 7"
            , test "formats New Year's Day 2026 (Thursday)" <|
                \_ ->
                    formatFriendlyDate "2026-01-01T00:00:00Z"
                        |> Expect.equal "Thursday, Jan 1"
            , test "formats a date in December" <|
                \_ ->
                    formatFriendlyDate "2025-12-25T12:00:00-05:00"
                        |> Expect.equal "Thursday, Dec 25"
            , test "formats a leap year date" <|
                \_ ->
                    formatFriendlyDate "2024-02-29T09:00:00-05:00"
                        |> Expect.equal "Thursday, Feb 29"
            , test "falls back on malformed input" <|
                \_ ->
                    formatFriendlyDate "not-a-date"
                        |> Expect.equal "not-a-date"
            , test "falls back on empty string" <|
                \_ ->
                    formatFriendlyDate ""
                        |> Expect.equal ""
            , test "falls back on non-numeric date parts" <|
                \_ ->
                    formatFriendlyDate "abcd-ef-ghTHH:MM:SS"
                        |> Expect.equal "abcd-ef-gh"
            ]
        , describe "formatFriendlyTime"
            [ test "formats midnight as 12 AM" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T00:00:00-05:00"
                        |> Expect.equal "12 AM"
            , test "formats noon as 12 PM" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T12:00:00-05:00"
                        |> Expect.equal "12 PM"
            , test "formats morning on the hour without minutes" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T09:00:00-05:00"
                        |> Expect.equal "9 AM"
            , test "formats afternoon with minutes" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T14:30:00-05:00"
                        |> Expect.equal "2:30 PM"
            , test "formats 1 AM" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T01:00:00-05:00"
                        |> Expect.equal "1 AM"
            , test "formats 11 AM with minutes" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T11:45:00-05:00"
                        |> Expect.equal "11:45 AM"
            , test "formats 11 PM" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T23:00:00-05:00"
                        |> Expect.equal "11 PM"
            , test "formats 12:30 PM with minutes shown" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T12:30:00-05:00"
                        |> Expect.equal "12:30 PM"
            , test "formats 12:15 AM (just after midnight)" <|
                \_ ->
                    formatFriendlyTime "2026-02-06T00:15:00-05:00"
                        |> Expect.equal "12:15 AM"
            , test "falls back on malformed input (short string)" <|
                \_ ->
                    formatFriendlyTime "not-a-time"
                        |> Expect.equal ""
            , test "falls back on malformed input (non-numeric time)" <|
                \_ ->
                    -- slice 11..16 = "ab:cd", split on ":" = ["ab","cd"], toInt "ab" = Nothing
                    formatFriendlyTime "2026-02-06Tab:cd:00-05:00"
                        |> Expect.equal "ab:cd"
            , test "falls back on empty string" <|
                \_ ->
                    formatFriendlyTime ""
                        |> Expect.equal ""
            ]
        ]
