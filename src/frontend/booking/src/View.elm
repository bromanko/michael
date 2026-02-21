module View exposing (view)

import DateFormat exposing (formatFriendlyDate, formatFriendlyTime)
import Html exposing (Html, button, div, h1, h2, input, label, p, span, text, textarea)
import Html.Attributes exposing (attribute, class, disabled, for, id, maxlength, placeholder, rows, type_, value)
import Html.Events exposing (onClick, onInput, onSubmit, preventDefaultOn)
import Html.Keyed as Keyed
import Json.Decode as Decode
import Model exposing (Model)
import Types exposing (AvailabilityWindow, FormStep(..), TimeSlot)
import Update exposing (Msg(..))


view : Model -> Html Msg
view model =
    div [ class "min-h-screen flex flex-col" ]
        [ progressBar model.currentStep
        , div [ class "flex-1 flex items-center justify-center px-6 py-12" ]
            [ div [ class "w-full max-w-xl" ]
                [ errorBanner model
                , stepContent model
                ]
            ]
        , viewFooter
        ]



-- Progress bar (thin, minimal)


stepNumber : FormStep -> Int
stepNumber step =
    case step of
        TitleStep ->
            1

        AvailabilityStep ->
            2

        AvailabilityConfirmStep ->
            3

        SlotSelectionStep ->
            4

        ContactInfoStep ->
            5

        ConfirmationStep ->
            6

        CompleteStep ->
            7


progressBar : FormStep -> Html msg
progressBar step =
    let
        current =
            stepNumber step

        totalSteps =
            7

        pct =
            String.fromFloat (toFloat current / toFloat totalSteps * 100) ++ "%"
    in
    div [ class "fixed top-0 left-0 right-0 z-50" ]
        [ div [ class "w-full bg-sand-200 h-1" ]
            [ div
                [ class "bg-coral h-1 transition-all duration-500 ease-out"
                , Html.Attributes.style "width" pct
                ]
                []
            ]
        ]


errorBanner : Model -> Html msg
errorBanner model =
    case model.error of
        Just err ->
            div [ class "mb-8 px-5 py-4 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm", attribute "role" "alert" ]
                [ text err ]

        Nothing ->
            text ""



-- Question typography helper


questionHeading : String -> Html msg
questionHeading q =
    h1 [ class "font-display text-question md:text-question-lg text-sand-900 mb-3" ]
        [ text q ]


questionSubtext : String -> Html msg
questionSubtext s =
    p [ class "text-sand-500 text-lg mb-10" ]
        [ text s ]



-- Primary action button


primaryButton : { label : String, isDisabled : Bool, isLoading : Bool, id : String } -> Html Msg
primaryButton config =
    button
        [ type_ "submit"
        , class "bg-coral text-white px-8 py-3 rounded-full text-base font-medium hover:bg-coral-dark transition-colors disabled:opacity-40"
        , disabled config.isDisabled
        , id config.id
        ]
        [ text config.label
        , if not config.isDisabled then
            span [ class "ml-2 text-sm opacity-70" ] [ text "press Enter" ]

          else
            text ""
        ]


backButton : String -> Html Msg
backButton buttonId =
    button
        [ type_ "button"
        , class "text-sand-500 hover:text-sand-700 text-sm transition-colors"
        , id buttonId
        , onClick BackStepClicked
        ]
        [ text "Back" ]


actionRow : { backButtonId : Maybe String } -> List (Html Msg) -> Html Msg
actionRow config children =
    div [ class "flex items-center gap-6 mt-10" ]
        (children
            ++ (case config.backButtonId of
                    Just backId ->
                        [ backButton backId ]

                    Nothing ->
                        []
               )
        )



-- Enter key on textarea submits instead of inserting a newline


onEnterSubmit : Msg -> Html.Attribute Msg
onEnterSubmit msg =
    preventDefaultOn "keydown"
        (Decode.map2 Tuple.pair
            (Decode.field "key" Decode.string)
            (Decode.field "shiftKey" Decode.bool)
            |> Decode.andThen
                (\( key, shift ) ->
                    if key == "Enter" && not shift then
                        Decode.succeed ( msg, True )

                    else
                        Decode.fail "not Enter"
                )
        )



-- Input styling


inputClasses : String
inputClasses =
    "w-full bg-transparent border-b-2 border-sand-300 text-sand-900 text-xl px-0 py-3 focus:outline-none focus:border-coral transition-colors placeholder:text-sand-400"


textareaClasses : String
textareaClasses =
    "w-full bg-transparent border-b-2 border-sand-300 text-sand-900 text-lg px-0 py-3 focus:outline-none focus:border-coral transition-colors placeholder:text-sand-400 resize-none"



-- Steps


stepContent : Model -> Html Msg
stepContent model =
    case model.currentStep of
        TitleStep ->
            viewTitleStep model

        AvailabilityStep ->
            viewAvailabilityStep model

        AvailabilityConfirmStep ->
            viewAvailabilityConfirmStep model

        SlotSelectionStep ->
            viewSlotSelectionStep model

        ContactInfoStep ->
            viewContactInfoStep model

        ConfirmationStep ->
            viewConfirmationStep model

        CompleteStep ->
            viewCompleteStep model



-- 1. Title


viewTitleStep : Model -> Html Msg
viewTitleStep model =
    Html.form [ onSubmit TitleStepCompleted ]
        [ questionHeading "What would you like to meet about?"
        , questionSubtext "Give it a short title."
        , input
            [ type_ "text"
            , class inputClasses
            , placeholder "e.g. Project kickoff, Coffee chat..."
            , value model.title
            , onInput TitleUpdated
            , id "title-input"
            , maxlength 500
            ]
            []
        , actionRow { backButtonId = Nothing }
            [ primaryButton
                { label = "OK"
                , isDisabled = String.isEmpty (String.trim model.title)
                , isLoading = False
                , id = "title-submit-btn"
                }
            ]
        ]



-- 2. Availability


viewAvailabilityStep : Model -> Html Msg
viewAvailabilityStep model =
    Html.form [ onSubmit AvailabilityStepCompleted ]
        [ questionHeading "When are you free?"
        , questionSubtext "Describe your availability in plain language."
        , textarea
            [ class textareaClasses
            , id "availability-input"
            , placeholder "e.g. Tomorrow between 9am and 5pm, or next Monday afternoon..."
            , value model.availabilityText
            , onInput AvailabilityTextUpdated
            , onEnterSubmit AvailabilityStepCompleted
            , rows 3
            , maxlength 2000
            ]
            []
        , actionRow { backButtonId = Just "availability-back-btn" }
            [ primaryButton
                { label =
                    if model.loading then
                        "Finding slots..."

                    else
                        "Find slots"
                , isDisabled = model.loading || String.isEmpty (String.trim model.availabilityText)
                , isLoading = model.loading
                , id = "availability-submit-btn"
                }
            ]
        ]



-- 3b. Availability confirmation


viewAvailabilityConfirmStep : Model -> Html Msg
viewAvailabilityConfirmStep model =
    Html.form [ onSubmit AvailabilityWindowsConfirmed ]
        [ questionHeading "Did I get that right?"
        , questionSubtext "Here's what I understood about your availability."
        , div [ class "mb-4" ]
            [ timezoneSelector "availability-confirm" model ]
        , div [ class "space-y-3 mb-6" ]
            (List.map viewParsedWindow model.parsedWindows)
        , actionRow { backButtonId = Just "availability-confirm-back-btn" }
            [ primaryButton
                { label =
                    if model.loading then
                        "Finding slots..."

                    else
                        "Looks good"
                , isDisabled = model.loading
                , isLoading = model.loading
                , id = "confirm-availability-btn"
                }
            ]
        ]


viewParsedWindow : AvailabilityWindow -> Html msg
viewParsedWindow window =
    div [ class "px-5 py-4 rounded-lg border-2 border-sand-200 bg-sand-50" ]
        [ div [ class "text-lg font-medium text-sand-800" ]
            [ text (formatFriendlyDate window.start) ]
        , div [ class "text-sand-500 text-sm mt-1" ]
            [ text (formatFriendlyTime window.start ++ " – " ++ formatFriendlyTime window.end) ]
        ]



-- 4. Slot selection


viewSlotSelectionStep : Model -> Html Msg
viewSlotSelectionStep model =
    div []
        [ questionHeading "Pick a time that works."
        , if List.isEmpty model.slots then
            div []
                [ questionSubtext "No overlapping slots found for those times."
                , button
                    [ class "text-coral hover:text-coral-dark text-base font-medium transition-colors"
                    , id "slot-selection-try-different-times-btn"
                    , onClick BackStepClicked
                    ]
                    [ text "Try different times" ]
                ]

          else
            div []
                [ questionSubtext "These times are available. Use Tab or ↑↓ to browse, Enter to select."
                , div [ class "mb-4" ]
                    [ timezoneSelector "slot-selection" model ]
                , Keyed.node "div"
                    [ class "space-y-3 max-h-80 overflow-y-auto pr-2" ]
                    (List.indexedMap (\i slot -> ( slot.start, slotButton i slot )) model.slots)
                , div [ class "mt-10" ]
                    [ backButton "slot-selection-back-btn" ]
                ]
        ]


slotButton : Int -> TimeSlot -> Html Msg
slotButton index slot =
    button
        [ class "w-full text-left px-6 py-4 rounded-lg border-2 border-sand-300 hover:border-coral hover:bg-coral/5 focus:border-coral focus:bg-coral/5 focus:outline-none transition-all group"
        , id ("slot-" ++ String.fromInt index)
        , onClick (SlotSelected slot)
        ]
        [ div [ class "text-lg font-medium text-sand-800 group-hover:text-coral group-focus:text-coral" ]
            [ text (formatFriendlyDate slot.start) ]
        , div [ class "text-sand-500 text-sm mt-1" ]
            [ text (formatFriendlyTime slot.start ++ " – " ++ formatFriendlyTime slot.end) ]
        ]



-- 5. Contact info


viewContactInfoStep : Model -> Html Msg
viewContactInfoStep model =
    Html.form [ onSubmit ContactInfoStepCompleted ]
        [ questionHeading "How can we reach you?"
        , questionSubtext "We’ll send a calendar invite to your email."
        , div [ class "space-y-8" ]
            [ div []
                [ label [ for "name-input", class "block text-sm font-medium text-sand-500 mb-2 uppercase tracking-wider" ] [ text "Name" ]
                , input
                    [ type_ "text"
                    , id "name-input"
                    , class inputClasses
                    , placeholder "Your name"
                    , value model.name
                    , onInput NameUpdated
                    , maxlength 200
                    ]
                    []
                ]
            , div []
                [ label [ for "email-input", class "block text-sm font-medium text-sand-500 mb-2 uppercase tracking-wider" ] [ text "Email" ]
                , input
                    [ type_ "email"
                    , id "email-input"
                    , class inputClasses
                    , placeholder "you@example.com"
                    , value model.email
                    , onInput EmailUpdated
                    , maxlength 254
                    ]
                    []
                ]
            , div []
                [ label [ for "phone-input", class "block text-sm font-medium text-sand-500 mb-2 uppercase tracking-wider" ]
                    [ text "Phone "
                    , span [ class "text-sand-400 normal-case tracking-normal" ] [ text "(optional)" ]
                    ]
                , input
                    [ type_ "tel"
                    , id "phone-input"
                    , class inputClasses
                    , placeholder "+1 (555) 123-4567"
                    , value model.phone
                    , onInput PhoneUpdated
                    , maxlength 50
                    ]
                    []
                ]
            ]
        , actionRow { backButtonId = Just "contact-info-back-btn" }
            [ primaryButton
                { label = "OK"
                , isDisabled = False
                , isLoading = False
                , id = "contact-info-submit-btn"
                }
            ]
        ]



-- 6. Confirmation


viewConfirmationStep : Model -> Html Msg
viewConfirmationStep model =
    let
        slotText =
            case model.selectedSlot of
                Just slot ->
                    formatFriendlyDate slot.start ++ " at " ++ formatFriendlyTime slot.start ++ " – " ++ formatFriendlyTime slot.end

                Nothing ->
                    "—"
    in
    Html.form [ onSubmit BookingConfirmed ]
        [ questionHeading "Does this look right?"
        , questionSubtext "Review your booking details."
        , div [ class "space-y-6 mb-10" ]
            [ summaryField "Topic" model.title
            , summaryField "When" slotText
            , summaryField "Name" model.name
            , summaryField "Email" model.email
            , if String.isEmpty (String.trim model.phone) then
                text ""

              else
                summaryField "Phone" model.phone
            ]
        , actionRow { backButtonId = Just "confirmation-back-btn" }
            [ primaryButton
                { label =
                    if model.loading then
                        "Booking..."

                    else
                        "Confirm booking"
                , isDisabled = model.loading
                , isLoading = model.loading
                , id = "confirm-booking-btn"
                }
            ]
        ]


summaryField : String -> String -> Html msg
summaryField fieldLabel fieldValue =
    div [ class "border-b border-sand-200 pb-4" ]
        [ div [ class "text-sm font-medium text-sand-500 uppercase tracking-wider mb-1" ]
            [ text fieldLabel ]
        , div [ class "text-xl text-sand-900" ]
            [ text fieldValue ]
        ]



-- 7. Complete


viewCompleteStep : Model -> Html msg
viewCompleteStep model =
    div [ class "text-center" ]
        [ div [ class "text-coral text-6xl mb-6" ] [ text "✓" ]
        , h2 [ class "font-display text-question md:text-question-lg text-sand-900 mb-4" ]
            [ text "You’re booked." ]
        , case model.bookingResult of
            Just result ->
                p [ class "text-sand-500 text-lg mb-2" ]
                    [ text ("Booking ID: " ++ result.bookingId) ]

            Nothing ->
                text ""
        , p [ class "text-sand-500 text-lg" ]
            [ text "A confirmation email is on its way." ]
        ]



-- Footer


viewFooter : Html Msg
viewFooter =
    div [ class "py-6 px-6" ]
        [ div [ class "max-w-xl mx-auto" ]
            [ p [ class "text-sand-400 text-sm text-center" ]
                [ text "Powered by Michael" ]
            ]
        ]



-- Inline timezone selector (for availability confirm & slot selection steps)


timezoneSelector : String -> Model -> Html Msg
timezoneSelector stepId model =
    div [ class "relative inline-block" ]
        [ button
            [ type_ "button"
            , class "text-sand-500 hover:text-sand-700 text-sm transition-colors flex items-center gap-1.5 px-3 py-1.5 rounded-full border border-sand-300 hover:border-sand-400"
            , id (stepId ++ "-timezone-toggle-btn")
            , onClick TimezoneDropdownToggled
            ]
            [ span [ class "text-xs" ] [ text "🌐" ]
            , text (formatTimezoneName model.timezone)
            , span [ class "text-xs text-sand-400" ] [ text "▾" ]
            ]
        , if model.timezoneDropdownOpen then
            div [ class "absolute top-full left-0 mt-2 w-72 bg-white border border-sand-200 rounded-lg shadow-lg max-h-64 overflow-y-auto z-50" ]
                [ div [ class "p-2" ]
                    (List.map (timezoneOption stepId model.timezone) commonTimezones)
                ]

          else
            text ""
        ]


timezoneOption : String -> String -> String -> Html Msg
timezoneOption stepId currentTz tz =
    button
        [ type_ "button"
        , class
            ("w-full text-left px-3 py-2 rounded text-sm transition-colors "
                ++ (if tz == currentTz then
                        "bg-coral/10 text-coral font-medium"

                    else
                        "text-sand-700 hover:bg-sand-100"
                   )
            )
        , id (stepId ++ "-timezone-option-" ++ formatTimezoneIdSegment tz ++ "-btn")
        , onClick (TimezoneChanged tz)
        ]
        [ text (formatTimezoneName tz) ]


formatTimezoneName : String -> String
formatTimezoneName tz =
    -- Turn "America/New_York" into "America / New York"
    tz
        |> String.replace "_" " "
        |> String.replace "/" " / "


formatTimezoneIdSegment : String -> String
formatTimezoneIdSegment tz =
    tz
        |> String.toLower
        |> String.replace "/" "-"
        |> String.replace "_" "-"


commonTimezones : List String
commonTimezones =
    [ "Pacific/Honolulu"
    , "America/Anchorage"
    , "America/Los_Angeles"
    , "America/Denver"
    , "America/Chicago"
    , "America/New_York"
    , "America/Sao_Paulo"
    , "Atlantic/Reykjavik"
    , "Europe/London"
    , "Europe/Paris"
    , "Europe/Berlin"
    , "Europe/Helsinki"
    , "Europe/Moscow"
    , "Asia/Dubai"
    , "Asia/Kolkata"
    , "Asia/Bangkok"
    , "Asia/Shanghai"
    , "Asia/Tokyo"
    , "Asia/Seoul"
    , "Australia/Sydney"
    , "Pacific/Auckland"
    ]
