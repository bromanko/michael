module Update exposing (Msg(..), update)

import Api
import Http
import Model exposing (Model, emptyParseResult, mergeResult)
import Types exposing (BookingConfirmation, ChatMessage, ConversationPhase(..), MessageRole(..), ParseResponse, ParseResult, TimeSlot)


type Msg
    = InputUpdated String
    | MessageSubmitted
    | ParseResponseReceived (Result Http.Error ParseResponse)
    | ParseConfirmed
    | ParseRejected
    | SlotsReceived (Result Http.Error (List TimeSlot))
    | SlotSelected TimeSlot
    | BookingConfirmed
    | BookingResultReceived (Result Http.Error BookingConfirmation)


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


userMessages : Model -> List String
userMessages model =
    model.messages
        |> List.filter
            (\m ->
                case m.role of
                    User ->
                        True

                    System ->
                        False
            )
        |> List.map .content


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        InputUpdated text ->
            ( { model | inputText = text }, Cmd.none )

        MessageSubmitted ->
            if String.isEmpty (String.trim model.inputText) then
                ( model, Cmd.none )

            else
                let
                    userMsg =
                        { role = User, content = model.inputText }

                    updatedMessages =
                        model.messages ++ [ userMsg ]

                    previousUserMessages =
                        userMessages model
                in
                ( { model
                    | messages = updatedMessages
                    , inputText = ""
                    , loading = True
                    , error = Nothing
                  }
                , Api.parseMessage
                    model.inputText
                    model.timezone
                    previousUserMessages
                    ParseResponseReceived
                )

        ParseResponseReceived (Ok response) ->
            let
                systemMsg =
                    { role = System, content = response.systemMessage }

                newAccumulated =
                    mergeResult model.accumulated response.parseResult

                newPhase =
                    if List.isEmpty newAccumulated.missingFields then
                        ConfirmingParse

                    else
                        Chatting
            in
            ( { model
                | messages = model.messages ++ [ systemMsg ]
                , accumulated = newAccumulated
                , phase = newPhase
                , loading = False
              }
            , Cmd.none
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

                systemMsg =
                    { role = System, content = "Something went wrong. Please try again." }
            in
            ( { model
                | messages = model.messages ++ [ systemMsg ]
                , loading = False
                , error = Just errorMsg
              }
            , Cmd.none
            )

        ParseConfirmed ->
            case model.accumulated.durationMinutes of
                Just duration ->
                    ( { model | loading = True, phase = SelectingSlot }
                    , Api.fetchSlots
                        model.accumulated.availabilityWindows
                        duration
                        model.timezone
                        SlotsReceived
                    )

                Nothing ->
                    ( model, Cmd.none )

        ParseRejected ->
            let
                systemMsg =
                    { role = System
                    , content = "No problem! Please tell me your availability again and I'll re-interpret it."
                    }
            in
            ( { model
                | phase = Chatting
                , messages = model.messages ++ [ systemMsg ]
                , accumulated = Model.emptyParseResult
                , slots = []
                , selectedSlot = Nothing
                , error = Nothing
              }
            , Cmd.none
            )

        SlotsReceived (Ok slots) ->
            ( { model
                | slots = slots
                , loading = False
              }
            , Cmd.none
            )

        SlotsReceived (Err _) ->
            ( { model
                | loading = False
                , error = Just "Failed to load available slots. Please try again."
                , phase = ConfirmingParse
              }
            , Cmd.none
            )

        SlotSelected slot ->
            ( { model
                | selectedSlot = Just slot
                , phase = ConfirmingBooking
              }
            , Cmd.none
            )

        BookingConfirmed ->
            case ( model.selectedSlot, model.accumulated.name, model.accumulated.email ) of
                ( Just slot, Just name, Just email ) ->
                    if not (isValidEmail email) then
                        ( { model | error = Just "Please provide a valid email address." }
                        , Cmd.none
                        )

                    else
                        case ( model.accumulated.title, model.accumulated.durationMinutes ) of
                            ( Just title, Just duration ) ->
                                ( { model | loading = True }
                                , Api.bookSlot
                                    { name = name
                                    , email = email
                                    , phone = model.accumulated.phone
                                    , title = title
                                    , description = model.accumulated.description
                                    , slot = slot
                                    , durationMinutes = duration
                                    , timezone = model.timezone
                                    }
                                    BookingResultReceived
                                )

                            _ ->
                                ( model, Cmd.none )

                _ ->
                    ( model, Cmd.none )

        BookingResultReceived (Ok confirmation) ->
            ( { model
                | bookingResult = Just confirmation
                , phase = BookingComplete
                , loading = False
              }
            , Cmd.none
            )

        BookingResultReceived (Err _) ->
            ( { model
                | loading = False
                , error = Just "Failed to confirm booking. Please try again."
                , phase = ConfirmingBooking
              }
            , Cmd.none
            )
