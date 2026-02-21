module ApiCodecs exposing
    ( bookingConfirmationDecoder
    , csrfTokenDecoder
    , encodeAvailabilityWindow
    , encodeBookingRequest
    , encodeParseRequest
    , encodeSlotsRequest
    , parseResponseDecoder
    , slotsResponseDecoder
    )

import Json.Decode as Decode exposing (Decoder)
import Json.Decode.Pipeline exposing (optional, required)
import Json.Encode as Encode
import Model
import Types exposing (AvailabilityWindow, BookingConfirmation, ParseResponse, ParseResult, TimeSlot)


{-| Decode a string that does not exceed `maxLen` characters.

Guards against oversized payloads from a compromised or buggy server
causing client-side memory or rendering issues.

-}
boundedString : Int -> Decoder String
boundedString maxLen =
    Decode.string
        |> Decode.andThen
            (\s ->
                if String.length s <= maxLen then
                    Decode.succeed s

                else
                    Decode.fail ("String exceeds max length of " ++ String.fromInt maxLen)
            )


{-| Decode a list that does not exceed `maxLen` elements.
-}
boundedList : Int -> Decoder a -> Decoder (List a)
boundedList maxLen itemDecoder =
    Decode.list itemDecoder
        |> Decode.andThen
            (\items ->
                if List.length items <= maxLen then
                    Decode.succeed items

                else
                    Decode.fail ("List exceeds max length of " ++ String.fromInt maxLen)
            )


{-| Decode a string that looks like an ISO 8601 date-time.

Validates structural requirements that downstream code depends on:
the `DateFormat` module does positional slicing (chars 0-9 for date,
11-15 for time), so we verify the `T` separator and numeric date
components at decode time rather than rendering garbage later.

Accepts formats like:

    "2026-02-09T09:00:00-05:00"

    "2026-02-09T14:30:00Z"

    "2026-02-09T09:00:00+05:30"

-}
isoDateTimeString : Decoder String
isoDateTimeString =
    Decode.string
        |> Decode.andThen
            (\s ->
                let
                    len =
                        String.length s

                    datePart =
                        String.left 10 s

                    dateParts =
                        String.split "-" datePart

                    hasT =
                        String.slice 10 11 s == "T"
                in
                if len < 19 || len > 35 then
                    Decode.fail ("Invalid date-time string: expected 19–35 characters, got " ++ String.fromInt len)

                else if not hasT then
                    Decode.fail "Invalid date-time string: missing T separator at position 10"

                else
                    case dateParts of
                        [ yearStr, monthStr, dayStr ] ->
                            case ( String.toInt yearStr, String.toInt monthStr, String.toInt dayStr ) of
                                ( Just _, Just month, Just day ) ->
                                    if month >= 1 && month <= 12 && day >= 1 && day <= 31 then
                                        Decode.succeed s

                                    else
                                        Decode.fail "Invalid date-time string: month or day out of range"

                                _ ->
                                    Decode.fail "Invalid date-time string: non-numeric date components"

                        _ ->
                            Decode.fail "Invalid date-time string: date portion must be YYYY-MM-DD"
            )


csrfTokenDecoder : Decoder String
csrfTokenDecoder =
    Decode.field "token" (boundedString 200)
        |> Decode.andThen
            (\raw ->
                case Model.validCsrfToken raw of
                    Just token ->
                        Decode.succeed token

                    Nothing ->
                        Decode.fail "Invalid CSRF token format"
            )


parseResponseDecoder : Decoder ParseResponse
parseResponseDecoder =
    Decode.succeed ParseResponse
        |> required "parseResult" parseResultDecoder
        |> required "systemMessage" (boundedString 5000)


parseResultDecoder : Decoder ParseResult
parseResultDecoder =
    Decode.succeed ParseResult
        |> required "availabilityWindows" (boundedList 100 availabilityWindowDecoder)
        |> optional "durationMinutes" (Decode.nullable Decode.int) Nothing
        |> optional "title" (Decode.nullable (boundedString 500)) Nothing
        |> optional "description" (Decode.nullable (boundedString 2000)) Nothing
        |> optional "name" (Decode.nullable (boundedString 200)) Nothing
        |> optional "email" (Decode.nullable (boundedString 254)) Nothing
        |> optional "phone" (Decode.nullable (boundedString 50)) Nothing
        |> required "missingFields" (boundedList 20 (boundedString 100))


availabilityWindowDecoder : Decoder AvailabilityWindow
availabilityWindowDecoder =
    Decode.succeed AvailabilityWindow
        |> required "start" isoDateTimeString
        |> required "end" isoDateTimeString
        |> optional "timezone" (Decode.nullable (boundedString 100)) Nothing


encodeMaybe : (a -> Encode.Value) -> Maybe a -> Encode.Value
encodeMaybe encoder maybeValue =
    case maybeValue of
        Just value ->
            encoder value

        Nothing ->
            Encode.null


encodeAvailabilityWindow : AvailabilityWindow -> Encode.Value
encodeAvailabilityWindow window =
    Encode.object
        [ ( "start", Encode.string window.start )
        , ( "end", Encode.string window.end )
        , ( "timezone", encodeMaybe Encode.string window.timezone )
        ]


encodeBookingRequest :
    { name : String
    , email : String
    , phone : Maybe String
    , title : String
    , description : Maybe String
    , slot : TimeSlot
    , durationMinutes : Int
    , timezone : String
    }
    -> Encode.Value
encodeBookingRequest req =
    Encode.object
        [ ( "name", Encode.string req.name )
        , ( "email", Encode.string req.email )
        , ( "phone", encodeMaybe Encode.string req.phone )
        , ( "title", Encode.string req.title )
        , ( "description", encodeMaybe Encode.string req.description )
        , ( "slot"
          , Encode.object
                [ ( "start", Encode.string req.slot.start )
                , ( "end", Encode.string req.slot.end )
                ]
          )
        , ( "durationMinutes", Encode.int req.durationMinutes )
        , ( "timezone", Encode.string req.timezone )
        ]


encodeParseRequest :
    { message : String
    , timezone : String
    , previousMessages : List String
    }
    -> Encode.Value
encodeParseRequest req =
    Encode.object
        [ ( "message", Encode.string req.message )
        , ( "timezone", Encode.string req.timezone )
        , ( "previousMessages", Encode.list Encode.string req.previousMessages )
        ]


encodeSlotsRequest :
    { availabilityWindows : List AvailabilityWindow
    , durationMinutes : Int
    , timezone : String
    }
    -> Encode.Value
encodeSlotsRequest req =
    Encode.object
        [ ( "availabilityWindows"
          , Encode.list encodeAvailabilityWindow req.availabilityWindows
          )
        , ( "durationMinutes", Encode.int req.durationMinutes )
        , ( "timezone", Encode.string req.timezone )
        ]


slotsResponseDecoder : Decoder (List TimeSlot)
slotsResponseDecoder =
    Decode.field "slots" (boundedList 500 timeSlotDecoder)


timeSlotDecoder : Decoder TimeSlot
timeSlotDecoder =
    Decode.succeed TimeSlot
        |> required "start" isoDateTimeString
        |> required "end" isoDateTimeString


bookingConfirmationDecoder : Decoder BookingConfirmation
bookingConfirmationDecoder =
    Decode.succeed BookingConfirmation
        |> required "bookingId" (boundedString 100)
        |> required "confirmed" Decode.bool
