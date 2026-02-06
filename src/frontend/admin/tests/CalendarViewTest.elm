module CalendarViewTest exposing (suite)

import Expect
import Page.CalendarView exposing (addDaysToDate, getDaysInMonth, isLeapYear)
import Test exposing (Test, describe, test)


suite : Test
suite =
    describe "Page.CalendarView"
        [ isLeapYearTests
        , getDaysInMonthTests
        , addDaysToDateTests
        ]


isLeapYearTests : Test
isLeapYearTests =
    describe "isLeapYear"
        [ test "2024 is a leap year" <|
            \_ ->
                isLeapYear 2024
                    |> Expect.equal True
        , test "2025 is not a leap year" <|
            \_ ->
                isLeapYear 2025
                    |> Expect.equal False
        , test "1900 is not a leap year (divisible by 100)" <|
            \_ ->
                isLeapYear 1900
                    |> Expect.equal False
        , test "2000 is a leap year (divisible by 400)" <|
            \_ ->
                isLeapYear 2000
                    |> Expect.equal True
        ]


getDaysInMonthTests : Test
getDaysInMonthTests =
    describe "getDaysInMonth"
        [ test "January has 31 days" <|
            \_ ->
                getDaysInMonth 2026 1
                    |> Expect.equal 31
        , test "February non-leap has 28 days" <|
            \_ ->
                getDaysInMonth 2026 2
                    |> Expect.equal 28
        , test "February leap has 29 days" <|
            \_ ->
                getDaysInMonth 2024 2
                    |> Expect.equal 29
        , test "April has 30 days" <|
            \_ ->
                getDaysInMonth 2026 4
                    |> Expect.equal 30
        , test "December has 31 days" <|
            \_ ->
                getDaysInMonth 2026 12
                    |> Expect.equal 31
        ]


addDaysToDateTests : Test
addDaysToDateTests =
    describe "addDaysToDate"
        [ test "add 1 day mid-month" <|
            \_ ->
                addDaysToDate "2026-02-10" 1
                    |> Expect.equal "2026-02-11"
        , test "add 7 days within month" <|
            \_ ->
                addDaysToDate "2026-02-01" 7
                    |> Expect.equal "2026-02-08"
        , test "add 7 days crossing month boundary" <|
            \_ ->
                addDaysToDate "2026-02-25" 7
                    |> Expect.equal "2026-03-04"
        , test "subtract 7 days within month" <|
            \_ ->
                addDaysToDate "2026-02-15" -7
                    |> Expect.equal "2026-02-08"
        , test "subtract 7 days crossing month boundary" <|
            \_ ->
                addDaysToDate "2026-03-03" -7
                    |> Expect.equal "2026-02-24"
        , test "add days crossing year boundary" <|
            \_ ->
                addDaysToDate "2026-12-28" 7
                    |> Expect.equal "2027-01-04"
        , test "subtract days crossing year boundary" <|
            \_ ->
                addDaysToDate "2027-01-03" -7
                    |> Expect.equal "2026-12-27"
        , test "add 0 days returns same date" <|
            \_ ->
                addDaysToDate "2026-02-10" 0
                    |> Expect.equal "2026-02-10"
        , test "feb 28 + 1 in leap year" <|
            \_ ->
                addDaysToDate "2024-02-28" 1
                    |> Expect.equal "2024-02-29"
        , test "feb 28 + 1 in non-leap year" <|
            \_ ->
                addDaysToDate "2026-02-28" 1
                    |> Expect.equal "2026-03-01"
        ]
