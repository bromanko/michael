module Page.Availability exposing
    ( Model
    , Msg(..)
    , init
    , isEndAfterStart
    , isValidTime
    , update
    , validateSlots
    , view
    )

import Api exposing (AvailabilityResponse)
import Html exposing (Html, button, div, input, option, p, select, table, td, text, th, thead, tr)
import Html.Attributes exposing (class, selected, type_, value)
import Html.Events exposing (onClick, onInput)
import Html.Keyed as Keyed
import Html.Lazy exposing (lazy)
import Http
import Types exposing (AvailabilitySlot, AvailabilitySlotInput, DayOfWeek(..), dayOfWeekFromInt, dayOfWeekLabel, dayOfWeekToInt)
import View.Components exposing (card, errorBanner, formatTime12Hour, loadingSpinner, pageHeading, primaryButton, secondaryButton, successBanner)


type alias Model =
    { slots : List AvailabilitySlot
    , editSlots : List AvailabilitySlotInput
    , hostTimezone : String
    , loading : Bool
    , saving : Bool
    , editing : Bool
    , error : Maybe String
    , success : Maybe String
    }


type Msg
    = SlotsReceived (Result Http.Error AvailabilityResponse)
    | EditStarted
    | EditCancelled
    | SlotDayChanged Int String
    | SlotStartTimeChanged Int String
    | SlotEndTimeChanged Int String
    | SlotRemoved Int
    | SlotAdded
    | SaveClicked
    | SaveCompleted (Result Http.Error AvailabilityResponse)


init : ( Model, Cmd Msg )
init =
    ( { slots = []
      , editSlots = []
      , hostTimezone = ""
      , loading = True
      , saving = False
      , editing = False
      , error = Nothing
      , success = Nothing
      }
    , Api.fetchAvailability SlotsReceived
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        SlotsReceived (Ok response) ->
            ( { model | slots = response.slots, hostTimezone = response.timezone, loading = False }, Cmd.none )

        SlotsReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load availability." }, Cmd.none )

        EditStarted ->
            ( { model
                | editing = True
                , editSlots = List.map slotToEdit model.slots
                , error = Nothing
                , success = Nothing
              }
            , Cmd.none
            )

        EditCancelled ->
            ( { model | editing = False, editSlots = [], error = Nothing }, Cmd.none )

        SlotDayChanged index dayStr ->
            ( { model | editSlots = updateSlotAt index (\s -> { s | dayOfWeek = dayFromString dayStr }) model.editSlots }
            , Cmd.none
            )

        SlotStartTimeChanged index time ->
            ( { model | editSlots = updateSlotAt index (\s -> { s | startTime = time }) model.editSlots }
            , Cmd.none
            )

        SlotEndTimeChanged index time ->
            ( { model | editSlots = updateSlotAt index (\s -> { s | endTime = time }) model.editSlots }
            , Cmd.none
            )

        SlotRemoved index ->
            ( { model | editSlots = removeAt index model.editSlots }, Cmd.none )

        SlotAdded ->
            let
                newSlot =
                    { dayOfWeek = Monday
                    , startTime = "09:00"
                    , endTime = "17:00"
                    }
            in
            ( { model | editSlots = model.editSlots ++ [ newSlot ] }, Cmd.none )

        SaveClicked ->
            case validateSlots model.editSlots of
                Err validationError ->
                    ( { model | error = Just validationError }, Cmd.none )

                Ok _ ->
                    ( { model | saving = True, error = Nothing, success = Nothing }
                    , Api.saveAvailability model.editSlots SaveCompleted
                    )

        SaveCompleted (Ok response) ->
            ( { model
                | slots = response.slots
                , hostTimezone = response.timezone
                , editing = False
                , editSlots = []
                , saving = False
                , success = Just "Availability updated."
              }
            , Cmd.none
            )

        SaveCompleted (Err _) ->
            ( { model | saving = False, error = Just "Failed to save availability." }, Cmd.none )



-- Helpers


slotToEdit : AvailabilitySlot -> AvailabilitySlotInput
slotToEdit slot =
    { dayOfWeek = slot.dayOfWeek
    , startTime = slot.startTime
    , endTime = slot.endTime
    }


updateSlotAt : Int -> (AvailabilitySlotInput -> AvailabilitySlotInput) -> List AvailabilitySlotInput -> List AvailabilitySlotInput
updateSlotAt index fn slots =
    List.indexedMap
        (\i s ->
            if i == index then
                fn s

            else
                s
        )
        slots


removeAt : Int -> List a -> List a
removeAt index list =
    List.indexedMap Tuple.pair list
        |> List.filterMap
            (\( i, item ) ->
                if i == index then
                    Nothing

                else
                    Just item
            )


dayFromString : String -> DayOfWeek
dayFromString str =
    String.toInt str
        |> Maybe.andThen dayOfWeekFromInt
        |> Maybe.withDefault Monday



-- Validation


validateSlots : List AvailabilitySlotInput -> Result String (List AvailabilitySlotInput)
validateSlots slots =
    let
        errors =
            List.indexedMap validateSlot slots
                |> List.filterMap identity
    in
    case errors of
        [] ->
            Ok slots

        firstError :: _ ->
            Err firstError


validateSlot : Int -> AvailabilitySlotInput -> Maybe String
validateSlot index slot =
    let
        slotNum =
            String.fromInt (index + 1)
    in
    if not (isValidTime slot.startTime) then
        Just ("Slot " ++ slotNum ++ ": Invalid start time format.")

    else if not (isValidTime slot.endTime) then
        Just ("Slot " ++ slotNum ++ ": Invalid end time format.")

    else if not (isEndAfterStart slot.startTime slot.endTime) then
        Just ("Slot " ++ slotNum ++ ": End time must be after start time.")

    else
        Nothing


isValidTime : String -> Bool
isValidTime time =
    case String.split ":" time of
        [ hourStr, minStr ] ->
            case ( String.toInt hourStr, String.toInt minStr ) of
                ( Just h, Just m ) ->
                    h >= 0 && h <= 23 && m >= 0 && m <= 59

                _ ->
                    False

        _ ->
            False


isEndAfterStart : String -> String -> Bool
isEndAfterStart startTime endTime =
    -- Simple string comparison works for HH:MM format
    startTime < endTime



-- View


view : Model -> Html Msg
view model =
    div []
        [ div [ class "flex items-center justify-between mb-6" ]
            [ div []
                [ pageHeading "Availability"
                , if model.hostTimezone /= "" then
                    p [ class "text-sm text-sand-500 mt-1" ]
                        [ text ("All times in " ++ model.hostTimezone) ]

                  else
                    text ""
                ]
            , if not model.editing then
                secondaryButton
                    { label = "Edit"
                    , onPress = EditStarted
                    }

              else
                text ""
            ]
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

          else if model.editing then
            editView model

          else
            readView model.slots
        ]


readView : List AvailabilitySlot -> Html msg
readView slots =
    if List.isEmpty slots then
        card
            [ div [ class "text-center py-8" ]
                [ p [ class "text-sand-400 text-sm" ]
                    [ text "No availability slots configured." ]
                ]
            ]

    else
        div [ class "bg-white rounded-lg shadow-sm border border-sand-200 overflow-hidden" ]
            [ table [ class "w-full" ]
                [ thead []
                    [ tr [ class "border-b border-sand-200 bg-sand-50" ]
                        [ th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Day" ]
                        , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Start" ]
                        , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "End" ]
                        ]
                    ]
                , Keyed.node "tbody"
                    []
                    (List.map keyedReadSlotRow slots)
                ]
            ]


keyedReadSlotRow : AvailabilitySlot -> ( String, Html msg )
keyedReadSlotRow slot =
    ( slot.id, lazy readSlotRow slot )


readSlotRow : AvailabilitySlot -> Html msg
readSlotRow slot =
    tr [ class "border-b border-sand-100" ]
        [ td [ class "px-6 py-4 text-sm font-medium text-sand-900" ]
            [ text (dayOfWeekLabel slot.dayOfWeek) ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text (formatTime12Hour slot.startTime) ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text (formatTime12Hour slot.endTime) ]
        ]


editView : Model -> Html Msg
editView model =
    div []
        [ card
            [ if List.isEmpty model.editSlots then
                div [ class "text-center py-8" ]
                    [ p [ class "text-sand-400 text-sm mb-4" ]
                        [ text "No slots. Add one to define your availability." ]
                    ]

              else
                div [ class "space-y-4" ]
                    (List.indexedMap editSlotRow model.editSlots)
            , div [ class "mt-4 pt-4 border-t border-sand-200" ]
                [ button
                    [ class "text-sm text-coral hover:text-coral-dark transition-colors font-medium"
                    , onClick SlotAdded
                    ]
                    [ text "+ Add time slot" ]
                ]
            ]
        , div [ class "flex gap-3 mt-6" ]
            [ primaryButton
                { label = "Save"
                , onPress = SaveClicked
                , isDisabled = List.isEmpty model.editSlots
                , isLoading = model.saving
                }
            , secondaryButton
                { label = "Cancel"
                , onPress = EditCancelled
                }
            ]
        ]


editSlotRow : Int -> AvailabilitySlotInput -> Html Msg
editSlotRow index slot =
    div [ class "flex items-center gap-3 flex-wrap" ]
        [ daySelect index slot.dayOfWeek
        , timeInput "Start" (SlotStartTimeChanged index) slot.startTime
        , p [ class "text-sand-400 text-sm" ] [ text "to" ]
        , timeInput "End" (SlotEndTimeChanged index) slot.endTime
        , button
            [ class "text-sm text-red-500 hover:text-red-700 transition-colors"
            , onClick (SlotRemoved index)
            ]
            [ text "Remove" ]
        ]


daySelect : Int -> DayOfWeek -> Html Msg
daySelect index currentDay =
    select
        [ class "border border-sand-300 rounded-lg px-3 py-2 text-sm text-sand-700 bg-white"
        , onInput (SlotDayChanged index)
        ]
        (List.map
            (\day ->
                option
                    [ value (String.fromInt (dayOfWeekToInt day))
                    , selected (day == currentDay)
                    ]
                    [ text (dayOfWeekLabel day) ]
            )
            allDays
        )


allDays : List DayOfWeek
allDays =
    [ Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday ]


timeInput : String -> (String -> Msg) -> String -> Html Msg
timeInput label onChange currentValue =
    input
        [ type_ "time"
        , class "border border-sand-300 rounded-lg px-3 py-2 text-sm text-sand-700"
        , value currentValue
        , onInput onChange
        , Html.Attributes.title label
        ]
        []
