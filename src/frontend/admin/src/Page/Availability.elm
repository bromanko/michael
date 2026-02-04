module Page.Availability exposing (Model, Msg, init, update, view)

import Api
import Html exposing (Html, button, div, input, option, p, select, table, tbody, td, text, th, thead, tr)
import Html.Attributes exposing (class, disabled, selected, type_, value)
import Html.Events exposing (onClick, onInput)
import Http
import Types exposing (AvailabilitySlot, DayOfWeek(..))
import View.Components exposing (card, dangerButton, errorBanner, loadingSpinner, pageHeading, primaryButton, secondaryButton)


type alias EditSlot =
    { dayOfWeek : DayOfWeek
    , startTime : String
    , endTime : String
    , timezone : String
    }


type alias Model =
    { slots : List AvailabilitySlot
    , editSlots : List EditSlot
    , loading : Bool
    , saving : Bool
    , editing : Bool
    , error : Maybe String
    , success : Maybe String
    }


type Msg
    = SlotsReceived (Result Http.Error (List AvailabilitySlot))
    | EditStarted
    | EditCancelled
    | SlotDayChanged Int String
    | SlotStartTimeChanged Int String
    | SlotEndTimeChanged Int String
    | SlotTimezoneChanged Int String
    | SlotRemoved Int
    | SlotAdded
    | SaveClicked
    | SaveCompleted (Result Http.Error (List AvailabilitySlot))


init : ( Model, Cmd Msg )
init =
    ( { slots = []
      , editSlots = []
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
        SlotsReceived (Ok slots) ->
            ( { model | slots = slots, loading = False }, Cmd.none )

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

        SlotTimezoneChanged index tz ->
            ( { model | editSlots = updateSlotAt index (\s -> { s | timezone = tz }) model.editSlots }
            , Cmd.none
            )

        SlotRemoved index ->
            ( { model | editSlots = removeAt index model.editSlots }, Cmd.none )

        SlotAdded ->
            let
                defaultTz =
                    case model.editSlots of
                        first :: _ ->
                            first.timezone

                        [] ->
                            "America/New_York"

                newSlot =
                    { dayOfWeek = Monday
                    , startTime = "09:00"
                    , endTime = "17:00"
                    , timezone = defaultTz
                    }
            in
            ( { model | editSlots = model.editSlots ++ [ newSlot ] }, Cmd.none )

        SaveClicked ->
            ( { model | saving = True, error = Nothing, success = Nothing }
            , Api.saveAvailability
                (List.map
                    (\s ->
                        { dayOfWeek = s.dayOfWeek
                        , startTime = s.startTime
                        , endTime = s.endTime
                        , timezone = s.timezone
                        }
                    )
                    model.editSlots
                )
                SaveCompleted
            )

        SaveCompleted (Ok slots) ->
            ( { model
                | slots = slots
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


slotToEdit : AvailabilitySlot -> EditSlot
slotToEdit slot =
    { dayOfWeek = slot.dayOfWeek
    , startTime = slot.startTime
    , endTime = slot.endTime
    , timezone = slot.timezone
    }


updateSlotAt : Int -> (EditSlot -> EditSlot) -> List EditSlot -> List EditSlot
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
    case str of
        "1" ->
            Monday

        "2" ->
            Tuesday

        "3" ->
            Wednesday

        "4" ->
            Thursday

        "5" ->
            Friday

        "6" ->
            Saturday

        "7" ->
            Sunday

        _ ->
            Monday


dayToString : DayOfWeek -> String
dayToString day =
    case day of
        Monday ->
            "1"

        Tuesday ->
            "2"

        Wednesday ->
            "3"

        Thursday ->
            "4"

        Friday ->
            "5"

        Saturday ->
            "6"

        Sunday ->
            "7"


dayLabel : DayOfWeek -> String
dayLabel day =
    case day of
        Monday ->
            "Monday"

        Tuesday ->
            "Tuesday"

        Wednesday ->
            "Wednesday"

        Thursday ->
            "Thursday"

        Friday ->
            "Friday"

        Saturday ->
            "Saturday"

        Sunday ->
            "Sunday"



-- View


view : Model -> Html Msg
view model =
    div []
        [ div [ class "flex items-center justify-between mb-6" ]
            [ pageHeading "Availability"
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
                div [ class "mb-6 px-5 py-4 bg-green-50 border border-green-200 rounded-lg text-green-700 text-sm" ]
                    [ text msg ]

            Nothing ->
                text ""
        , if model.loading then
            loadingSpinner

          else if model.editing then
            editView model

          else
            readView model.slots
        ]


readView : List AvailabilitySlot -> Html Msg
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
                        , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Timezone" ]
                        ]
                    ]
                , tbody []
                    (List.map readSlotRow slots)
                ]
            ]


readSlotRow : AvailabilitySlot -> Html msg
readSlotRow slot =
    tr [ class "border-b border-sand-100" ]
        [ td [ class "px-6 py-4 text-sm font-medium text-sand-900" ]
            [ text (dayLabel slot.dayOfWeek) ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text slot.startTime ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text slot.endTime ]
        , td [ class "px-6 py-4 text-sm text-sand-500" ]
            [ text slot.timezone ]
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


editSlotRow : Int -> EditSlot -> Html Msg
editSlotRow index slot =
    div [ class "flex items-center gap-3 flex-wrap" ]
        [ daySelect index slot.dayOfWeek
        , timeInput "Start" (SlotStartTimeChanged index) slot.startTime
        , p [ class "text-sand-400 text-sm" ] [ text "to" ]
        , timeInput "End" (SlotEndTimeChanged index) slot.endTime
        , timezoneInput index slot.timezone
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
                    [ value (dayToString day)
                    , selected (day == currentDay)
                    ]
                    [ text (dayLabel day) ]
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


timezoneInput : Int -> String -> Html Msg
timezoneInput index currentTz =
    input
        [ type_ "text"
        , class "border border-sand-300 rounded-lg px-3 py-2 text-sm text-sand-700 w-48"
        , value currentTz
        , onInput (SlotTimezoneChanged index)
        , Html.Attributes.placeholder "e.g. America/New_York"
        ]
        []
