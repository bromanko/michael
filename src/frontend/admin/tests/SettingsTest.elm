module SettingsTest exposing (suite)

import Expect
import Page.Settings exposing (FormState, validateForm)
import Test exposing (Test, describe, test)


suite : Test
suite =
    describe "Page.Settings"
        [ validateFormTests
        ]


validForm : FormState
validForm =
    { minNoticeHours = "6"
    , bookingWindowDays = "30"
    , defaultDurationMinutes = "30"
    , videoLink = ""
    }


validateFormTests : Test
validateFormTests =
    describe "validateForm"
        [ test "valid form passes" <|
            \_ ->
                validateForm validForm
                    |> Expect.ok
        , test "non-numeric minNoticeHours fails" <|
            \_ ->
                validateForm { validForm | minNoticeHours = "abc" }
                    |> Expect.err
        , test "non-numeric bookingWindowDays fails" <|
            \_ ->
                validateForm { validForm | bookingWindowDays = "abc" }
                    |> Expect.err
        , test "non-numeric defaultDurationMinutes fails" <|
            \_ ->
                validateForm { validForm | defaultDurationMinutes = "abc" }
                    |> Expect.err
        , test "negative minNoticeHours fails" <|
            \_ ->
                validateForm { validForm | minNoticeHours = "-1" }
                    |> Expect.err
        , test "zero minNoticeHours passes" <|
            \_ ->
                validateForm { validForm | minNoticeHours = "0" }
                    |> Expect.ok
        , test "bookingWindowDays less than 1 fails" <|
            \_ ->
                validateForm { validForm | bookingWindowDays = "0" }
                    |> Expect.err
        , test "bookingWindowDays of 1 passes" <|
            \_ ->
                validateForm { validForm | bookingWindowDays = "1" }
                    |> Expect.ok
        , test "defaultDurationMinutes less than 5 fails" <|
            \_ ->
                validateForm { validForm | defaultDurationMinutes = "4" }
                    |> Expect.err
        , test "defaultDurationMinutes of 5 passes" <|
            \_ ->
                validateForm { validForm | defaultDurationMinutes = "5" }
                    |> Expect.ok
        , test "defaultDurationMinutes of 480 passes" <|
            \_ ->
                validateForm { validForm | defaultDurationMinutes = "480" }
                    |> Expect.ok
        , test "defaultDurationMinutes over 480 fails" <|
            \_ ->
                validateForm { validForm | defaultDurationMinutes = "481" }
                    |> Expect.err
        , test "empty videoLink results in Nothing" <|
            \_ ->
                validateForm { validForm | videoLink = "" }
                    |> Result.map .videoLink
                    |> Expect.equal (Ok Nothing)
        , test "whitespace-only videoLink results in Nothing" <|
            \_ ->
                validateForm { validForm | videoLink = "   " }
                    |> Result.map .videoLink
                    |> Expect.equal (Ok Nothing)
        , test "videoLink is trimmed" <|
            \_ ->
                validateForm { validForm | videoLink = "  https://zoom.us/j/123  " }
                    |> Result.map .videoLink
                    |> Expect.equal (Ok (Just "https://zoom.us/j/123"))
        , test "valid videoLink is preserved" <|
            \_ ->
                validateForm { validForm | videoLink = "https://zoom.us/j/123" }
                    |> Result.map .videoLink
                    |> Expect.equal (Ok (Just "https://zoom.us/j/123"))
        ]
