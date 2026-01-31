module View exposing (view)

import Html exposing (Html, button, div, h1, h3, input, label, p, span, text, textarea)
import Html.Attributes exposing (class, disabled, for, id, placeholder, rows, type_, value)
import Html.Events exposing (onClick, onInput, onSubmit)
import Html.Keyed as Keyed
import Model exposing (Model)
import Types exposing (DurationChoice(..), FormStep(..), TimeSlot)
import Update exposing (Msg(..))


view : Model -> Html Msg
view model =
    div [ class "bg-white rounded-lg shadow-lg overflow-hidden max-w-lg mx-auto" ]
        [ header
        , progressBar model.currentStep
        , errorBanner model
        , stepContent model
        ]


header : Html msg
header =
    div [ class "bg-blue-600 text-white px-6 py-4" ]
        [ h1 [ class "text-xl font-semibold" ] [ text "Schedule a Meeting" ]
        ]


stepNumber : FormStep -> Int
stepNumber step =
    case step of
        TitleStep ->
            1

        DurationStep ->
            2

        AvailabilityStep ->
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
    div [ class "px-6 pt-4 pb-2" ]
        [ div [ class "flex justify-between text-xs text-gray-500 mb-1" ]
            [ text ("Step " ++ String.fromInt current ++ " of " ++ String.fromInt totalSteps) ]
        , div [ class "w-full bg-gray-200 rounded-full h-2" ]
            [ div
                [ class "bg-blue-600 h-2 rounded-full transition-all duration-300"
                , Html.Attributes.style "width" pct
                ]
                []
            ]
        ]


errorBanner : Model -> Html msg
errorBanner model =
    case model.error of
        Just err ->
            div [ class "mx-6 mt-3 p-3 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm" ]
                [ text err ]

        Nothing ->
            text ""


stepContent : Model -> Html Msg
stepContent model =
    case model.currentStep of
        TitleStep ->
            viewTitleStep model

        DurationStep ->
            viewDurationStep model

        AvailabilityStep ->
            viewAvailabilityStep model

        SlotSelectionStep ->
            viewSlotSelectionStep model

        ContactInfoStep ->
            viewContactInfoStep model

        ConfirmationStep ->
            viewConfirmationStep model

        CompleteStep ->
            viewCompleteStep model



-- Title step


viewTitleStep : Model -> Html Msg
viewTitleStep model =
    div [ class "px-6 py-6" ]
        [ h3 [ class "text-lg font-semibold text-gray-800 mb-4" ] [ text "What is your meeting about?" ]
        , Html.form [ onSubmit TitleStepCompleted ]
            [ input
                [ type_ "text"
                , class "w-full border border-gray-300 rounded-lg px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                , placeholder "e.g. Project kickoff, Coffee chat, Interview..."
                , value model.title
                , onInput TitleUpdated
                , id "title-input"
                ]
                []
            , navigationButtons
                { showBack = False
                , nextLabel = "Next"
                , nextDisabled = False
                , onNext = TitleStepCompleted
                , loading = False
                }
            ]
        ]



-- Duration step


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
                    ("px-4 py-3 rounded-lg border text-sm font-medium transition-colors "
                        ++ (if isSelected mins then
                                "border-blue-500 bg-blue-50 text-blue-700"

                            else
                                "border-gray-200 hover:border-blue-400 hover:bg-blue-50 text-gray-700"
                           )
                    )
                , onClick (DurationPresetSelected mins)
                ]
                [ text (String.fromInt mins ++ " min") ]
    in
    div [ class "px-6 py-6" ]
        [ h3 [ class "text-lg font-semibold text-gray-800 mb-4" ] [ text "How long should the meeting be?" ]
        , div [ class "grid grid-cols-2 gap-3 mb-3" ]
            (List.map presetButton presets)
        , button
            [ type_ "button"
            , class
                ("w-full px-4 py-3 rounded-lg border text-sm font-medium transition-colors "
                    ++ (if isCustomSelected then
                            "border-blue-500 bg-blue-50 text-blue-700"

                        else
                            "border-gray-200 hover:border-blue-400 hover:bg-blue-50 text-gray-700"
                       )
                )
            , onClick CustomDurationSelected
            ]
            [ text "Custom duration" ]
        , if isCustomSelected then
            div [ class "mt-3" ]
                [ input
                    [ type_ "number"
                    , class "w-full border border-gray-300 rounded-lg px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    , placeholder "Minutes"
                    , value model.customDuration
                    , onInput CustomDurationUpdated
                    ]
                    []
                ]

          else
            text ""
        , navigationButtons
            { showBack = True
            , nextLabel = "Next"
            , nextDisabled = model.durationChoice == Nothing
            , onNext = DurationStepCompleted
            , loading = False
            }
        ]



-- Availability step


viewAvailabilityStep : Model -> Html Msg
viewAvailabilityStep model =
    div [ class "px-6 py-6" ]
        [ h3 [ class "text-lg font-semibold text-gray-800 mb-2" ] [ text "When are you available?" ]
        , p [ class "text-sm text-gray-500 mb-4" ]
            [ text "Describe your availability in plain language. For example: \"Tomorrow between 9am and 5pm\" or \"Next Monday afternoon\"." ]
        , textarea
            [ class "w-full border border-gray-300 rounded-lg px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            , placeholder "I'm available..."
            , value model.availabilityText
            , onInput AvailabilityTextUpdated
            , rows 3
            ]
            []
        , navigationButtons
            { showBack = True
            , nextLabel =
                if model.loading then
                    "Parsing..."

                else
                    "Find slots"
            , nextDisabled = model.loading || String.isEmpty (String.trim model.availabilityText)
            , onNext = AvailabilityStepCompleted
            , loading = model.loading
            }
        ]



-- Slot selection step


viewSlotSelectionStep : Model -> Html Msg
viewSlotSelectionStep model =
    div [ class "px-6 py-6" ]
        [ h3 [ class "text-lg font-semibold text-gray-800 mb-4" ] [ text "Pick a time slot" ]
        , if List.isEmpty model.slots then
            div []
                [ p [ class "text-gray-500 text-sm mb-3" ] [ text "No available slots found for the given availability." ]
                , button
                    [ class "text-blue-600 text-sm hover:underline"
                    , onClick BackStepClicked
                    ]
                    [ text "Try different availability" ]
                ]

          else
            Keyed.node "div"
                [ class "space-y-2 max-h-64 overflow-y-auto" ]
                (List.map (\slot -> ( slot.start, slotButton slot )) model.slots)
        , div [ class "mt-4" ]
            [ button
                [ class "text-sm text-gray-500 hover:text-gray-700"
                , onClick BackStepClicked
                ]
                [ text "Back" ]
            ]
        ]


slotButton : TimeSlot -> Html Msg
slotButton slot =
    button
        [ class "w-full text-left px-4 py-3 rounded-lg border border-gray-200 hover:border-blue-400 hover:bg-blue-50 transition-colors text-sm"
        , onClick (SlotSelected slot)
        ]
        [ div [ class "font-medium text-gray-800" ] [ text (formatSlotDate slot.start) ]
        , div [ class "text-gray-500 text-xs" ]
            [ text (formatSlotTimeOnly slot.start ++ " -- " ++ formatSlotTimeOnly slot.end) ]
        ]


formatSlotDate : String -> String
formatSlotDate isoString =
    String.left 10 isoString


formatSlotTimeOnly : String -> String
formatSlotTimeOnly isoString =
    String.slice 11 16 isoString


formatSlotTime : String -> String
formatSlotTime isoString =
    formatSlotDate isoString ++ " " ++ formatSlotTimeOnly isoString



-- Contact info step


viewContactInfoStep : Model -> Html Msg
viewContactInfoStep model =
    div [ class "px-6 py-6" ]
        [ h3 [ class "text-lg font-semibold text-gray-800 mb-4" ] [ text "Your contact information" ]
        , Html.form [ onSubmit ContactInfoStepCompleted ]
            [ div [ class "space-y-4" ]
                [ div []
                    [ label [ for "name-input", class "block text-sm font-medium text-gray-700 mb-1" ] [ text "Name" ]
                    , input
                        [ type_ "text"
                        , id "name-input"
                        , class "w-full border border-gray-300 rounded-lg px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                        , placeholder "Your name"
                        , value model.name
                        , onInput NameUpdated
                        ]
                        []
                    ]
                , div []
                    [ label [ for "email-input", class "block text-sm font-medium text-gray-700 mb-1" ] [ text "Email" ]
                    , input
                        [ type_ "email"
                        , id "email-input"
                        , class "w-full border border-gray-300 rounded-lg px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                        , placeholder "you@example.com"
                        , value model.email
                        , onInput EmailUpdated
                        ]
                        []
                    ]
                , div []
                    [ label [ for "phone-input", class "block text-sm font-medium text-gray-700 mb-1" ]
                        [ text "Phone "
                        , span [ class "text-gray-400 font-normal" ] [ text "(optional)" ]
                        ]
                    , input
                        [ type_ "tel"
                        , id "phone-input"
                        , class "w-full border border-gray-300 rounded-lg px-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                        , placeholder "+1 (555) 123-4567"
                        , value model.phone
                        , onInput PhoneUpdated
                        ]
                        []
                    ]
                ]
            , navigationButtons
                { showBack = True
                , nextLabel = "Next"
                , nextDisabled = False
                , onNext = ContactInfoStepCompleted
                , loading = False
                }
            ]
        ]



-- Confirmation step


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
                    formatSlotTime slot.start ++ " -- " ++ formatSlotTime slot.end

                Nothing ->
                    "--"
    in
    div [ class "px-6 py-6" ]
        [ h3 [ class "text-lg font-semibold text-gray-800 mb-4" ] [ text "Confirm your booking" ]
        , div [ class "bg-gray-50 rounded-lg p-4 space-y-2 text-sm" ]
            [ summaryField "Topic" model.title
            , summaryField "Duration" durationText
            , summaryField "Time" slotText
            , summaryField "Name" model.name
            , summaryField "Email" model.email
            , if String.isEmpty (String.trim model.phone) then
                text ""

              else
                summaryField "Phone" model.phone
            ]
        , navigationButtons
            { showBack = True
            , nextLabel =
                if model.loading then
                    "Booking..."

                else
                    "Confirm Booking"
            , nextDisabled = model.loading
            , onNext = BookingConfirmed
            , loading = model.loading
            }
        ]


summaryField : String -> String -> Html msg
summaryField fieldLabel fieldValue =
    div []
        [ span [ class "font-medium text-gray-600" ] [ text (fieldLabel ++ ": ") ]
        , span [ class "text-gray-800" ] [ text fieldValue ]
        ]



-- Complete step


viewCompleteStep : Model -> Html msg
viewCompleteStep model =
    div [ class "px-6 py-8 text-center" ]
        [ div [ class "text-green-500 text-5xl mb-4" ] [ text "OK" ]
        , h3 [ class "text-xl font-semibold text-gray-800 mb-2" ] [ text "Booking Confirmed!" ]
        , case model.bookingResult of
            Just result ->
                p [ class "text-gray-500 text-sm" ]
                    [ text ("Booking ID: " ++ result.bookingId) ]

            Nothing ->
                text ""
        , p [ class "text-gray-600 mt-4" ]
            [ text "You'll receive a confirmation email shortly." ]
        ]



-- Navigation buttons


type alias NavConfig =
    { showBack : Bool
    , nextLabel : String
    , nextDisabled : Bool
    , onNext : Msg
    , loading : Bool
    }


navigationButtons : NavConfig -> Html Msg
navigationButtons config =
    div [ class "flex space-x-3 mt-6" ]
        [ if config.showBack then
            button
                [ type_ "button"
                , class "flex-1 bg-gray-200 text-gray-700 py-2 px-4 rounded-lg hover:bg-gray-300 font-medium text-sm"
                , onClick BackStepClicked
                , disabled config.loading
                ]
                [ text "Back" ]

          else
            text ""
        , button
            [ type_ "button"
            , class "flex-1 bg-blue-600 text-white py-2 px-4 rounded-lg hover:bg-blue-700 font-medium text-sm disabled:opacity-50"
            , onClick config.onNext
            , disabled config.nextDisabled
            ]
            [ text config.nextLabel ]
        ]
