module Api exposing (bookSlot, fetchCsrfToken, fetchSlots, parseMessage)

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
import Http
import Json.Encode as Encode
import Types exposing (AvailabilityWindow, BookingConfirmation, ParseResponse, TimeSlot)


postWithCsrf : String -> String -> Encode.Value -> Http.Expect msg -> Cmd msg
postWithCsrf csrfToken url body expect =
    Http.request
        { method = "POST"
        , headers = [ Http.header "X-CSRF-Token" csrfToken ]
        , url = url
        , body = Http.jsonBody body
        , expect = expect
        , timeout = Nothing
        , tracker = Nothing
        }


fetchCsrfToken : (Result Http.Error String -> msg) -> Cmd msg
fetchCsrfToken toMsg =
    Http.get
        { url = "/api/csrf-token"
        , expect = Http.expectJson toMsg csrfTokenDecoder
        }



-- Parse endpoint


parseMessage : String -> String -> String -> List String -> (Result Http.Error ParseResponse -> msg) -> Cmd msg
parseMessage csrfToken message timezone previousMessages toMsg =
    postWithCsrf
        csrfToken
        "/api/parse"
        (encodeParseRequest
            { message = message
            , timezone = timezone
            , previousMessages = previousMessages
            }
        )
        (Http.expectJson toMsg parseResponseDecoder)



-- Slots endpoint


fetchSlots : String -> List AvailabilityWindow -> Int -> String -> (Result Http.Error (List TimeSlot) -> msg) -> Cmd msg
fetchSlots csrfToken windows durationMinutes timezone toMsg =
    postWithCsrf
        csrfToken
        "/api/slots"
        (encodeSlotsRequest
            { availabilityWindows = windows
            , durationMinutes = durationMinutes
            , timezone = timezone
            }
        )
        (Http.expectJson toMsg slotsResponseDecoder)



-- Book endpoint


bookSlot :
    String
    ->
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
bookSlot csrfToken req toMsg =
    postWithCsrf
        csrfToken
        "/api/book"
        (encodeBookingRequest req)
        (Http.expectJson toMsg bookingConfirmationDecoder)
