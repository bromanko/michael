module Model exposing (Flags, Model, init, validCsrfToken, validTimezone)

import Types exposing (AvailabilityWindow, BookingConfirmation, FormStep(..), TimeSlot)


type alias Flags =
    { timezone : String
    , csrfToken : String
    }


type alias Model =
    { currentStep : FormStep
    , title : String
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
    , csrfToken : Maybe String
    , csrfRefreshAttempted : Bool
    }


validTimezone : String -> String
validTimezone tz =
    if String.contains "/" tz && String.length tz < 50 then
        tz

    else
        "UTC"


validCsrfToken : String -> Maybe String
validCsrfToken token =
    let
        isHex : Char -> Bool
        isHex c =
            Char.isDigit c
                || List.member c [ 'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F' ]

        isPositiveInt : String -> Bool
        isPositiveInt text =
            case String.toInt text of
                Just n ->
                    n > 0

                Nothing ->
                    False
    in
    case String.split ":" token of
        [ issuedAt, nonce, signature ] ->
            if
                isPositiveInt issuedAt
                    && String.length nonce
                    == 32
                    && String.all isHex nonce
                    && String.length signature
                    == 64
                    && String.all isHex signature
            then
                Just token

            else
                Nothing

        _ ->
            Nothing


init : Flags -> ( Model, Cmd msg )
init flags =
    ( { currentStep = TitleStep
      , title = ""
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
      , csrfToken = validCsrfToken flags.csrfToken
      , csrfRefreshAttempted = False
      }
    , Cmd.none
    )
