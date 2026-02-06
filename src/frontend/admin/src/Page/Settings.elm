module Page.Settings exposing (FormState, Model, Msg, init, update, validateForm, view)

import Api
import Html exposing (Html, button, div, input, label, p, text)
import Html.Attributes exposing (class, disabled, for, id, placeholder, type_, value)
import Html.Events exposing (onClick, onInput)
import Http
import Types exposing (SchedulingSettings)
import View.Components exposing (card, errorBanner, loadingSpinner, pageHeading, successBanner)


type alias Model =
    { settings : Maybe SchedulingSettings
    , form : FormState
    , loading : Bool
    , saving : Bool
    , error : Maybe String
    , success : Maybe String
    }


type alias FormState =
    { minNoticeHours : String
    , bookingWindowDays : String
    , defaultDurationMinutes : String
    , videoLink : String
    }


type Msg
    = SettingsReceived (Result Http.Error SchedulingSettings)
    | MinNoticeHoursChanged String
    | BookingWindowDaysChanged String
    | DefaultDurationMinutesChanged String
    | VideoLinkChanged String
    | SaveClicked
    | SaveCompleted (Result Http.Error SchedulingSettings)


emptyForm : FormState
emptyForm =
    { minNoticeHours = ""
    , bookingWindowDays = ""
    , defaultDurationMinutes = ""
    , videoLink = ""
    }


settingsToForm : SchedulingSettings -> FormState
settingsToForm s =
    { minNoticeHours = String.fromInt s.minNoticeHours
    , bookingWindowDays = String.fromInt s.bookingWindowDays
    , defaultDurationMinutes = String.fromInt s.defaultDurationMinutes
    , videoLink = Maybe.withDefault "" s.videoLink
    }


init : ( Model, Cmd Msg )
init =
    ( { settings = Nothing
      , form = emptyForm
      , loading = True
      , saving = False
      , error = Nothing
      , success = Nothing
      }
    , Api.fetchSettings SettingsReceived
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        SettingsReceived (Ok settings) ->
            ( { model
                | settings = Just settings
                , form = settingsToForm settings
                , loading = False
              }
            , Cmd.none
            )

        SettingsReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load settings." }, Cmd.none )

        MinNoticeHoursChanged val ->
            let
                form =
                    model.form
            in
            ( { model | form = { form | minNoticeHours = val }, success = Nothing }, Cmd.none )

        BookingWindowDaysChanged val ->
            let
                form =
                    model.form
            in
            ( { model | form = { form | bookingWindowDays = val }, success = Nothing }, Cmd.none )

        DefaultDurationMinutesChanged val ->
            let
                form =
                    model.form
            in
            ( { model | form = { form | defaultDurationMinutes = val }, success = Nothing }, Cmd.none )

        VideoLinkChanged val ->
            let
                form =
                    model.form
            in
            ( { model | form = { form | videoLink = val }, success = Nothing }, Cmd.none )

        SaveClicked ->
            case validateForm model.form of
                Err err ->
                    ( { model | error = Just err, success = Nothing }, Cmd.none )

                Ok settings ->
                    ( { model | saving = True, error = Nothing, success = Nothing }
                    , Api.saveSettings settings SaveCompleted
                    )

        SaveCompleted (Ok settings) ->
            ( { model
                | settings = Just settings
                , form = settingsToForm settings
                , saving = False
                , success = Just "Settings saved successfully."
              }
            , Cmd.none
            )

        SaveCompleted (Err _) ->
            ( { model | saving = False, error = Just "Failed to save settings." }, Cmd.none )


validateForm : FormState -> Result String SchedulingSettings
validateForm form =
    let
        minNotice =
            String.toInt form.minNoticeHours

        bookingWindow =
            String.toInt form.bookingWindowDays

        defaultDuration =
            String.toInt form.defaultDurationMinutes
    in
    case ( minNotice, bookingWindow, defaultDuration ) of
        ( Nothing, _, _ ) ->
            Err "Minimum notice hours must be a number."

        ( _, Nothing, _ ) ->
            Err "Booking window days must be a number."

        ( _, _, Nothing ) ->
            Err "Default duration must be a number."

        ( Just mn, Just bw, Just dd ) ->
            if mn < 0 then
                Err "Minimum notice hours cannot be negative."

            else if bw < 1 then
                Err "Booking window must be at least 1 day."

            else if dd < 5 then
                Err "Default duration must be at least 5 minutes."

            else if dd > 480 then
                Err "Default duration cannot exceed 480 minutes (8 hours)."

            else
                Ok
                    { minNoticeHours = mn
                    , bookingWindowDays = bw
                    , defaultDurationMinutes = dd
                    , videoLink =
                        if String.isEmpty (String.trim form.videoLink) then
                            Nothing

                        else
                            Just (String.trim form.videoLink)
                    }


view : Model -> Html Msg
view model =
    div []
        [ pageHeading "Settings"
        , case model.error of
            Just err ->
                errorBanner err

            Nothing ->
                text ""
        , case model.success of
            Just msg ->
                successBanner msg

            Nothing ->
                text ""
        , if model.loading then
            loadingSpinner

          else
            formView model
        ]


formView : Model -> Html Msg
formView model =
    card
        [ div [ class "space-y-6" ]
            [ div []
                [ label [ for "minNoticeHours", class "block text-sm font-medium text-sand-700 mb-1" ]
                    [ text "Minimum Notice (hours)" ]
                , input
                    [ type_ "number"
                    , id "minNoticeHours"
                    , value model.form.minNoticeHours
                    , onInput MinNoticeHoursChanged
                    , class "w-full px-3 py-2 border border-sand-300 rounded-md focus:outline-none focus:ring-2 focus:ring-coral focus:border-transparent"
                    , placeholder "6"
                    ]
                    []
                , p [ class "mt-1 text-sm text-sand-500" ]
                    [ text "How far in advance participants must book (0 for no restriction)." ]
                ]
            , div []
                [ label [ for "bookingWindowDays", class "block text-sm font-medium text-sand-700 mb-1" ]
                    [ text "Booking Window (days)" ]
                , input
                    [ type_ "number"
                    , id "bookingWindowDays"
                    , value model.form.bookingWindowDays
                    , onInput BookingWindowDaysChanged
                    , class "w-full px-3 py-2 border border-sand-300 rounded-md focus:outline-none focus:ring-2 focus:ring-coral focus:border-transparent"
                    , placeholder "30"
                    ]
                    []
                , p [ class "mt-1 text-sm text-sand-500" ]
                    [ text "How far into the future participants can book." ]
                ]
            , div []
                [ label [ for "defaultDurationMinutes", class "block text-sm font-medium text-sand-700 mb-1" ]
                    [ text "Default Meeting Duration (minutes)" ]
                , input
                    [ type_ "number"
                    , id "defaultDurationMinutes"
                    , value model.form.defaultDurationMinutes
                    , onInput DefaultDurationMinutesChanged
                    , class "w-full px-3 py-2 border border-sand-300 rounded-md focus:outline-none focus:ring-2 focus:ring-coral focus:border-transparent"
                    , placeholder "30"
                    ]
                    []
                , p [ class "mt-1 text-sm text-sand-500" ]
                    [ text "Default duration when participants don't specify." ]
                ]
            , div []
                [ label [ for "videoLink", class "block text-sm font-medium text-sand-700 mb-1" ]
                    [ text "Video Conferencing Link (optional)" ]
                , input
                    [ type_ "url"
                    , id "videoLink"
                    , value model.form.videoLink
                    , onInput VideoLinkChanged
                    , class "w-full px-3 py-2 border border-sand-300 rounded-md focus:outline-none focus:ring-2 focus:ring-coral focus:border-transparent"
                    , placeholder "https://zoom.us/j/..."
                    ]
                    []
                , p [ class "mt-1 text-sm text-sand-500" ]
                    [ text "Default video link to include in meeting invites." ]
                ]
            , div [ class "pt-4" ]
                [ button
                    [ onClick SaveClicked
                    , disabled model.saving
                    , class
                        (if model.saving then
                            "w-full py-2 px-4 bg-sand-300 text-sand-500 rounded-md cursor-not-allowed"

                         else
                            "w-full py-2 px-4 bg-coral text-white rounded-md hover:bg-coral-dark focus:outline-none focus:ring-2 focus:ring-coral focus:ring-offset-2"
                        )
                    ]
                    [ text
                        (if model.saving then
                            "Saving..."

                         else
                            "Save Settings"
                        )
                    ]
                ]
            ]
        ]
