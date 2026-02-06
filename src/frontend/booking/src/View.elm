module View exposing (view)

import Html exposing (Html, button, div, h1, h2, input, label, li, p, span, text, textarea, ul)
import Html.Attributes exposing (class, disabled, for, id, placeholder, rows, type_, value)
import Html.Events exposing (onClick, onInput, onSubmit)
import Html.Keyed as Keyed
import Model exposing (Model)
import Types exposing (AvailabilityWindow, DurationChoice(..), FormStep(..), TimeSlot)
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
        , viewFooter model
        ]



-- Progress bar (thin, minimal)


stepNumber : FormStep -> Int
stepNumber step =
    case step of
        TitleStep ->
            1

        DurationStep ->
            2

        AvailabilityStep ->
            3

        AvailabilityConfirmStep ->
            4

        SlotSelectionStep ->
            5

        ContactInfoStep ->
            6

        ConfirmationStep ->
            7

        CompleteStep ->
            8


progressBar : FormStep -> Html msg
progressBar step =
    let
        current =
            stepNumber step

        totalSteps =
            8

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
            div [ class "mb-8 px-5 py-4 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm" ]
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


primaryButton : { label : String, isDisabled : Bool, onPress : Msg, isLoading : Bool } -> Html Msg
primaryButton config =
    button
        [ type_ "submit"
        , class "bg-coral text-white px-8 py-3 rounded-full text-base font-medium hover:bg-coral-dark transition-colors disabled:opacity-40"
        , onClick config.onPress
        , disabled config.isDisabled
        ]
        [ text config.label
        , if not config.isDisabled then
            span [ class "ml-2 text-sm opacity-70" ] [ text "press Enter" ]

          else
            text ""
        ]


backButton : Html Msg
backButton =
    button
        [ type_ "button"
        , class "text-sand-500 hover:text-sand-700 text-sm transition-colors"
        , onClick BackStepClicked
        ]
        [ text "Back" ]


actionRow : { showBack : Bool } -> List (Html Msg) -> Html Msg
actionRow config children =
    div [ class "flex items-center gap-6 mt-10" ]
        (children
            ++ (if config.showBack then
                    [ backButton ]

                else
                    []
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

        DurationStep ->
            viewDurationStep model

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
            ]
            []
        , actionRow { showBack = False }
            [ primaryButton
                { label = "OK"
                , isDisabled = String.isEmpty (String.trim model.title)
                , onPress = TitleStepCompleted
                , isLoading = False
                }
            ]
        ]



-- 2. Duration


viewDurationStep : Model -> Html Msg
viewDurationStep model =
    let
        presets =
            [ 15, 30, 45, 60 ]

        isSelected mins =
            case model.durationChoice of
                Just (Preset m) ->
                    m == mins

                _ ->
                    False

        isCustomSelected =
            case model.durationChoice of
                Just Custom ->
                    True

                _ ->
                    False

        presetButton mins =
            button
                [ type_ "button"
                , class
                    ("px-6 py-4 rounded-lg border-2 text-lg font-medium transition-all "
                        ++ (if isSelected mins then
                                "border-coral bg-coral/10 text-coral"

                            else
                                "border-sand-300 hover:border-coral/50 text-sand-700"
                           )
                    )
                , onClick (DurationPresetSelected mins)
                ]
                [ text (String.fromInt mins ++ " min") ]
    in
    div []
        [ questionHeading "How long should it be?"
        , questionSubtext "Pick a duration for your meeting."
        , div [ class "grid grid-cols-2 gap-4 mb-4" ]
            (List.map presetButton presets)
        , button
            [ type_ "button"
            , class
                ("w-full px-6 py-4 rounded-lg border-2 text-lg font-medium transition-all "
                    ++ (if isCustomSelected then
                            "border-coral bg-coral/10 text-coral"

                        else
                            "border-sand-300 hover:border-coral/50 text-sand-700"
                       )
                )
            , onClick CustomDurationSelected
            ]
            [ text "Custom duration" ]
        , if isCustomSelected then
            div [ class "mt-4" ]
                [ input
                    [ type_ "number"
                    , class inputClasses
                    , placeholder "Minutes"
                    , value model.customDuration
                    , onInput CustomDurationUpdated
                    ]
                    []
                ]

          else
            text ""
        , actionRow { showBack = True }
            [ primaryButton
                { label = "OK"
                , isDisabled = model.durationChoice == Nothing
                , onPress = DurationStepCompleted
                , isLoading = False
                }
            ]
        ]



-- 3. Availability


viewAvailabilityStep : Model -> Html Msg
viewAvailabilityStep model =
    Html.form [ onSubmit AvailabilityStepCompleted ]
        [ questionHeading "When are you free?"
        , questionSubtext "Describe your availability in plain language."
        , textarea
            [ class textareaClasses
            , placeholder "e.g. Tomorrow between 9am and 5pm, or next Monday afternoon..."
            , value model.availabilityText
            , onInput AvailabilityTextUpdated
            , rows 3
            ]
            []
        , actionRow { showBack = True }
            [ primaryButton
                { label =
                    if model.loading then
                        "Finding slots..."

                    else
                        "Find slots"
                , isDisabled = model.loading || String.isEmpty (String.trim model.availabilityText)
                , onPress = AvailabilityStepCompleted
                , isLoading = model.loading
                }
            ]
        ]



-- 3b. Availability confirmation


viewAvailabilityConfirmStep : Model -> Html Msg
viewAvailabilityConfirmStep model =
    div []
        [ questionHeading "Did I get that right?"
        , questionSubtext "Here's what I understood about your availability."
        , div [ class "space-y-3 mb-6" ]
            (List.map viewParsedWindow model.parsedWindows)
        , actionRow { showBack = True }
            [ primaryButton
                { label =
                    if model.loading then
                        "Finding slots..."

                    else
                        "Looks good"
                , isDisabled = model.loading
                , onPress = AvailabilityWindowsConfirmed
                , isLoading = model.loading
                }
            ]
        ]


viewParsedWindow : AvailabilityWindow -> Html msg
viewParsedWindow window =
    div [ class "px-5 py-4 rounded-lg border-2 border-sand-200 bg-sand-50" ]
        [ div [ class "text-lg font-medium text-sand-800" ]
            [ text (formatWindowDate window.start) ]
        , div [ class "text-sand-500 text-sm mt-1" ]
            [ text (formatWindowTime window.start ++ " – " ++ formatWindowTime window.end) ]
        ]


formatWindowDate : String -> String
formatWindowDate isoString =
    -- ISO strings from the backend are like "2026-02-09T09:00:00-05:00"
    String.left 10 isoString


formatWindowTime : String -> String
formatWindowTime isoString =
    String.slice 11 16 isoString



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
                    , onClick BackStepClicked
                    ]
                    [ text "Try different times" ]
                ]

          else
            div []
                [ questionSubtext "These times are available."
                , Keyed.node "div"
                    [ class "space-y-3 max-h-80 overflow-y-auto pr-2" ]
                    (List.map (\slot -> ( slot.start, slotButton slot )) model.slots)
                , div [ class "mt-10" ]
                    [ backButton ]
                ]
        ]


slotButton : TimeSlot -> Html Msg
slotButton slot =
    button
        [ class "w-full text-left px-6 py-4 rounded-lg border-2 border-sand-300 hover:border-coral hover:bg-coral/5 transition-all group"
        , onClick (SlotSelected slot)
        ]
        [ div [ class "text-lg font-medium text-sand-800 group-hover:text-coral" ]
            [ text (formatSlotDate slot.start) ]
        , div [ class "text-sand-500 text-sm mt-1" ]
            [ text (formatSlotTimeOnly slot.start ++ " – " ++ formatSlotTimeOnly slot.end) ]
        ]


formatSlotDate : String -> String
formatSlotDate isoString =
    String.left 10 isoString


formatSlotTimeOnly : String -> String
formatSlotTimeOnly isoString =
    String.slice 11 16 isoString



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
                    ]
                    []
                ]
            ]
        , actionRow { showBack = True }
            [ primaryButton
                { label = "OK"
                , isDisabled = False
                , onPress = ContactInfoStepCompleted
                , isLoading = False
                }
            ]
        ]



-- 6. Confirmation


viewConfirmationStep : Model -> Html Msg
viewConfirmationStep model =
    let
        durationText =
            case model.durationChoice of
                Just (Preset mins) ->
                    String.fromInt mins ++ " minutes"

                Just Custom ->
                    model.customDuration ++ " minutes"

                Nothing ->
                    "30 minutes"

        slotText =
            case model.selectedSlot of
                Just slot ->
                    formatSlotDate slot.start ++ " at " ++ formatSlotTimeOnly slot.start ++ " – " ++ formatSlotTimeOnly slot.end

                Nothing ->
                    "—"
    in
    div []
        [ questionHeading "Does this look right?"
        , questionSubtext "Review your booking details."
        , div [ class "space-y-6 mb-10" ]
            [ summaryField "Topic" model.title
            , summaryField "Duration" durationText
            , summaryField "When" slotText
            , summaryField "Name" model.name
            , summaryField "Email" model.email
            , if String.isEmpty (String.trim model.phone) then
                text ""

              else
                summaryField "Phone" model.phone
            ]
        , actionRow { showBack = True }
            [ primaryButton
                { label =
                    if model.loading then
                        "Booking..."

                    else
                        "Confirm booking"
                , isDisabled = model.loading
                , onPress = BookingConfirmed
                , isLoading = model.loading
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



-- Footer with timezone selector


viewFooter : Model -> Html Msg
viewFooter model =
    div [ class "py-6 px-6" ]
        [ div [ class "max-w-xl mx-auto flex items-center justify-between" ]
            [ p [ class "text-sand-400 text-sm" ]
                [ text "Powered by Michael" ]
            , timezoneSelector model
            ]
        ]


timezoneSelector : Model -> Html Msg
timezoneSelector model =
    div [ class "relative" ]
        [ button
            [ type_ "button"
            , class "text-sand-400 hover:text-sand-600 text-sm transition-colors flex items-center gap-1"
            , onClick TimezoneDropdownToggled
            ]
            [ span [ class "text-xs" ] [ text "🌐" ]
            , text (formatTimezoneName model.timezone)
            ]
        , if model.timezoneDropdownOpen then
            div [ class "absolute bottom-full right-0 mb-2 w-72 bg-white border border-sand-200 rounded-lg shadow-lg max-h-64 overflow-y-auto z-50" ]
                [ div [ class "p-2" ]
                    (List.map (timezoneOption model.timezone) commonTimezones)
                ]

          else
            text ""
        ]


timezoneOption : String -> String -> Html Msg
timezoneOption currentTz tz =
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
        , onClick (TimezoneChanged tz)
        ]
        [ text (formatTimezoneName tz) ]


formatTimezoneName : String -> String
formatTimezoneName tz =
    -- Turn "America/New_York" into "America / New York"
    tz
        |> String.replace "_" " "
        |> String.replace "/" " / "


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
