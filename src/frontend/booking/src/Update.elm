module Update exposing (Msg(..), focusElement, update)

import Api
import Browser.Dom as Dom
import Http
import Model exposing (Model, validCsrfToken)
import Task
import Types exposing (BookingConfirmation, FormStep(..), ParseResponse, TimeSlot)


type Msg
    = TitleUpdated String
    | TitleStepCompleted
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
    | CsrfTokenRefreshedForParse (Result Http.Error String)
    | CsrfTokenRefreshedForSlots (Result Http.Error String)
    | CsrfTokenRefreshedForBook (Result Http.Error String)
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


defaultDurationMinutes : Int
defaultDurationMinutes =
    30


makeBookRequest :
    Model
    ->
        Maybe
            { name : String
            , email : String
            , phone : Maybe String
            , title : String
            , description : Maybe String
            , slot : TimeSlot
            , durationMinutes : Int
            , timezone : String
            }
makeBookRequest model =
    case model.selectedSlot of
        Just slot ->
            Just
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
                , durationMinutes = defaultDurationMinutes
                , timezone = model.timezone
                }

        Nothing ->
            Nothing


withCsrfToken : Model -> (String -> ( Model, Cmd Msg )) -> ( Model, Cmd Msg )
withCsrfToken model onToken =
    case model.csrfToken of
        Just token ->
            onToken token

        Nothing ->
            ( { model
                | loading = False
                , error = Just "Failed to initialize booking session. Please refresh and try again."
                , csrfRefreshAttempted = False
              }
            , Cmd.none
            )


refreshCsrfForParse : Model -> ( Model, Cmd Msg )
refreshCsrfForParse model =
    ( { model | loading = True, error = Nothing, csrfRefreshAttempted = True }
    , Api.fetchCsrfToken CsrfTokenRefreshedForParse
    )


refreshCsrfForSlots : Model -> ( Model, Cmd Msg )
refreshCsrfForSlots model =
    ( { model | loading = True, error = Nothing, csrfRefreshAttempted = True }
    , Api.fetchCsrfToken CsrfTokenRefreshedForSlots
    )


refreshCsrfForBook : Model -> ( Model, Cmd Msg )
refreshCsrfForBook model =
    ( { model | loading = True, error = Nothing, csrfRefreshAttempted = True }
    , Api.fetchCsrfToken CsrfTokenRefreshedForBook
    )


retryParseWithToken : String -> Model -> Cmd Msg
retryParseWithToken csrfToken model =
    Api.parseMessage csrfToken model.availabilityText model.timezone [] ParseResponseReceived


retrySlotsWithToken : String -> Model -> Cmd Msg
retrySlotsWithToken csrfToken model =
    Api.fetchSlots csrfToken model.parsedWindows defaultDurationMinutes model.timezone SlotsReceived


retryBookWithToken : String -> Model -> Cmd Msg
retryBookWithToken csrfToken model =
    case makeBookRequest model of
        Just req ->
            Api.bookSlot csrfToken req BookingResultReceived

        Nothing ->
            Cmd.none


handleCsrfRefreshResult : (String -> Model -> Cmd Msg) -> Result Http.Error String -> Model -> ( Model, Cmd Msg )
handleCsrfRefreshResult retryCmd result model =
    case result of
        Ok token ->
            case validCsrfToken token of
                Just validToken ->
                    let
                        updatedModel =
                            { model | csrfToken = Just validToken, loading = True, error = Nothing }
                    in
                    ( updatedModel, retryCmd validToken updatedModel )

                Nothing ->
                    ( { model
                        | loading = False
                        , error = Just "Could not refresh booking session token. Please refresh and try again."
                        , csrfRefreshAttempted = False
                      }
                    , Cmd.none
                    )

        Err _ ->
            ( { model
                | loading = False
                , error = Just "Could not refresh booking session token. Please refresh and try again."
                , csrfRefreshAttempted = False
              }
            , Cmd.none
            )


previousStep : FormStep -> FormStep
previousStep step =
    case step of
        TitleStep ->
            TitleStep

        AvailabilityStep ->
            TitleStep

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
                ( { model | currentStep = AvailabilityStep, error = Nothing }, focusElement "availability-input" )

        AvailabilityTextUpdated text ->
            ( { model | availabilityText = text }, Cmd.none )

        AvailabilityStepCompleted ->
            if String.isEmpty (String.trim model.availabilityText) then
                ( { model | error = Just "Please describe your availability." }, Cmd.none )

            else
                withCsrfToken { model | loading = True, error = Nothing, csrfRefreshAttempted = False } <|
                    \csrfToken ->
                        ( { model | loading = True, error = Nothing, csrfRefreshAttempted = False }
                        , Api.parseMessage
                            csrfToken
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
                    , csrfRefreshAttempted = False
                  }
                , Cmd.none
                )

            else
                ( { model
                    | parsedWindows = windows
                    , loading = False
                    , currentStep = AvailabilityConfirmStep
                    , error = Nothing
                    , csrfRefreshAttempted = False
                  }
                , focusElement "confirm-availability-btn"
                )

        AvailabilityWindowsConfirmed ->
            withCsrfToken { model | loading = True, error = Nothing, csrfRefreshAttempted = False } <|
                \csrfToken ->
                    ( { model | loading = True, error = Nothing, csrfRefreshAttempted = False }
                    , Api.fetchSlots csrfToken model.parsedWindows defaultDurationMinutes model.timezone SlotsReceived
                    )

        ParseResponseReceived (Err err) ->
            case err of
                Http.BadStatus 403 ->
                    if model.csrfRefreshAttempted then
                        ( { model
                            | loading = False
                            , error = Just "Booking session expired. Please refresh and try again."
                            , csrfRefreshAttempted = False
                          }
                        , Cmd.none
                        )

                    else
                        refreshCsrfForParse model

                _ ->
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
                    ( { model | loading = False, error = Just errorMsg, csrfRefreshAttempted = False }, Cmd.none )

        SlotsReceived (Ok slots) ->
            ( { model
                | slots = slots
                , loading = False
                , currentStep = SlotSelectionStep
                , error = Nothing
                , csrfRefreshAttempted = False
              }
            , focusElement "slot-0"
            )

        SlotsReceived (Err err) ->
            case err of
                Http.BadStatus 403 ->
                    if model.csrfRefreshAttempted then
                        ( { model
                            | loading = False
                            , error = Just "Booking session expired. Please refresh and try again."
                            , csrfRefreshAttempted = False
                          }
                        , Cmd.none
                        )

                    else
                        refreshCsrfForSlots model

                _ ->
                    ( { model
                        | loading = False
                        , error = Just "Failed to load available slots. Please try again."
                        , csrfRefreshAttempted = False
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
            case makeBookRequest model of
                Just req ->
                    withCsrfToken { model | loading = True, error = Nothing, csrfRefreshAttempted = False } <|
                        \csrfToken ->
                            ( { model | loading = True, error = Nothing, csrfRefreshAttempted = False }
                            , Api.bookSlot csrfToken req BookingResultReceived
                            )

                Nothing ->
                    ( model, Cmd.none )

        BookingResultReceived (Ok confirmation) ->
            ( { model
                | bookingResult = Just confirmation
                , currentStep = CompleteStep
                , loading = False
                , csrfRefreshAttempted = False
              }
            , Cmd.none
            )

        BookingResultReceived (Err err) ->
            case err of
                Http.BadStatus 403 ->
                    if model.csrfRefreshAttempted then
                        ( { model
                            | loading = False
                            , error = Just "Booking session expired. Please refresh and try again."
                            , csrfRefreshAttempted = False
                          }
                        , Cmd.none
                        )

                    else
                        refreshCsrfForBook model

                Http.BadStatus 409 ->
                    withCsrfToken
                        { model
                            | loading = True
                            , currentStep = SlotSelectionStep
                            , selectedSlot = Nothing
                            , error = Just "That slot is no longer available. Please choose another time."
                            , csrfRefreshAttempted = False
                        }
                    <|
                        \csrfToken ->
                            ( { model
                                | loading = True
                                , currentStep = SlotSelectionStep
                                , selectedSlot = Nothing
                                , error = Just "That slot is no longer available. Please choose another time."
                                , csrfRefreshAttempted = False
                              }
                            , Api.fetchSlots csrfToken model.parsedWindows defaultDurationMinutes model.timezone SlotsReceived
                            )

                _ ->
                    ( { model
                        | loading = False
                        , error = Just "Failed to confirm booking. Please try again."
                        , csrfRefreshAttempted = False
                      }
                    , Cmd.none
                    )

        CsrfTokenRefreshedForParse result ->
            handleCsrfRefreshResult retryParseWithToken result model

        CsrfTokenRefreshedForSlots result ->
            handleCsrfRefreshResult retrySlotsWithToken result model

        CsrfTokenRefreshedForBook result ->
            handleCsrfRefreshResult retryBookWithToken result model

        TimezoneChanged tz ->
            let
                updatedModel =
                    { model | timezone = tz, timezoneDropdownOpen = False, csrfRefreshAttempted = False }
            in
            case model.currentStep of
                AvailabilityConfirmStep ->
                    withCsrfToken { updatedModel | loading = True, error = Nothing, csrfRefreshAttempted = False } <|
                        \csrfToken ->
                            ( { updatedModel | loading = True, error = Nothing, csrfRefreshAttempted = False }
                            , Api.parseMessage
                                csrfToken
                                model.availabilityText
                                tz
                                []
                                ParseResponseReceived
                            )

                SlotSelectionStep ->
                    withCsrfToken { updatedModel | loading = True, error = Nothing, csrfRefreshAttempted = False } <|
                        \csrfToken ->
                            ( { updatedModel | loading = True, error = Nothing, csrfRefreshAttempted = False }
                            , Api.fetchSlots csrfToken model.parsedWindows defaultDurationMinutes tz SlotsReceived
                            )

                _ ->
                    ( updatedModel, Cmd.none )

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

                        CompleteStep ->
                            Cmd.none
            in
            ( { clearedModel | currentStep = prev, error = Nothing }, focusCmd )

        NoOp ->
            ( model, Cmd.none )
