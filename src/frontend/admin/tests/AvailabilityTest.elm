module AvailabilityTest exposing (suite)

import Expect
import Page.Availability exposing (Msg(..), isEndAfterStart, isValidTime, validateSlots)
import Test exposing (Test, describe, test)
import Types exposing (AvailabilitySlotInput, DayOfWeek(..))


suite : Test
suite =
    describe "Page.Availability"
        [ isValidTimeTests
        , isEndAfterStartTests
        , validateSlotsTests
        ]


isValidTimeTests : Test
isValidTimeTests =
    describe "isValidTime"
        [ test "valid time 09:00" <|
            \_ ->
                isValidTime "09:00"
                    |> Expect.equal True
        , test "valid time 00:00" <|
            \_ ->
                isValidTime "00:00"
                    |> Expect.equal True
        , test "valid time 23:59" <|
            \_ ->
                isValidTime "23:59"
                    |> Expect.equal True
        , test "invalid hour 24:00" <|
            \_ ->
                isValidTime "24:00"
                    |> Expect.equal False
        , test "invalid hour 99:00" <|
            \_ ->
                isValidTime "99:00"
                    |> Expect.equal False
        , test "invalid minute 09:60" <|
            \_ ->
                isValidTime "09:60"
                    |> Expect.equal False
        , test "negative hour -1:00" <|
            \_ ->
                isValidTime "-1:00"
                    |> Expect.equal False
        , test "non-numeric" <|
            \_ ->
                isValidTime "ab:cd"
                    |> Expect.equal False
        , test "missing colon" <|
            \_ ->
                isValidTime "0900"
                    |> Expect.equal False
        , test "empty string" <|
            \_ ->
                isValidTime ""
                    |> Expect.equal False
        , test "too many colons" <|
            \_ ->
                isValidTime "09:00:00"
                    |> Expect.equal False
        ]


isEndAfterStartTests : Test
isEndAfterStartTests =
    describe "isEndAfterStart"
        [ test "end after start" <|
            \_ ->
                isEndAfterStart "09:00" "17:00"
                    |> Expect.equal True
        , test "end equals start" <|
            \_ ->
                isEndAfterStart "09:00" "09:00"
                    |> Expect.equal False
        , test "end before start" <|
            \_ ->
                isEndAfterStart "17:00" "09:00"
                    |> Expect.equal False
        , test "minute difference" <|
            \_ ->
                isEndAfterStart "09:00" "09:01"
                    |> Expect.equal True
        ]


validateSlotsTests : Test
validateSlotsTests =
    describe "validateSlots"
        [ test "valid slot passes" <|
            \_ ->
                let
                    slot =
                        { dayOfWeek = Monday
                        , startTime = "09:00"
                        , endTime = "17:00"
                        , timezone = "America/New_York"
                        }
                in
                validateSlots [ slot ]
                    |> Expect.ok
        , test "empty list passes" <|
            \_ ->
                validateSlots []
                    |> Expect.ok
        , test "multiple valid slots pass" <|
            \_ ->
                let
                    slots =
                        [ { dayOfWeek = Monday, startTime = "09:00", endTime = "12:00", timezone = "UTC" }
                        , { dayOfWeek = Tuesday, startTime = "13:00", endTime = "17:00", timezone = "UTC" }
                        ]
                in
                validateSlots slots
                    |> Expect.ok
        , test "invalid start time fails" <|
            \_ ->
                let
                    slot =
                        { dayOfWeek = Monday
                        , startTime = "25:00"
                        , endTime = "17:00"
                        , timezone = "America/New_York"
                        }
                in
                validateSlots [ slot ]
                    |> Expect.err
        , test "invalid end time fails" <|
            \_ ->
                let
                    slot =
                        { dayOfWeek = Monday
                        , startTime = "09:00"
                        , endTime = "99:99"
                        , timezone = "America/New_York"
                        }
                in
                validateSlots [ slot ]
                    |> Expect.err
        , test "end before start fails" <|
            \_ ->
                let
                    slot =
                        { dayOfWeek = Monday
                        , startTime = "17:00"
                        , endTime = "09:00"
                        , timezone = "America/New_York"
                        }
                in
                validateSlots [ slot ]
                    |> Expect.err
        , test "empty timezone fails" <|
            \_ ->
                let
                    slot =
                        { dayOfWeek = Monday
                        , startTime = "09:00"
                        , endTime = "17:00"
                        , timezone = ""
                        }
                in
                validateSlots [ slot ]
                    |> Expect.err
        , test "whitespace-only timezone fails" <|
            \_ ->
                let
                    slot =
                        { dayOfWeek = Monday
                        , startTime = "09:00"
                        , endTime = "17:00"
                        , timezone = "   "
                        }
                in
                validateSlots [ slot ]
                    |> Expect.err
        , test "error message includes slot number" <|
            \_ ->
                let
                    slots =
                        [ { dayOfWeek = Monday, startTime = "09:00", endTime = "17:00", timezone = "UTC" }
                        , { dayOfWeek = Tuesday, startTime = "17:00", endTime = "09:00", timezone = "UTC" }
                        ]
                in
                case validateSlots slots of
                    Err msg ->
                        String.contains "Slot 2" msg
                            |> Expect.equal True

                    Ok _ ->
                        Expect.fail "Expected validation to fail"
        ]
