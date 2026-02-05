module TypesTest exposing (suite)

import Expect
import Fuzz exposing (Fuzzer)
import Test exposing (Test, describe, fuzz, test)
import Types exposing (DayOfWeek(..), dayOfWeekFromInt, dayOfWeekLabel, dayOfWeekToInt)


suite : Test
suite =
    describe "Types"
        [ dayOfWeekTests
        ]


dayOfWeekTests : Test
dayOfWeekTests =
    describe "DayOfWeek"
        [ describe "dayOfWeekToInt"
            [ test "Monday is 1" <|
                \_ ->
                    dayOfWeekToInt Monday
                        |> Expect.equal 1
            , test "Sunday is 7" <|
                \_ ->
                    dayOfWeekToInt Sunday
                        |> Expect.equal 7
            ]
        , describe "dayOfWeekFromInt"
            [ test "1 is Monday" <|
                \_ ->
                    dayOfWeekFromInt 1
                        |> Expect.equal (Just Monday)
            , test "7 is Sunday" <|
                \_ ->
                    dayOfWeekFromInt 7
                        |> Expect.equal (Just Sunday)
            , test "0 is Nothing" <|
                \_ ->
                    dayOfWeekFromInt 0
                        |> Expect.equal Nothing
            , test "8 is Nothing" <|
                \_ ->
                    dayOfWeekFromInt 8
                        |> Expect.equal Nothing
            , test "-1 is Nothing" <|
                \_ ->
                    dayOfWeekFromInt -1
                        |> Expect.equal Nothing
            ]
        , describe "round-trip"
            [ fuzz validDayOfWeekInt "dayOfWeekFromInt >> dayOfWeekToInt round-trips" <|
                \n ->
                    dayOfWeekFromInt n
                        |> Maybe.map dayOfWeekToInt
                        |> Expect.equal (Just n)
            , test "all days round-trip" <|
                \_ ->
                    let
                        allDays =
                            [ Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday ]

                        roundTrip day =
                            dayOfWeekFromInt (dayOfWeekToInt day) == Just day
                    in
                    List.all roundTrip allDays
                        |> Expect.equal True
            ]
        , describe "dayOfWeekLabel"
            [ test "Monday label" <|
                \_ ->
                    dayOfWeekLabel Monday
                        |> Expect.equal "Monday"
            , test "Sunday label" <|
                \_ ->
                    dayOfWeekLabel Sunday
                        |> Expect.equal "Sunday"
            ]
        ]


validDayOfWeekInt : Fuzzer Int
validDayOfWeekInt =
    Fuzz.intRange 1 7
