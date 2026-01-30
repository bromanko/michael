module Types exposing
    ( AvailabilityWindow
    , BookingConfirmation
    , ChatMessage
    , ConversationPhase(..)
    , MessageRole(..)
    , ParseResponse
    , ParseResult
    , TimeSlot
    )


type ConversationPhase
    = Chatting
    | ConfirmingParse
    | SelectingSlot
    | ConfirmingBooking
    | BookingComplete


type MessageRole
    = User
    | System


type alias ChatMessage =
    { role : MessageRole
    , content : String
    }


type alias AvailabilityWindow =
    { start : String
    , end : String
    , timezone : Maybe String
    }


type alias ParseResult =
    { availabilityWindows : List AvailabilityWindow
    , durationMinutes : Maybe Int
    , title : Maybe String
    , description : Maybe String
    , name : Maybe String
    , email : Maybe String
    , phone : Maybe String
    , missingFields : List String
    }


type alias TimeSlot =
    { start : String
    , end : String
    }


type alias BookingConfirmation =
    { bookingId : String
    , confirmed : Bool
    }


type alias ParseResponse =
    { parseResult : ParseResult
    , systemMessage : String
    }
