module Api exposing (bookSlot, fetchSlots, parseMessage)

import Http
import Json.Decode as Decode exposing (Decoder)
import Json.Decode.Pipeline exposing (optional, required)
import Json.Encode as Encode
import Types exposing (AvailabilityWindow, BookingConfirmation, ParseResponse, ParseResult, TimeSlot)



-- Parse endpoint


parseMessage : String -> String -> List String -> (Result Http.Error ParseResponse -> msg) -> Cmd msg
parseMessage message timezone previousMessages toMsg =
    Http.post
        { url = "/api/parse"
        , body =
            Http.jsonBody
                (Encode.object
                    [ ( "message", Encode.string message )
                    , ( "timezone", Encode.string timezone )
                    , ( "previousMessages"
                      , Encode.list Encode.string previousMessages
                      )
                    ]
                )
        , expect = Http.expectJson toMsg parseResponseDecoder
        }


parseResponseDecoder : Decoder ParseResponse
parseResponseDecoder =
    Decode.succeed ParseResponse
        |> required "parseResult" parseResultDecoder
        |> required "systemMessage" Decode.string


parseResultDecoder : Decoder ParseResult
parseResultDecoder =
    Decode.succeed ParseResult
        |> required "availabilityWindows" (Decode.list availabilityWindowDecoder)
        |> optional "durationMinutes" (Decode.nullable Decode.int) Nothing
        |> optional "title" (Decode.nullable Decode.string) Nothing
        |> optional "description" (Decode.nullable Decode.string) Nothing
        |> optional "name" (Decode.nullable Decode.string) Nothing
        |> optional "email" (Decode.nullable Decode.string) Nothing
        |> optional "phone" (Decode.nullable Decode.string) Nothing
        |> required "missingFields" (Decode.list Decode.string)


availabilityWindowDecoder : Decoder AvailabilityWindow
availabilityWindowDecoder =
    Decode.succeed AvailabilityWindow
        |> required "start" Decode.string
        |> required "end" Decode.string
        |> optional "timezone" (Decode.nullable Decode.string) Nothing



-- Slots endpoint


fetchSlots : List AvailabilityWindow -> Int -> String -> (Result Http.Error (List TimeSlot) -> msg) -> Cmd msg
fetchSlots windows durationMinutes timezone toMsg =
    Http.post
        { url = "/api/slots"
        , body =
            Http.jsonBody
                (Encode.object
                    [ ( "availabilityWindows"
                      , Encode.list encodeAvailabilityWindow windows
                      )
                    , ( "durationMinutes", Encode.int durationMinutes )
                    , ( "timezone", Encode.string timezone )
                    ]
                )
        , expect = Http.expectJson toMsg slotsResponseDecoder
        }


encodeAvailabilityWindow : AvailabilityWindow -> Encode.Value
encodeAvailabilityWindow w =
    Encode.object
        [ ( "start", Encode.string w.start )
        , ( "end", Encode.string w.end )
        , ( "timezone"
          , case w.timezone of
                Just tz ->
                    Encode.string tz

                Nothing ->
                    Encode.null
          )
        ]


slotsResponseDecoder : Decoder (List TimeSlot)
slotsResponseDecoder =
    Decode.field "slots" (Decode.list timeSlotDecoder)


timeSlotDecoder : Decoder TimeSlot
timeSlotDecoder =
    Decode.succeed TimeSlot
        |> required "start" Decode.string
        |> required "end" Decode.string



-- Book endpoint


bookSlot :
    { name : String
    , email : String
    , phone : Maybe String
    , title : String
    , description : Maybe String
    , slot : TimeSlot
    , durationMinutes : Int
    , timezone : String
    }
    -> (Result Http.Error BookingConfirmation -> msg)
    -> Cmd msg
bookSlot req toMsg =
    Http.post
        { url = "/api/book"
        , body =
            Http.jsonBody
                (Encode.object
                    [ ( "name", Encode.string req.name )
                    , ( "email", Encode.string req.email )
                    , ( "phone"
                      , case req.phone of
                            Just p ->
                                Encode.string p

                            Nothing ->
                                Encode.null
                      )
                    , ( "title", Encode.string req.title )
                    , ( "description"
                      , case req.description of
                            Just d ->
                                Encode.string d

                            Nothing ->
                                Encode.null
                      )
                    , ( "slot"
                      , Encode.object
                            [ ( "start", Encode.string req.slot.start )
                            , ( "end", Encode.string req.slot.end )
                            ]
                      )
                    , ( "durationMinutes", Encode.int req.durationMinutes )
                    , ( "timezone", Encode.string req.timezone )
                    ]
                )
        , expect = Http.expectJson toMsg bookingConfirmationDecoder
        }


bookingConfirmationDecoder : Decoder BookingConfirmation
bookingConfirmationDecoder =
    Decode.succeed BookingConfirmation
        |> required "bookingId" Decode.string
        |> required "confirmed" Decode.bool
