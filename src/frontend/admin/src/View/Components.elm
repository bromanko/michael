module View.Components exposing
    ( card
    , dangerButton
    , errorBanner
    , formatDateTime
    , formatTime12Hour
    , loadingSpinner
    , pageHeading
    , primaryButton
    , secondaryButton
    , statusBadge
    , successBanner
    )

import Html exposing (Html, button, div, h1, p, span, text)
import Html.Attributes exposing (class, disabled, type_)
import Html.Events exposing (onClick)
import Types exposing (BookingStatus(..))


pageHeading : String -> Html msg
pageHeading title =
    h1 [ class "font-display text-2xl text-sand-900 mb-6" ]
        [ text title ]


card : List (Html msg) -> Html msg
card children =
    div [ class "bg-white rounded-lg shadow-sm border border-sand-200 p-6" ]
        children


primaryButton : { label : String, onPress : msg, isDisabled : Bool, isLoading : Bool } -> Html msg
primaryButton config =
    button
        [ type_ "button"
        , class "bg-coral text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-coral-dark transition-colors disabled:opacity-40"
        , onClick config.onPress
        , disabled (config.isDisabled || config.isLoading)
        ]
        [ text
            (if config.isLoading then
                "Loading..."

             else
                config.label
            )
        ]


secondaryButton : { label : String, onPress : msg } -> Html msg
secondaryButton config =
    button
        [ type_ "button"
        , class "border border-sand-300 text-sand-700 px-5 py-2 rounded-lg text-sm font-medium hover:bg-sand-100 transition-colors"
        , onClick config.onPress
        ]
        [ text config.label ]


dangerButton : { label : String, onPress : msg, isDisabled : Bool, isLoading : Bool } -> Html msg
dangerButton config =
    button
        [ type_ "button"
        , class "bg-red-600 text-white px-5 py-2 rounded-lg text-sm font-medium hover:bg-red-700 transition-colors disabled:opacity-40"
        , onClick config.onPress
        , disabled (config.isDisabled || config.isLoading)
        ]
        [ text
            (if config.isLoading then
                "Cancelling..."

             else
                config.label
            )
        ]


statusBadge : BookingStatus -> Html msg
statusBadge status =
    let
        ( label, classes ) =
            case status of
                Confirmed ->
                    ( "Confirmed"
                    , "bg-green-100 text-green-800"
                    )

                Cancelled ->
                    ( "Cancelled"
                    , "bg-red-100 text-red-800"
                    )
    in
    span [ class ("inline-block px-2 py-1 rounded-full text-xs font-medium " ++ classes) ]
        [ text label ]


errorBanner : String -> Html msg
errorBanner message =
    div [ class "mb-6 px-5 py-4 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm" ]
        [ text message ]


successBanner : String -> Html msg
successBanner message =
    div [ class "mb-6 px-5 py-4 bg-green-50 border border-green-200 rounded-lg text-green-700 text-sm" ]
        [ text message ]


loadingSpinner : Html msg
loadingSpinner =
    div [ class "flex items-center justify-center py-12" ]
        [ div [ class "text-sand-400 text-sm" ]
            [ text "Loading..." ]
        ]


formatDateTime : String -> String
formatDateTime isoString =
    let
        datePart =
            String.left 10 isoString

        timePart =
            formatTime12Hour (String.slice 11 16 isoString)
    in
    datePart ++ " at " ++ timePart


formatTime12Hour : String -> String
formatTime12Hour timeStr =
    -- Expects "HH:MM" format
    let
        parts =
            String.split ":" timeStr

        ( hour, minute ) =
            case parts of
                [ h, m ] ->
                    ( String.toInt h |> Maybe.withDefault 0, m )

                _ ->
                    ( 0, "00" )

        ( displayHour, amPm ) =
            if hour == 0 then
                ( 12, "am" )

            else if hour < 12 then
                ( hour, "am" )

            else if hour == 12 then
                ( 12, "pm" )

            else
                ( hour - 12, "pm" )
    in
    String.fromInt displayHour ++ ":" ++ minute ++ " " ++ amPm
