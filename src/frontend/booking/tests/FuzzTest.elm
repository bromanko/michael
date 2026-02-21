module FuzzTest exposing (suite)

import ApiCodecs exposing (encodeAvailabilityWindow)
import DateFormat exposing (formatFriendlyTime)
import Expect
import Fuzz
import Json.Decode as Decode
import Json.Encode as Encode
import Model
import Test exposing (Test, describe, fuzz, fuzz2)
import Types
import Update


hexChars : List Char
hexChars =
    [ '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' ]


hexCharFuzzer : Fuzz.Fuzzer Char
hexCharFuzzer =
    Fuzz.oneOfValues hexChars


fixedHexFuzzer : Int -> Fuzz.Fuzzer String
fixedHexFuzzer length =
    Fuzz.listOfLength length hexCharFuzzer
        |> Fuzz.map String.fromList


validTokenFuzzer : Fuzz.Fuzzer String
validTokenFuzzer =
    Fuzz.map3
        (\issuedAt nonce signature ->
            String.fromInt issuedAt ++ ":" ++ nonce ++ ":" ++ signature
        )
        (Fuzz.intRange 1 2000000000)
        (fixedHexFuzzer 32)
        (fixedHexFuzzer 64)


isoDateTimeFuzzer : Fuzz.Fuzzer String
isoDateTimeFuzzer =
    Fuzz.map4
        (\year monthDay hourMinute offsetHour ->
            let
                month =
                    Tuple.first monthDay

                day =
                    Tuple.second monthDay

                hour =
                    Tuple.first hourMinute

                minute =
                    Tuple.second hourMinute

                sign =
                    if offsetHour >= 0 then
                        "+"

                    else
                        "-"
            in
            String.fromInt year
                ++ "-"
                ++ pad2 month
                ++ "-"
                ++ pad2 day
                ++ "T"
                ++ pad2 hour
                ++ ":"
                ++ pad2 minute
                ++ ":00"
                ++ sign
                ++ pad2 (abs offsetHour)
                ++ ":00"
        )
        (Fuzz.intRange 2020 2035)
        (Fuzz.pair (Fuzz.intRange 1 12) (Fuzz.intRange 1 28))
        (Fuzz.pair (Fuzz.intRange 0 23) (Fuzz.intRange 0 59))
        (Fuzz.intRange -12 12)


availabilityWindowFuzzer : Fuzz.Fuzzer Types.AvailabilityWindow
availabilityWindowFuzzer =
    Fuzz.map3
        (\startText endText timezoneText ->
            { start = startText
            , end = endText
            , timezone = timezoneText
            }
        )
        isoDateTimeFuzzer
        isoDateTimeFuzzer
        (Fuzz.maybe Fuzz.string)


flagsFuzzer : Fuzz.Fuzzer Model.Flags
flagsFuzzer =
    Fuzz.map2
        (\timezone csrfToken ->
            { timezone = timezone
            , csrfToken = csrfToken
            }
        )
        Fuzz.string
        Fuzz.string


pad2 : Int -> String
pad2 number =
    if number < 10 then
        "0" ++ String.fromInt number

    else
        String.fromInt number


applyBackNTimes : Int -> Model.Model -> Model.Model
applyBackNTimes count model =
    if count <= 0 then
        model

    else
        applyBackNTimes (count - 1) (model |> Update.update Update.BackStepClicked |> Tuple.first)


suite : Test
suite =
    describe "Fuzz invariants"
        [ fuzz validTokenFuzzer "validCsrfToken accepts all generated valid-shape tokens" <|
            \token ->
                Model.validCsrfToken token
                    |> Expect.equal (Just token)
        , fuzz Fuzz.string "validCsrfToken identity: whenever result is Just it equals input" <|
            \token ->
                case Model.validCsrfToken token of
                    Just acceptedToken ->
                        acceptedToken
                            |> Expect.equal token

                    Nothing ->
                        Expect.pass
        , fuzz availabilityWindowFuzzer "encodeAvailabilityWindow round-trips through decoder" <|
            \window ->
                let
                    decoder =
                        Decode.map3 (\s e tz -> { start = s, end = e, timezone = tz })
                            (Decode.field "start" Decode.string)
                            (Decode.field "end" Decode.string)
                            (Decode.field "timezone" (Decode.nullable Decode.string))
                in
                window
                    |> encodeAvailabilityWindow
                    |> Encode.encode 0
                    |> Decode.decodeString decoder
                    |> Expect.equal (Ok window)
        , fuzz flagsFuzzer "Model.init always starts at TitleStep" <|
            \flags ->
                let
                    ( model, _ ) =
                        Model.init flags
                in
                model.currentStep
                    |> Expect.equal Types.TitleStep
        , fuzz (Fuzz.intRange 0 50) "BackStepClicked repeatedly from TitleStep stays at TitleStep" <|
            \count ->
                let
                    model =
                        let
                            ( baseModel, _ ) =
                                Model.init { timezone = "UTC", csrfToken = "invalid" }
                        in
                        { baseModel | currentStep = Types.TitleStep }
                            |> applyBackNTimes count
                in
                model.currentStep
                    |> Expect.equal Types.TitleStep
        , fuzz (Fuzz.intRange 0 50) "BackStepClicked repeatedly from CompleteStep stays at CompleteStep" <|
            \count ->
                let
                    model =
                        let
                            ( baseModel, _ ) =
                                Model.init { timezone = "UTC", csrfToken = "invalid" }
                        in
                        { baseModel | currentStep = Types.CompleteStep }
                            |> applyBackNTimes count
                in
                model.currentStep
                    |> Expect.equal Types.CompleteStep
        , fuzz2 (Fuzz.intRange 0 23) (Fuzz.intRange 0 59) "formatFriendlyTime keeps AM/PM and omits : for zero minutes" <|
            \hour minute ->
                let
                    isoTime =
                        "2026-02-09T" ++ pad2 hour ++ ":" ++ pad2 minute ++ ":00-05:00"

                    output =
                        formatFriendlyTime isoTime
                in
                Expect.all
                    [ \o ->
                        (String.contains "AM" o || String.contains "PM" o)
                            |> Expect.equal True
                            |> Expect.onFail ("Expected AM or PM in: " ++ o)
                    , \o ->
                        String.contains ":" o
                            |> Expect.equal (minute /= 0)
                            |> Expect.onFail ("Colon presence mismatch for minute=" ++ String.fromInt minute ++ " in: " ++ o)
                    ]
                    output
        ]
