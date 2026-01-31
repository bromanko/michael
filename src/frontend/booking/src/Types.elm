module Types exposing
    ( AvailabilityWindow
    , BookingConfirmation
    , DurationChoice(..)
    , FormStep(..)
    , ParseResponse
    , ParseResult
    , TimeSlot
    )


type FormStep
    = TitleStep
    | DurationStep
    | AvailabilityStep
    | SlotSelectionStep
    | ContactInfoStep
    | ConfirmationStep
    | CompleteStep


type DurationChoice
    = Preset Int
    | Custom


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
