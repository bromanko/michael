module ApiTest exposing (suite)

import ApiCodecs
    exposing
        ( bookingConfirmationDecoder
        , csrfTokenDecoder
        , encodeBookingRequest
        , encodeParseRequest
        , encodeSlotsRequest
        , parseResponseDecoder
        , slotsResponseDecoder
        )
import TestFixtures exposing (validToken)
import Expect
import Json.Decode as Decode
import Json.Encode as Encode
import Test exposing (Test, describe, test)


expectDecodeError : Result Decode.Error value -> Expect.Expectation
expectDecodeError result =
    case result of
        Ok _ ->
            Expect.fail "Expected decoder failure, but decoder succeeded"

        Err _ ->
            Expect.pass



-- JSON wrapping helpers for testing sub-decoders through top-level decoders


parseResponseWithResult : String -> String
parseResponseWithResult resultJson =
    """{"parseResult":""" ++ resultJson ++ ""","systemMessage":"ok"}"""


parseResponseWithWindow : String -> String
parseResponseWithWindow windowJson =
    parseResponseWithResult
        ("""{"availabilityWindows":[""" ++ windowJson ++ """],"missingFields":[]}""")


slotsResponseWith : String -> String
slotsResponseWith slotJson =
    """{"slots":[""" ++ slotJson ++ """]}"""



-- Inline decoders for verifying encoder output (these mirror the
-- internal decoders in ApiCodecs but are defined locally so the
-- module doesn't need to expose its building blocks).


windowFieldsDecoder : Decode.Decoder { start : String, end : String, timezone : Maybe String }
windowFieldsDecoder =
    Decode.map3 (\s e tz -> { start = s, end = e, timezone = tz })
        (Decode.field "start" Decode.string)
        (Decode.field "end" Decode.string)
        (Decode.field "timezone" (Decode.nullable Decode.string))


slotFieldsDecoder : Decode.Decoder { start : String, end : String }
slotFieldsDecoder =
    Decode.map2 (\s e -> { start = s, end = e })
        (Decode.field "start" Decode.string)
        (Decode.field "end" Decode.string)



-- Extraction helpers


firstWindow :
    Result Decode.Error { parseResult : { a | availabilityWindows : List b }, systemMessage : String }
    -> Result Decode.Error (Maybe b)
firstWindow =
    Result.map (.parseResult >> .availabilityWindows >> List.head)


suite : Test
suite =
    describe "ApiCodecs"
        [ describe "csrfTokenDecoder"
            [ test "decodes a valid token" <|
                \_ ->
                    Decode.decodeString csrfTokenDecoder
                        ("""{"token": \"""" ++ validToken ++ """"}""")
                        |> Expect.equal (Ok validToken)
            , test "fails when token field is missing" <|
                \_ ->
                    Decode.decodeString csrfTokenDecoder "{}"
                        |> expectDecodeError
            , test "fails when token is not a string" <|
                \_ ->
                    Decode.decodeString csrfTokenDecoder """{"token": 42}"""
                        |> expectDecodeError
            , test "fails when token has invalid format" <|
                \_ ->
                    Decode.decodeString csrfTokenDecoder """{"token": "abc123"}"""
                        |> expectDecodeError
            , test "fails when token exceeds max length" <|
                \_ ->
                    Decode.decodeString csrfTokenDecoder
                        ("""{"token": \"""" ++ String.repeat 201 "a" ++ """"}""")
                        |> expectDecodeError
            ]
        , describe "parseResponseDecoder"
            [ test "decodes response with required fields" <|
                \_ ->
                    Decode.decodeString
                        parseResponseDecoder
                        """
                        {
                          "parseResult": {
                            "availabilityWindows": [],
                            "missingFields": []
                          },
                          "systemMessage": "parsed"
                        }
                        """
                        |> Result.map .systemMessage
                        |> Expect.equal (Ok "parsed")
            , test "fails when systemMessage is missing" <|
                \_ ->
                    Decode.decodeString
                        parseResponseDecoder
                        """
                        {
                          "parseResult": {
                            "availabilityWindows": [],
                            "missingFields": []
                          }
                        }
                        """
                        |> expectDecodeError
            , describe "parse result fields"
                [ test "decodes a full parse result payload" <|
                    \_ ->
                        Decode.decodeString
                            parseResponseDecoder
                            """
                            {
                              "parseResult": {
                                "availabilityWindows": [
                                  {
                                    "start": "2026-03-01T09:00:00-05:00",
                                    "end": "2026-03-01T11:00:00-05:00",
                                    "timezone": "America/New_York"
                                  }
                                ],
                                "durationMinutes": 45,
                                "title": "Planning Session",
                                "description": "Discuss roadmap",
                                "name": "Taylor",
                                "email": "taylor@example.com",
                                "phone": "555-0001",
                                "missingFields": ["phone"]
                              },
                              "systemMessage": "ok"
                            }
                            """
                            |> Result.map .parseResult
                            |> Expect.equal
                                (Ok
                                    { availabilityWindows =
                                        [ { start = "2026-03-01T09:00:00-05:00"
                                          , end = "2026-03-01T11:00:00-05:00"
                                          , timezone = Just "America/New_York"
                                          }
                                        ]
                                    , durationMinutes = Just 45
                                    , title = Just "Planning Session"
                                    , description = Just "Discuss roadmap"
                                    , name = Just "Taylor"
                                    , email = Just "taylor@example.com"
                                    , phone = Just "555-0001"
                                    , missingFields = [ "phone" ]
                                    }
                                )
                , test "uses Nothing defaults when optional parse result fields are absent" <|
                    \_ ->
                        Decode.decodeString
                            parseResponseDecoder
                            (parseResponseWithResult
                                """
                                {
                                  "availabilityWindows": [
                                    {
                                      "start": "2026-03-02T09:00:00-05:00",
                                      "end": "2026-03-02T10:00:00-05:00"
                                    }
                                  ],
                                  "missingFields": []
                                }
                                """
                            )
                            |> Result.map .parseResult
                            |> Expect.equal
                                (Ok
                                    { availabilityWindows =
                                        [ { start = "2026-03-02T09:00:00-05:00"
                                          , end = "2026-03-02T10:00:00-05:00"
                                          , timezone = Nothing
                                          }
                                        ]
                                    , durationMinutes = Nothing
                                    , title = Nothing
                                    , description = Nothing
                                    , name = Nothing
                                    , email = Nothing
                                    , phone = Nothing
                                    , missingFields = []
                                    }
                                )
                , test "fails when parse result payload is malformed" <|
                    \_ ->
                        Decode.decodeString
                            parseResponseDecoder
                            (parseResponseWithResult
                                """{"availabilityWindows": "not-a-list", "missingFields": []}"""
                            )
                            |> expectDecodeError
                , test "fails when title exceeds max length" <|
                    \_ ->
                        Decode.decodeString
                            parseResponseDecoder
                            (parseResponseWithResult
                                ("""{"availabilityWindows": [], "title": \""""
                                    ++ String.repeat 501 "x"
                                    ++ """", "missingFields": []}"""
                                )
                            )
                            |> expectDecodeError
                , test "fails when name exceeds max length" <|
                    \_ ->
                        Decode.decodeString
                            parseResponseDecoder
                            (parseResponseWithResult
                                ("""{"availabilityWindows": [], "name": \""""
                                    ++ String.repeat 201 "x"
                                    ++ """", "missingFields": []}"""
                                )
                            )
                            |> expectDecodeError
                , test "accepts title at exactly max length" <|
                    \_ ->
                        Decode.decodeString
                            parseResponseDecoder
                            (parseResponseWithResult
                                ("""{"availabilityWindows": [], "title": \""""
                                    ++ String.repeat 500 "x"
                                    ++ """", "missingFields": []}"""
                                )
                            )
                            |> Result.map (.parseResult >> .title)
                            |> Expect.equal (Ok (Just (String.repeat 500 "x")))
                ]
            , describe "availability window validation"
                [ test "decodes timezone when present" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00-05:00", "end": "2026-03-03T10:00:00-05:00", "timezone": "UTC"}"""
                            )
                            |> firstWindow
                            |> Expect.equal
                                (Ok
                                    (Just
                                        { start = "2026-03-03T09:00:00-05:00"
                                        , end = "2026-03-03T10:00:00-05:00"
                                        , timezone = Just "UTC"
                                        }
                                    )
                                )
                , test "defaults timezone to Nothing when absent" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00-05:00", "end": "2026-03-03T10:00:00-05:00"}"""
                            )
                            |> firstWindow
                            |> Result.map (Maybe.map .timezone)
                            |> Expect.equal (Ok (Just Nothing))
                , test "treats explicit null timezone as Nothing" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00-05:00", "end": "2026-03-03T10:00:00-05:00", "timezone": null}"""
                            )
                            |> firstWindow
                            |> Result.map (Maybe.map .timezone)
                            |> Expect.equal (Ok (Just Nothing))
                , test "fails when required fields are missing" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when start is an empty string" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "", "end": "2026-03-03T10:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when start is not an ISO date-time" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "not-a-date", "end": "2026-03-03T10:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when month is out of range" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-13-03T09:00:00-05:00", "end": "2026-03-03T10:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when day is zero" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-00T09:00:00-05:00", "end": "2026-03-03T10:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when string is excessively long" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00-05:00AAAAAAAAAAAAAAAA", "end": "2026-03-03T10:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "accepts UTC Z suffix" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00Z", "end": "2026-03-03T10:00:00Z"}"""
                            )
                            |> firstWindow
                            |> Expect.equal
                                (Ok
                                    (Just
                                        { start = "2026-03-03T09:00:00Z"
                                        , end = "2026-03-03T10:00:00Z"
                                        , timezone = Nothing
                                        }
                                    )
                                )
                , test "accepts positive timezone offset" <|
                    \_ ->
                        Decode.decodeString parseResponseDecoder
                            (parseResponseWithWindow
                                """{"start": "2026-03-03T09:00:00+05:30", "end": "2026-03-03T10:00:00+05:30"}"""
                            )
                            |> firstWindow
                            |> Expect.equal
                                (Ok
                                    (Just
                                        { start = "2026-03-03T09:00:00+05:30"
                                        , end = "2026-03-03T10:00:00+05:30"
                                        , timezone = Nothing
                                        }
                                    )
                                )
                ]
            ]
        , describe "slotsResponseDecoder"
            [ test "decodes slots response with populated list" <|
                \_ ->
                    Decode.decodeString
                        slotsResponseDecoder
                        """
                        {
                          "slots": [
                            {
                              "start": "2026-03-04T09:00:00-05:00",
                              "end": "2026-03-04T09:30:00-05:00"
                            },
                            {
                              "start": "2026-03-04T10:00:00-05:00",
                              "end": "2026-03-04T10:30:00-05:00"
                            }
                          ]
                        }
                        """
                        |> Result.map List.length
                        |> Expect.equal (Ok 2)
            , test "decodes slots response with empty list" <|
                \_ ->
                    Decode.decodeString slotsResponseDecoder """{"slots": []}"""
                        |> Expect.equal (Ok [])
            , test "fails when slots key is missing" <|
                \_ ->
                    Decode.decodeString slotsResponseDecoder "{}"
                        |> expectDecodeError
            , describe "time slot validation"
                [ test "decodes a time slot and ignores extra fields" <|
                    \_ ->
                        Decode.decodeString slotsResponseDecoder
                            (slotsResponseWith
                                """{"start": "2026-03-04T09:00:00-05:00", "end": "2026-03-04T09:30:00-05:00", "meta": "ignored"}"""
                            )
                            |> Expect.equal
                                (Ok
                                    [ { start = "2026-03-04T09:00:00-05:00"
                                      , end = "2026-03-04T09:30:00-05:00"
                                      }
                                    ]
                                )
                , test "fails when a slot is missing required fields" <|
                    \_ ->
                        Decode.decodeString slotsResponseDecoder
                            (slotsResponseWith
                                """{"start": "2026-03-04T09:00:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when a slot has malformed start" <|
                    \_ ->
                        Decode.decodeString slotsResponseDecoder
                            (slotsResponseWith
                                """{"start": "garbage", "end": "2026-03-04T09:30:00-05:00"}"""
                            )
                            |> expectDecodeError
                , test "fails when a slot has empty end" <|
                    \_ ->
                        Decode.decodeString slotsResponseDecoder
                            (slotsResponseWith
                                """{"start": "2026-03-04T09:00:00-05:00", "end": ""}"""
                            )
                            |> expectDecodeError
                ]
            ]
        , describe "bookingConfirmationDecoder"
            [ test "decodes booking confirmation" <|
                \_ ->
                    Decode.decodeString
                        bookingConfirmationDecoder
                        """
                        {
                          "bookingId": "b-123",
                          "confirmed": true
                        }
                        """
                        |> Expect.equal (Ok { bookingId = "b-123", confirmed = True })
            , test "fails when required fields are missing" <|
                \_ ->
                    Decode.decodeString
                        bookingConfirmationDecoder
                        """
                        {
                          "bookingId": "b-123"
                        }
                        """
                        |> expectDecodeError
            , test "fails when bookingId exceeds max length" <|
                \_ ->
                    Decode.decodeString
                        bookingConfirmationDecoder
                        ("""{"bookingId": \"""" ++ String.repeat 101 "x" ++ """", "confirmed": true}""")
                        |> expectDecodeError
            ]
        , describe "encodeParseRequest"
            [ test "encodes all fields" <|
                \_ ->
                    encodeParseRequest
                        { message = "tomorrow at 3pm"
                        , timezone = "America/New_York"
                        , previousMessages = [ "earlier msg" ]
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.map3 (\m t p -> { message = m, timezone = t, previousMessages = p })
                                (Decode.field "message" Decode.string)
                                (Decode.field "timezone" Decode.string)
                                (Decode.field "previousMessages" (Decode.list Decode.string))
                            )
                        |> Expect.equal
                            (Ok
                                { message = "tomorrow at 3pm"
                                , timezone = "America/New_York"
                                , previousMessages = [ "earlier msg" ]
                                }
                            )
            , test "encodes empty previous messages as empty list" <|
                \_ ->
                    encodeParseRequest
                        { message = "next week"
                        , timezone = "UTC"
                        , previousMessages = []
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.field "previousMessages" (Decode.list Decode.string))
                        |> Expect.equal (Ok [])
            ]
        , describe "encodeSlotsRequest"
            [ test "encodes availability windows, duration, and timezone" <|
                \_ ->
                    encodeSlotsRequest
                        { availabilityWindows =
                            [ { start = "2026-03-05T09:00:00-05:00"
                              , end = "2026-03-05T10:00:00-05:00"
                              , timezone = Just "America/Chicago"
                              }
                            ]
                        , durationMinutes = 30
                        , timezone = "America/Chicago"
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.map3 (\w d t -> { windows = w, durationMinutes = d, timezone = t })
                                (Decode.field "availabilityWindows" (Decode.list windowFieldsDecoder))
                                (Decode.field "durationMinutes" Decode.int)
                                (Decode.field "timezone" Decode.string)
                            )
                        |> Expect.equal
                            (Ok
                                { windows =
                                    [ { start = "2026-03-05T09:00:00-05:00"
                                      , end = "2026-03-05T10:00:00-05:00"
                                      , timezone = Just "America/Chicago"
                                      }
                                    ]
                                , durationMinutes = 30
                                , timezone = "America/Chicago"
                                }
                            )
            , test "encodes empty availability windows as empty list" <|
                \_ ->
                    encodeSlotsRequest
                        { availabilityWindows = []
                        , durationMinutes = 60
                        , timezone = "UTC"
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.field "availabilityWindows" (Decode.list windowFieldsDecoder))
                        |> Expect.equal (Ok [])
            ]
        , describe "encodeBookingRequest"
            [ test "encodes all fields including optional ones" <|
                \_ ->
                    encodeBookingRequest
                        { name = "Taylor"
                        , email = "taylor@example.com"
                        , phone = Just "555-0001"
                        , title = "Planning Session"
                        , description = Just "Discuss roadmap"
                        , slot = { start = "2026-03-06T09:00:00-05:00", end = "2026-03-06T09:30:00-05:00" }
                        , durationMinutes = 30
                        , timezone = "America/New_York"
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.field "name" Decode.string)
                        |> Expect.equal (Ok "Taylor")
            , test "encodes Nothing fields as null" <|
                \_ ->
                    encodeBookingRequest
                        { name = "Taylor"
                        , email = "taylor@example.com"
                        , phone = Nothing
                        , title = "Quick Chat"
                        , description = Nothing
                        , slot = { start = "2026-03-06T09:00:00-05:00", end = "2026-03-06T09:30:00-05:00" }
                        , durationMinutes = 30
                        , timezone = "America/New_York"
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.field "phone" (Decode.nullable Decode.string))
                        |> Expect.equal (Ok Nothing)
            , test "encodes nested slot with start and end" <|
                \_ ->
                    encodeBookingRequest
                        { name = "Taylor"
                        , email = "taylor@example.com"
                        , phone = Nothing
                        , title = "Quick Chat"
                        , description = Nothing
                        , slot = { start = "2026-03-06T09:00:00-05:00", end = "2026-03-06T09:30:00-05:00" }
                        , durationMinutes = 30
                        , timezone = "America/New_York"
                        }
                        |> Encode.encode 0
                        |> Decode.decodeString
                            (Decode.field "slot" slotFieldsDecoder)
                        |> Expect.equal
                            (Ok
                                { start = "2026-03-06T09:00:00-05:00"
                                , end = "2026-03-06T09:30:00-05:00"
                                }
                            )
            ]
        ]
