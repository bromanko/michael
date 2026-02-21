module ModelTest exposing (suite)

import Expect
import Model exposing (Flags, Model, init, validCsrfToken)
import Test exposing (Test, describe, test)
import TestFixtures exposing (validToken)
import Types


uppercaseToken : String
uppercaseToken =
    "42:AABBCCDD11223344AABBCCDD11223344:AABBCCDD11223344AABBCCDD11223344AABBCCDD11223344AABBCCDD11223344"


baseFlags : Flags
baseFlags =
    { timezone = "America/New_York"
    , csrfToken = validToken
    }


initModel : Flags -> Model
initModel flags =
    flags
        |> init
        |> Tuple.first


suite : Test
suite =
    describe "Model"
        [ describe "init"
            [ test "initializes default model values" <|
                \_ ->
                    let
                        model =
                            initModel baseFlags
                    in
                    model
                        |> Expect.equal
                            { currentStep = Types.TitleStep
                            , title = ""
                            , availabilityText = ""
                            , parsedWindows = []
                            , slots = []
                            , selectedSlot = Nothing
                            , name = ""
                            , email = ""
                            , phone = ""
                            , timezone = "America/New_York"
                            , timezoneDropdownOpen = False
                            , loading = False
                            , error = Nothing
                            , bookingResult = Nothing
                            , csrfToken = Just validToken
                            , csrfRefreshAttempted = False
                            }
            , test "falls back to UTC when timezone is invalid" <|
                \_ ->
                    initModel { baseFlags | timezone = "NotATimezone" }
                        |> .timezone
                        |> Expect.equal "UTC"
            , test "falls back to UTC when timezone is empty" <|
                \_ ->
                    initModel { baseFlags | timezone = "" }
                        |> .timezone
                        |> Expect.equal "UTC"
            , test "falls back to UTC when timezone is too long" <|
                \_ ->
                    initModel { baseFlags | timezone = "America/New_York/with/an/unusually/long/segment/that/should/fail" }
                        |> .timezone
                        |> Expect.equal "UTC"
            , test "stores csrf token only when valid" <|
                \_ ->
                    initModel { baseFlags | csrfToken = "invalid-token" }
                        |> .csrfToken
                        |> Expect.equal Nothing
            ]
        , describe "validCsrfToken"
            [ test "accepts a valid lowercase token" <|
                \_ ->
                    validCsrfToken validToken
                        |> Expect.equal (Just validToken)
            , test "accepts uppercase hex characters" <|
                \_ ->
                    validCsrfToken uppercaseToken
                        |> Expect.equal (Just uppercaseToken)
            , test "rejects token with missing parts" <|
                \_ ->
                    validCsrfToken "123:abc"
                        |> Expect.equal Nothing
            , test "rejects token with non-positive issuedAt" <|
                \_ ->
                    validCsrfToken "0:aabbccdd11223344aabbccdd11223344:aabbccdd11223344aabbccdd11223344aabbccdd11223344aabbccdd11223344"
                        |> Expect.equal Nothing
            , test "rejects token with non-numeric issuedAt" <|
                \_ ->
                    validCsrfToken "issued:aabbccdd11223344aabbccdd11223344:aabbccdd11223344aabbccdd11223344aabbccdd11223344aabbccdd11223344"
                        |> Expect.equal Nothing
            , test "rejects token with nonce wrong length" <|
                \_ ->
                    validCsrfToken "12345:aabbccdd11223344:aabbccdd11223344aabbccdd11223344aabbccdd11223344aabbccdd11223344"
                        |> Expect.equal Nothing
            , test "rejects token with signature wrong length" <|
                \_ ->
                    validCsrfToken "12345:aabbccdd11223344aabbccdd11223344:aabbccdd11223344"
                        |> Expect.equal Nothing
            , test "rejects token with non-hex characters" <|
                \_ ->
                    validCsrfToken "12345:aabbccdd11223344aabbccdd1122334z:aabbccdd11223344aabbccdd11223344aabbccdd11223344aabbccdd11223344"
                        |> Expect.equal Nothing
            ]
        ]
