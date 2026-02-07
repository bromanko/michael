module Page.CalendarView exposing (Model, Msg, addDaysToDate, getDaysInMonth, init, isLeapYear, update, view)

import Api
import Dict exposing (Dict)
import Html exposing (Html, button, div, text)
import Html.Attributes exposing (class, style)
import Html.Events exposing (onClick)
import Http
import Types exposing (CalendarEvent, CalendarEventType(..))
import View.Components exposing (errorBanner, loadingSpinner, pageHeading)


type alias Model =
    { events : List CalendarEvent
    , loading : Bool
    , error : Maybe String
    , currentWeekStart : String -- ISO date string (YYYY-MM-DD)
    , timezone : String
    , todayDate : String -- ISO date string (YYYY-MM-DD) from flags
    }


type Msg
    = EventsReceived (Result Http.Error (List CalendarEvent))
    | PreviousWeekClicked
    | NextWeekClicked
    | TodayClicked


init : String -> String -> ( Model, Cmd Msg )
init timezone currentDate =
    let
        weekStart =
            mondayOfWeek currentDate
    in
    ( { events = []
      , loading = True
      , error = Nothing
      , currentWeekStart = weekStart
      , timezone = timezone
      , todayDate = currentDate
      }
    , fetchWeekEvents weekStart timezone
    )


fetchWeekEvents : String -> String -> Cmd Msg
fetchWeekEvents weekStart timezone =
    let
        -- weekStart is "YYYY-MM-DD", we need ISO instant format
        startInstant =
            weekStart ++ "T00:00:00Z"

        -- End is 7 days later
        endInstant =
            addDaysToDate weekStart 7 ++ "T00:00:00Z"
    in
    Api.fetchCalendarView startInstant endInstant timezone EventsReceived


mondayOfWeek : String -> String
mondayOfWeek dateStr =
    let
        dayName =
            getDayName dateStr

        offset =
            case dayName of
                "Mon" ->
                    0

                "Tue" ->
                    -1

                "Wed" ->
                    -2

                "Thu" ->
                    -3

                "Fri" ->
                    -4

                "Sat" ->
                    -5

                "Sun" ->
                    -6

                _ ->
                    0
    in
    addDaysToDate dateStr offset


addDaysToDate : String -> Int -> String
addDaysToDate dateStr days =
    -- Simple date arithmetic (assumes YYYY-MM-DD format)
    -- This is a naive implementation; in production use elm/time
    let
        parts =
            String.split "-" dateStr

        ( year, month, day ) =
            case parts of
                [ y, m, d ] ->
                    ( String.toInt y |> Maybe.withDefault 2026
                    , String.toInt m |> Maybe.withDefault 1
                    , String.toInt d |> Maybe.withDefault 1
                    )

                _ ->
                    ( 2026, 1, 1 )

        newDay =
            day + days

        daysInMonth =
            getDaysInMonth year month

        ( finalYear, finalMonth, finalDay ) =
            if newDay > daysInMonth then
                let
                    nextMonth =
                        if month == 12 then
                            1

                        else
                            month + 1

                    nextYear =
                        if month == 12 then
                            year + 1

                        else
                            year
                in
                ( nextYear, nextMonth, newDay - daysInMonth )

            else if newDay < 1 then
                let
                    prevMonth =
                        if month == 1 then
                            12

                        else
                            month - 1

                    prevYear =
                        if month == 1 then
                            year - 1

                        else
                            year

                    prevDays =
                        getDaysInMonth prevYear prevMonth
                in
                ( prevYear, prevMonth, prevDays + newDay )

            else
                ( year, month, newDay )
    in
    String.fromInt finalYear
        ++ "-"
        ++ String.padLeft 2 '0' (String.fromInt finalMonth)
        ++ "-"
        ++ String.padLeft 2 '0' (String.fromInt finalDay)


getDaysInMonth : Int -> Int -> Int
getDaysInMonth year month =
    case month of
        1 ->
            31

        2 ->
            if isLeapYear year then
                29

            else
                28

        3 ->
            31

        4 ->
            30

        5 ->
            31

        6 ->
            30

        7 ->
            31

        8 ->
            31

        9 ->
            30

        10 ->
            31

        11 ->
            30

        12 ->
            31

        _ ->
            30


isLeapYear : Int -> Bool
isLeapYear year =
    (modBy 4 year == 0) && (modBy 100 year /= 0 || modBy 400 year == 0)


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        EventsReceived (Ok events) ->
            ( { model | events = events, loading = False }, Cmd.none )

        EventsReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load calendar events." }, Cmd.none )

        PreviousWeekClicked ->
            let
                newWeekStart =
                    addDaysToDate model.currentWeekStart -7
            in
            ( { model | currentWeekStart = newWeekStart, loading = True }
            , fetchWeekEvents newWeekStart model.timezone
            )

        NextWeekClicked ->
            let
                newWeekStart =
                    addDaysToDate model.currentWeekStart 7
            in
            ( { model | currentWeekStart = newWeekStart, loading = True }
            , fetchWeekEvents newWeekStart model.timezone
            )

        TodayClicked ->
            let
                weekStart =
                    mondayOfWeek model.todayDate
            in
            ( { model | currentWeekStart = weekStart, loading = True }
            , fetchWeekEvents weekStart model.timezone
            )


view : Model -> Html Msg
view model =
    div []
        [ pageHeading "Calendar"
        , case model.error of
            Just err ->
                errorBanner err

            Nothing ->
                text ""
        , navigationBar model
        , if model.loading then
            loadingSpinner

          else
            weekView model
        ]


navigationBar : Model -> Html Msg
navigationBar model =
    div [ class "flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 mb-6" ]
        [ div [ class "flex items-center space-x-2" ]
            [ button
                [ onClick PreviousWeekClicked
                , class "px-3 py-2 border border-sand-300 rounded-md hover:bg-sand-100 text-sand-700 text-sm"
                ]
                [ text "←" ]
            , button
                [ onClick TodayClicked
                , class "px-3 py-2 border border-sand-300 rounded-md hover:bg-sand-100 text-sand-700 text-sm"
                ]
                [ text "Today" ]
            , button
                [ onClick NextWeekClicked
                , class "px-3 py-2 border border-sand-300 rounded-md hover:bg-sand-100 text-sand-700 text-sm"
                ]
                [ text "→" ]
            ]
        , div [ class "text-base sm:text-lg font-medium text-sand-700" ]
            [ text (formatWeekRange model.currentWeekStart) ]
        ]


formatWeekRange : String -> String
formatWeekRange weekStart =
    let
        weekEnd =
            addDaysToDate weekStart 6
    in
    weekStart ++ " — " ++ weekEnd


groupEventsByDate : List CalendarEvent -> Dict String (List CalendarEvent)
groupEventsByDate events =
    List.foldl
        (\event acc ->
            let
                dateStr =
                    String.left 10 event.start
            in
            Dict.update dateStr
                (\existing ->
                    case existing of
                        Just list ->
                            Just (event :: list)

                        Nothing ->
                            Just [ event ]
                )
                acc
        )
        Dict.empty
        events


weekView : Model -> Html Msg
weekView model =
    let
        days =
            List.range 0 6
                |> List.map (\offset -> addDaysToDate model.currentWeekStart offset)

        timedEvents =
            List.filter (\e -> not e.isAllDay) model.events

        allDayEvents =
            List.filter .isAllDay model.events

        eventsByDate =
            groupEventsByDate timedEvents

        allDayByDate =
            groupEventsByDate allDayEvents

        hasAllDay =
            not (List.isEmpty allDayEvents)
    in
    div [ class "bg-white rounded-lg shadow-sm border border-sand-200 overflow-x-auto" ]
        [ div [ class "min-w-[700px]" ]
            [ -- Header row with day names (with left gutter for time labels)
              div [ class "grid grid-cols-[3.5rem_repeat(7,1fr)] border-b border-sand-200" ]
                (div [ class "border-r border-sand-200" ] []
                    :: List.map dayHeader days
                )
            , -- All-day events row (only rendered when there are all-day events)
              if hasAllDay then
                div [ class "grid grid-cols-[3.5rem_repeat(7,1fr)] border-b border-sand-200" ]
                    (div [ class "border-r border-sand-200 text-xs text-sand-400 flex items-center justify-end pr-1 text-right overflow-hidden" ]
                        [ text "all day" ]
                        :: List.map (allDayColumn allDayByDate) days
                    )

              else
                text ""
            , -- Scrollable time grid container
              div [ class "overflow-y-auto", style "max-height" "calc(100vh - 280px)" ]
                [ -- Time grid with left gutter (6am to 10pm = 16 hours)
                  div [ class "grid grid-cols-[3.5rem_1fr]", style "height" "800px" ]
                    [ -- Time labels column
                      div [ class "relative border-r border-sand-200" ]
                        (List.range 6 22
                            |> List.map hourLabel
                        )
                    , -- Days grid
                      div [ class "relative" ]
                        [ -- Hour lines
                          div [ class "absolute inset-0" ]
                            (List.range 6 22
                                |> List.map hourLine
                            )
                        , -- Events overlay
                          div [ class "absolute inset-0 grid grid-cols-7" ]
                            (List.map (dayColumn eventsByDate) days)
                        ]
                    ]
                ]
            ]
        ]


dayHeader : String -> Html Msg
dayHeader dateStr =
    let
        dayName =
            getDayName dateStr
    in
    div [ class "py-3 px-2 text-center border-r border-sand-200 last:border-r-0" ]
        [ div [ class "text-xs text-sand-500 uppercase" ]
            [ text dayName ]
        , div [ class "text-lg font-medium text-sand-900" ]
            [ text (String.right 2 dateStr) ]
        ]


getDayName : String -> String
getDayName dateStr =
    -- Simple day name lookup (assumes week starts on Monday)
    let
        parts =
            String.split "-" dateStr

        ( year, month, day ) =
            case parts of
                [ yStr, mStr, dStr ] ->
                    ( String.toInt yStr |> Maybe.withDefault 2026
                    , String.toInt mStr |> Maybe.withDefault 1
                    , String.toInt dStr |> Maybe.withDefault 1
                    )

                _ ->
                    ( 2026, 1, 1 )

        -- Zeller's congruence for day of week
        adjustedMonth =
            if month < 3 then
                month + 12

            else
                month

        adjustedYear =
            if month < 3 then
                year - 1

            else
                year

        q =
            day

        zellerMonth =
            adjustedMonth

        k =
            modBy 100 adjustedYear

        j =
            adjustedYear // 100

        h =
            modBy 7 (q + (13 * (zellerMonth + 1) // 5) + k + (k // 4) + (j // 4) - (2 * j))

        dayOfWeek =
            modBy 7 (h + 5)
    in
    case dayOfWeek of
        0 ->
            "Mon"

        1 ->
            "Tue"

        2 ->
            "Wed"

        3 ->
            "Thu"

        4 ->
            "Fri"

        5 ->
            "Sat"

        6 ->
            "Sun"

        _ ->
            "?"


hourLabel : Int -> Html Msg
hourLabel hour =
    let
        topPercent =
            toFloat (hour - 6) / 16.0 * 100.0

        displayHour =
            if hour == 12 then
                "12pm"

            else if hour > 12 then
                String.fromInt (hour - 12) ++ "pm"

            else
                String.fromInt hour ++ "am"
    in
    div
        [ class "absolute right-2 text-xs text-sand-400"
        , style "top" ("calc(" ++ String.fromFloat topPercent ++ "% + 0.25rem)")
        ]
        [ text displayHour ]


hourLine : Int -> Html Msg
hourLine hour =
    let
        topPercent =
            toFloat (hour - 6) / 16.0 * 100.0
    in
    div
        [ class "absolute left-0 right-0 border-t border-sand-100"
        , style "top" (String.fromFloat topPercent ++ "%")
        ]
        []


allDayColumn : Dict String (List CalendarEvent) -> String -> Html Msg
allDayColumn allDayByDate dateStr =
    let
        dayEvents =
            Dict.get dateStr allDayByDate
                |> Maybe.withDefault []
    in
    div [ class "border-r border-sand-200 last:border-r-0 py-1 px-0.5 space-y-0.5" ]
        (List.map allDayBlock dayEvents)


allDayBlock : CalendarEvent -> Html Msg
allDayBlock event =
    let
        ( bgColor, textColor ) =
            case event.eventType of
                ExternalCalendarEvent ->
                    ( "bg-blue-100", "text-blue-800" )

                BookingEvent ->
                    ( "bg-coral-light", "text-coral-dark" )

                AvailabilityEvent ->
                    ( "bg-green-100", "text-green-800" )
    in
    div
        [ class ("rounded px-1.5 py-0.5 " ++ bgColor ++ " " ++ textColor) ]
        [ div [ class "text-xs font-medium truncate" ]
            [ text event.title ]
        ]


dayColumn : Dict String (List CalendarEvent) -> String -> Html Msg
dayColumn eventsByDate dateStr =
    let
        dayEvents =
            Dict.get dateStr eventsByDate
                |> Maybe.withDefault []
    in
    div [ class "relative border-r border-sand-200 last:border-r-0" ]
        (List.map eventBlock dayEvents)


eventBlock : CalendarEvent -> Html Msg
eventBlock event =
    let
        ( bgColor, textColor ) =
            case event.eventType of
                ExternalCalendarEvent ->
                    ( "bg-blue-100", "text-blue-800" )

                BookingEvent ->
                    ( "bg-coral-light", "text-coral-dark" )

                AvailabilityEvent ->
                    ( "bg-green-100", "text-green-800" )

        -- Parse start time to get position
        startHour =
            String.slice 11 13 event.start
                |> String.toInt
                |> Maybe.withDefault 9

        startMinute =
            String.slice 14 16 event.start
                |> String.toInt
                |> Maybe.withDefault 0

        endHour =
            String.slice 11 13 event.end
                |> String.toInt
                |> Maybe.withDefault (startHour + 1)

        endMinute =
            String.slice 14 16 event.end
                |> String.toInt
                |> Maybe.withDefault 0

        -- Calculate position (6am = 0%, 10pm = 100%)
        startPercent =
            (toFloat startHour - 6 + toFloat startMinute / 60) / 16.0 * 100.0

        durationHours =
            toFloat endHour - toFloat startHour + (toFloat endMinute - toFloat startMinute) / 60

        heightPercent =
            durationHours / 16.0 * 100.0
    in
    div
        [ class ("absolute left-0.5 right-0.5 rounded px-1 py-0.5 overflow-hidden " ++ bgColor ++ " " ++ textColor)
        , style "top" (String.fromFloat (max 0 startPercent) ++ "%")
        , style "height" (String.fromFloat (max 2 heightPercent) ++ "%")
        ]
        [ div [ class "text-xs font-medium truncate" ]
            [ text event.title ]
        ]
