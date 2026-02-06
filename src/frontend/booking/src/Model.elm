module Model exposing (Flags, Model, init)

import Types exposing (AvailabilityWindow, BookingConfirmation, DurationChoice, FormStep(..), TimeSlot)


type alias Flags =
    { timezone : String
    }


type alias Model =
    { currentStep : FormStep
    , title : String
    , durationChoice : Maybe DurationChoice
    , customDuration : String
    , availabilityText : String
    , parsedWindows : List AvailabilityWindow
    , slots : List TimeSlot
    , selectedSlot : Maybe TimeSlot
    , name : String
    , email : String
    , phone : String
    , timezone : String
    , timezoneDropdownOpen : Bool
    , loading : Bool
    , error : Maybe String
    , bookingResult : Maybe BookingConfirmation
    }


validTimezone : String -> String
validTimezone tz =
    if String.contains "/" tz && String.length tz < 50 then
        tz

    else
        "UTC"


init : Flags -> ( Model, Cmd msg )
init flags =
    ( { currentStep = TitleStep
      , title = ""
      , durationChoice = Nothing
      , customDuration = ""
      , availabilityText = ""
      , parsedWindows = []
      , slots = []
      , selectedSlot = Nothing
      , name = ""
      , email = ""
      , phone = ""
      , timezone = validTimezone flags.timezone
      , timezoneDropdownOpen = False
      , loading = False
      , error = Nothing
      , bookingResult = Nothing
      }
    , Cmd.none
    )
