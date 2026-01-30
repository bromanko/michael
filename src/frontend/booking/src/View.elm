module View exposing (view)

import Html exposing (Html, button, div, h1, h3, input, p, span, text)
import Html.Attributes exposing (class, disabled, id, placeholder, type_, value)
import Html.Events exposing (onClick, onInput, onSubmit)
import Html.Keyed as Keyed
import Model exposing (Model)
import Types exposing (ChatMessage, ConversationPhase(..), MessageRole(..), TimeSlot)
import Update exposing (Msg(..))


view : Model -> Html Msg
view model =
    div [ class "bg-white rounded-lg shadow-lg overflow-hidden" ]
        [ header
        , chatMessages model
        , phaseContent model
        , case model.phase of
            Chatting ->
                inputArea model

            _ ->
                text ""
        ]


header : Html msg
header =
    div [ class "bg-blue-600 text-white px-6 py-4" ]
        [ h1 [ class "text-xl font-semibold" ] [ text "Book a Meeting" ]
        , p [ class "text-blue-100 text-sm mt-1" ] [ text "Tell me when you're available" ]
        ]


chatMessages : Model -> Html msg
chatMessages model =
    Keyed.node "div"
        [ class "px-6 py-4 space-y-3 max-h-96 overflow-y-auto", id "chat-messages" ]
        (List.indexedMap (\i m -> ( String.fromInt i, viewMessage m )) model.messages)


viewMessage : ChatMessage -> Html msg
viewMessage msg =
    let
        ( alignment, bgColor, textColor ) =
            case msg.role of
                User ->
                    ( "ml-auto", "bg-blue-500", "text-white" )

                System ->
                    ( "mr-auto", "bg-gray-100", "text-gray-800" )
    in
    div [ class ("max-w-xs lg:max-w-md px-4 py-2 rounded-lg " ++ alignment ++ " " ++ bgColor ++ " " ++ textColor) ]
        [ p [ class "text-sm" ] [ text msg.content ] ]


phaseContent : Model -> Html Msg
phaseContent model =
    case model.phase of
        Chatting ->
            div []
                [ if model.loading then
                    loadingIndicator

                  else
                    text ""
                , errorBanner model
                ]

        ConfirmingParse ->
            confirmParseView model

        SelectingSlot ->
            if model.loading then
                loadingIndicator

            else
                selectSlotView model

        ConfirmingBooking ->
            confirmBookingView model

        BookingComplete ->
            bookingCompleteView model


loadingIndicator : Html msg
loadingIndicator =
    div [ class "px-6 py-3" ]
        [ div [ class "flex items-center space-x-2 text-gray-500" ]
            [ div [ class "animate-pulse flex space-x-1" ]
                [ span [ class "w-2 h-2 bg-gray-400 rounded-full" ] []
                , span [ class "w-2 h-2 bg-gray-400 rounded-full" ] []
                , span [ class "w-2 h-2 bg-gray-400 rounded-full" ] []
                ]
            , span [ class "text-sm" ] [ text "Thinking..." ]
            ]
        ]


confirmParseView : Model -> Html Msg
confirmParseView model =
    div [ class "px-6 py-4 border-t border-gray-200" ]
        [ h3 [ class "font-semibold text-gray-700 mb-3" ] [ text "Does this look right?" ]
        , div [ class "bg-gray-50 rounded-lg p-4 space-y-2 text-sm" ]
            [ summaryField "Name" model.accumulated.name
            , summaryField "Email" model.accumulated.email
            , summaryField "Topic" model.accumulated.title
            , summaryField "Duration"
                (model.accumulated.durationMinutes
                    |> Maybe.map (\d -> String.fromInt d ++ " minutes")
                )
            , div []
                [ span [ class "font-medium text-gray-600" ] [ text "Availability: " ]
                , span [ class "text-gray-800" ]
                    [ text
                        (String.fromInt (List.length model.accumulated.availabilityWindows)
                            ++ " window(s)"
                        )
                    ]
                ]
            ]
        , errorBanner model
        , div [ class "flex space-x-3 mt-4" ]
            [ button
                [ class "flex-1 bg-blue-600 text-white py-2 px-4 rounded-lg hover:bg-blue-700 font-medium"
                , onClick ParseConfirmed
                ]
                [ text "Looks good!" ]
            , button
                [ class "flex-1 bg-gray-200 text-gray-700 py-2 px-4 rounded-lg hover:bg-gray-300 font-medium"
                , onClick ParseRejected
                ]
                [ text "Let me correct that" ]
            ]
        ]


summaryField : String -> Maybe String -> Html msg
summaryField label value =
    div []
        [ span [ class "font-medium text-gray-600" ] [ text (label ++ ": ") ]
        , span [ class "text-gray-800" ]
            [ text (Maybe.withDefault "—" value) ]
        ]


selectSlotView : Model -> Html Msg
selectSlotView model =
    div [ class "px-6 py-4 border-t border-gray-200" ]
        [ h3 [ class "font-semibold text-gray-700 mb-3" ] [ text "Pick a time slot" ]
        , if List.isEmpty model.slots then
            div []
                [ p [ class "text-gray-500 text-sm" ] [ text "No available slots found." ]
                , button
                    [ class "mt-3 text-blue-600 text-sm hover:underline"
                    , onClick ParseRejected
                    ]
                    [ text "Try different availability" ]
                ]

          else
            Keyed.node "div"
                [ class "space-y-2 max-h-64 overflow-y-auto" ]
                (List.map (\slot -> ( slot.start, slotButton slot )) model.slots)
        , errorBanner model
        ]


slotButton : TimeSlot -> Html Msg
slotButton slot =
    button
        [ class "w-full text-left px-4 py-3 rounded-lg border border-gray-200 hover:border-blue-400 hover:bg-blue-50 transition-colors text-sm"
        , onClick (SlotSelected slot)
        ]
        [ div [ class "font-medium text-gray-800" ] [ text (formatSlotDate slot.start) ]
        , div [ class "text-gray-500 text-xs" ]
            [ text (formatSlotTimeOnly slot.start ++ " — " ++ formatSlotTimeOnly slot.end) ]
        ]


formatSlotDate : String -> String
formatSlotDate isoString =
    -- "2026-02-03T09:00:00-05:00" -> "2026-02-03"
    String.left 10 isoString


formatSlotTimeOnly : String -> String
formatSlotTimeOnly isoString =
    -- "2026-02-03T09:00:00-05:00" -> "09:00"
    String.slice 11 16 isoString


formatSlotTime : String -> String
formatSlotTime isoString =
    formatSlotDate isoString ++ " " ++ formatSlotTimeOnly isoString


confirmBookingView : Model -> Html Msg
confirmBookingView model =
    case model.selectedSlot of
        Just slot ->
            div [ class "px-6 py-4 border-t border-gray-200" ]
                [ h3 [ class "font-semibold text-gray-700 mb-3" ] [ text "Confirm your booking" ]
                , div [ class "bg-gray-50 rounded-lg p-4 space-y-2 text-sm" ]
                    [ summaryField "Name" model.accumulated.name
                    , summaryField "Email" model.accumulated.email
                    , summaryField "Topic" model.accumulated.title
                    , summaryField "Duration"
                        (model.accumulated.durationMinutes
                            |> Maybe.map (\d -> String.fromInt d ++ " minutes")
                        )
                    , summaryField "Time" (Just (formatSlotTime slot.start ++ " — " ++ formatSlotTime slot.end))
                    ]
                , errorBanner model
                , div [ class "flex space-x-3 mt-4" ]
                    [ button
                        [ class "flex-1 bg-green-600 text-white py-2 px-4 rounded-lg hover:bg-green-700 font-medium"
                        , onClick BookingConfirmed
                        , disabled model.loading
                        ]
                        [ if model.loading then
                            text "Booking..."

                          else
                            text "Confirm Booking"
                        ]
                    , button
                        [ class "flex-1 bg-gray-200 text-gray-700 py-2 px-4 rounded-lg hover:bg-gray-300 font-medium"
                        , onClick ParseRejected
                        , disabled model.loading
                        ]
                        [ text "Start over" ]
                    ]
                ]

        Nothing ->
            text ""


bookingCompleteView : Model -> Html msg
bookingCompleteView model =
    div [ class "px-6 py-8 border-t border-gray-200 text-center" ]
        [ div [ class "text-green-500 text-5xl mb-4" ] [ text "✓" ]
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


errorBanner : Model -> Html msg
errorBanner model =
    case model.error of
        Just err ->
            div [ class "mt-3 p-3 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm" ]
                [ text err ]

        Nothing ->
            text ""


inputArea : Model -> Html Msg
inputArea model =
    div [ class "px-6 py-4 border-t border-gray-200" ]
        [ Html.form [ onSubmit MessageSubmitted, class "flex space-x-2" ]
            [ input
                [ type_ "text"
                , class "flex-1 border border-gray-300 rounded-lg px-4 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                , placeholder "Type your availability..."
                , value model.inputText
                , onInput InputUpdated
                , disabled model.loading
                ]
                []
            , button
                [ type_ "submit"
                , class "bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 font-medium text-sm disabled:opacity-50"
                , disabled (model.loading || String.isEmpty (String.trim model.inputText))
                ]
                [ text "Send" ]
            ]
        ]
