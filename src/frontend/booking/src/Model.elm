module Model exposing (Flags, Model, emptyParseResult, init, mergeResult)

import Types exposing (BookingConfirmation, ChatMessage, ConversationPhase(..), MessageRole(..), ParseResult, TimeSlot)


type alias Flags =
    { timezone : String
    }


type alias Model =
    { messages : List ChatMessage
    , inputText : String
    , phase : ConversationPhase
    , accumulated : ParseResult
    , slots : List TimeSlot
    , selectedSlot : Maybe TimeSlot
    , bookingResult : Maybe BookingConfirmation
    , timezone : String
    , loading : Bool
    , error : Maybe String
    }


emptyParseResult : ParseResult
emptyParseResult =
    { availabilityWindows = []
    , durationMinutes = Nothing
    , title = Nothing
    , description = Nothing
    , name = Nothing
    , email = Nothing
    , phone = Nothing
    , missingFields = [ "availability", "duration", "title", "name", "email" ]
    }


validTimezone : String -> String
validTimezone tz =
    if String.contains "/" tz && String.length tz < 50 then
        tz

    else
        "UTC"


init : Flags -> ( Model, Cmd msg )
init flags =
    ( { messages =
            [ { role = System
              , content = "Hi! I'd like to schedule a meeting. When are you available? Please also let me know the meeting topic, duration, your name, and email."
              }
            ]
      , inputText = ""
      , phase = Chatting
      , accumulated = emptyParseResult
      , slots = []
      , selectedSlot = Nothing
      , bookingResult = Nothing
      , timezone = validTimezone flags.timezone
      , loading = False
      , error = Nothing
      }
    , Cmd.none
    )


orElse : Maybe a -> Maybe a -> Maybe a
orElse fallback primary =
    case primary of
        Just _ ->
            primary

        Nothing ->
            fallback


mergeResult : ParseResult -> ParseResult -> ParseResult
mergeResult acc new =
    { availabilityWindows =
        if List.isEmpty new.availabilityWindows then
            acc.availabilityWindows

        else
            new.availabilityWindows
    , durationMinutes = orElse acc.durationMinutes new.durationMinutes
    , title = orElse acc.title new.title
    , description = orElse acc.description new.description
    , name = orElse acc.name new.name
    , email = orElse acc.email new.email
    , phone = orElse acc.phone new.phone
    , missingFields = new.missingFields
    }
