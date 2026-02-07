module Update exposing (Msg(..), focusElement, update)

import Api
import Browser.Dom as Dom
import Http
import Model exposing (Model)
import Task
import Types exposing (BookingConfirmation, DurationChoice(..), FormStep(..), ParseResponse, TimeSlot)


type Msg
    = TitleUpdated String
    | TitleStepCompleted
    | DurationPresetSelected Int
    | CustomDurationSelected
    | CustomDurationUpdated String
    | DurationStepCompleted
    | AvailabilityTextUpdated String
    | AvailabilityStepCompleted
    | ParseResponseReceived (Result Http.Error ParseResponse)
    | AvailabilityWindowsConfirmed
    | SlotsReceived (Result Http.Error (List TimeSlot))
    | SlotSelected TimeSlot
    | NameUpdated String
    | EmailUpdated String
    | PhoneUpdated String
    | ContactInfoStepCompleted
    | BookingConfirmed
    | BookingResultReceived (Result Http.Error BookingConfirmation)
    | TimezoneChanged String
    | TimezoneDropdownToggled
    | BackStepClicked
    | NoOp


focusElement : String -> Cmd Msg
focusElement elementId =
    Task.attempt (\_ -> NoOp) (Dom.focus elementId)


isValidEmail : String -> Bool
isValidEmail email =
    let
        parts =
            String.split "@" email
    in
    case parts of
        [ local, domain ] ->
            not (String.isEmpty local)
                && String.contains "." domain
                && not (String.endsWith "." domain)

        _ ->
            False


getDurationMinutes : Model -> Int
getDurationMinutes model =
    case model.durationChoice of
        Just (Preset mins) ->
            mins

        Just Custom ->
            String.toInt model.customDuration
                |> Maybe.withDefault 30

        Nothing ->
            30


previousStep : FormStep -> FormStep
previousStep step =
    case step of
        TitleStep ->
            TitleStep

        DurationStep ->
            TitleStep

        AvailabilityStep ->
            DurationStep

        AvailabilityConfirmStep ->
            AvailabilityStep

        SlotSelectionStep ->
            AvailabilityConfirmStep

        ContactInfoStep ->
            SlotSelectionStep

        ConfirmationStep ->
            ContactInfoStep

        CompleteStep ->
            CompleteStep


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        TitleUpdated text ->
            ( { model | title = text }, Cmd.none )

        TitleStepCompleted ->
            if String.isEmpty (String.trim model.title) then
                ( { model | error = Just "Please enter a meeting title." }, Cmd.none )

            else
                ( { model | currentStep = DurationStep, error = Nothing }, focusElement "duration-15" )

        DurationPresetSelected mins ->
            ( { model | durationChoice = Just (Preset mins) }, Cmd.none )

        CustomDurationSelected ->
            ( { model | durationChoice = Just Custom }, focusElement "custom-duration-input" )

        CustomDurationUpdated text ->
            ( { model | customDuration = text }, Cmd.none )

        DurationStepCompleted ->
            case model.durationChoice of
                Nothing ->
                    ( { model | error = Just "Please select a duration." }, Cmd.none )

                Just Custom ->
                    case String.toInt model.customDuration of
                        Just mins ->
                            if mins > 0 && mins <= 480 then
                                ( { model | currentStep = AvailabilityStep, error = Nothing }, focusElement "availability-input" )

                            else
                                ( { model | error = Just "Duration must be between 1 and 480 minutes." }, Cmd.none )

                        Nothing ->
                            ( { model | error = Just "Please enter a valid number of minutes." }, Cmd.none )

                Just (Preset _) ->
                    ( { model | currentStep = AvailabilityStep, error = Nothing }, focusElement "availability-input" )

        AvailabilityTextUpdated text ->
            ( { model | availabilityText = text }, Cmd.none )

        AvailabilityStepCompleted ->
            if String.isEmpty (String.trim model.availabilityText) then
                ( { model | error = Just "Please describe your availability." }, Cmd.none )

            else
                ( { model | loading = True, error = Nothing }
                , Api.parseMessage
                    model.availabilityText
                    model.timezone
                    []
                    ParseResponseReceived
                )

        ParseResponseReceived (Ok response) ->
            let
                windows =
                    response.parseResult.availabilityWindows
            in
            if List.isEmpty windows then
                ( { model
                    | loading = False
                    , error = Just "Could not parse availability windows. Please try describing your availability differently."
                  }
                , Cmd.none
                )

            else
                ( { model
                    | parsedWindows = windows
                    , loading = False
                    , currentStep = AvailabilityConfirmStep
                    , error = Nothing
                  }
                , focusElement "confirm-availability-btn"
                )

        AvailabilityWindowsConfirmed ->
            let
                duration =
                    getDurationMinutes model
            in
            ( { model | loading = True, error = Nothing }
            , Api.fetchSlots model.parsedWindows duration model.timezone SlotsReceived
            )

        ParseResponseReceived (Err err) ->
            let
                errorMsg =
                    case err of
                        Http.BadBody body ->
                            "Failed to parse response: " ++ body

                        Http.NetworkError ->
                            "Network error. Please try again."

                        Http.BadStatus status ->
                            "Server error (" ++ String.fromInt status ++ ")"

                        Http.Timeout ->
                            "Request timed out. Please try again."

                        Http.BadUrl url ->
                            "Bad URL: " ++ url
            in
            ( { model | loading = False, error = Just errorMsg }, Cmd.none )

        SlotsReceived (Ok slots) ->
            ( { model
                | slots = slots
                , loading = False
                , currentStep = SlotSelectionStep
                , error = Nothing
              }
            , focusElement "slot-0"
            )

        SlotsReceived (Err _) ->
            ( { model
                | loading = False
                , error = Just "Failed to load available slots. Please try again."
              }
            , Cmd.none
            )

        SlotSelected slot ->
            ( { model
                | selectedSlot = Just slot
                , currentStep = ContactInfoStep
                , error = Nothing
              }
            , focusElement "name-input"
            )

        NameUpdated text ->
            ( { model | name = text }, Cmd.none )

        EmailUpdated text ->
            ( { model | email = text }, Cmd.none )

        PhoneUpdated text ->
            ( { model | phone = text }, Cmd.none )

        ContactInfoStepCompleted ->
            if String.isEmpty (String.trim model.name) then
                ( { model | error = Just "Please enter your name." }, Cmd.none )

            else if String.isEmpty (String.trim model.email) then
                ( { model | error = Just "Please enter your email address." }, Cmd.none )

            else if not (isValidEmail (String.trim model.email)) then
                ( { model | error = Just "Please enter a valid email address." }, Cmd.none )

            else
                ( { model | currentStep = ConfirmationStep, error = Nothing }, focusElement "confirm-booking-btn" )

        BookingConfirmed ->
            case model.selectedSlot of
                Just slot ->
                    ( { model | loading = True, error = Nothing }
                    , Api.bookSlot
                        { name = String.trim model.name
                        , email = String.trim model.email
                        , phone =
                            let
                                trimmed =
                                    String.trim model.phone
                            in
                            if String.isEmpty trimmed then
                                Nothing

                            else
                                Just trimmed
                        , title = String.trim model.title
                        , description = Nothing
                        , slot = slot
                        , durationMinutes = getDurationMinutes model
                        , timezone = model.timezone
                        }
                        BookingResultReceived
                    )

                Nothing ->
                    ( model, Cmd.none )

        BookingResultReceived (Ok confirmation) ->
            ( { model
                | bookingResult = Just confirmation
                , currentStep = CompleteStep
                , loading = False
              }
            , Cmd.none
            )

        BookingResultReceived (Err _) ->
            ( { model
                | loading = False
                , error = Just "Failed to confirm booking. Please try again."
              }
            , Cmd.none
            )

        TimezoneChanged tz ->
            ( { model | timezone = tz, timezoneDropdownOpen = False }, Cmd.none )

        TimezoneDropdownToggled ->
            ( { model | timezoneDropdownOpen = not model.timezoneDropdownOpen }, Cmd.none )

        BackStepClicked ->
            let
                prev =
                    previousStep model.currentStep

                clearedModel =
                    case model.currentStep of
                        SlotSelectionStep ->
                            { model | slots = [], selectedSlot = Nothing }

                        AvailabilityConfirmStep ->
                            { model | parsedWindows = [] }

                        _ ->
                            model

                focusCmd =
                    case prev of
                        TitleStep ->
                            focusElement "title-input"

                        DurationStep ->
                            focusElement "duration-15"

                        AvailabilityStep ->
                            focusElement "availability-input"

                        AvailabilityConfirmStep ->
                            focusElement "confirm-availability-btn"

                        SlotSelectionStep ->
                            focusElement "slot-0"

                        ContactInfoStep ->
                            focusElement "name-input"

                        ConfirmationStep ->
                            focusElement "confirm-booking-btn"

                        _ ->
                            Cmd.none
            in
            ( { clearedModel | currentStep = prev, error = Nothing }, focusCmd )

        NoOp ->
            ( model, Cmd.none )
