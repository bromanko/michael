module DateFormat exposing (formatFriendlyDate, formatFriendlyTime)

{-| Human-friendly formatting for ISO 8601 date/time strings.

ISO strings from the backend are like "2026-02-09T09:00:00-05:00".

-}

import Array exposing (Array)


{-| Format an ISO date string as "DayOfWeek, Mon DD".

    formatFriendlyDate "2026-02-09T09:00:00-05:00" == "Monday, Feb 9"

Falls back to the raw date portion if parsing fails.

-}
formatFriendlyDate : String -> String
formatFriendlyDate isoString =
    let
        datePart =
            String.left 10 isoString

        parts =
            String.split "-" datePart
    in
    case parts of
        [ yearStr, monthStr, dayStr ] ->
            case ( String.toInt yearStr, String.toInt monthStr, String.toInt dayStr ) of
                ( Just year, Just month, Just day ) ->
                    dayOfWeekName year month day
                        ++ ", "
                        ++ monthName month
                        ++ " "
                        ++ String.fromInt day

                _ ->
                    datePart

        _ ->
            datePart


{-| Format an ISO time string as 12-hour with AM/PM.

    formatFriendlyTime "2026-02-09T14:30:00-05:00" == "2:30 PM"

    formatFriendlyTime "2026-02-09T09:00:00-05:00" == "9 AM"

Minutes are omitted when `:00`. Falls back to raw time portion if parsing fails.

-}
formatFriendlyTime : String -> String
formatFriendlyTime isoString =
    let
        timePart =
            String.slice 11 16 isoString
    in
    case String.split ":" timePart of
        [ hourStr, minuteStr ] ->
            case String.toInt hourStr of
                Just hour ->
                    let
                        ( displayHour, period ) =
                            if hour == 0 then
                                ( 12, "AM" )

                            else if hour < 12 then
                                ( hour, "AM" )

                            else if hour == 12 then
                                ( 12, "PM" )

                            else
                                ( hour - 12, "PM" )

                        minuteSuffix =
                            if minuteStr == "00" then
                                ""

                            else
                                ":" ++ minuteStr
                    in
                    String.fromInt displayHour ++ minuteSuffix ++ " " ++ period

                Nothing ->
                    timePart

        _ ->
            timePart



-- Internal helpers


monthName : Int -> String
monthName m =
    case m of
        1 ->
            "Jan"

        2 ->
            "Feb"

        3 ->
            "Mar"

        4 ->
            "Apr"

        5 ->
            "May"

        6 ->
            "Jun"

        7 ->
            "Jul"

        8 ->
            "Aug"

        9 ->
            "Sep"

        10 ->
            "Oct"

        11 ->
            "Nov"

        12 ->
            "Dec"

        _ ->
            "???"


monthOffsets : Array Int
monthOffsets =
    Array.fromList [ 0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4 ]


dayOfWeekName : Int -> Int -> Int -> String
dayOfWeekName year month day =
    -- Tomohiko Sakamoto's day-of-week algorithm
    -- Returns 0=Sunday, 1=Monday, ..., 6=Saturday
    let
        y =
            if month < 3 then
                year - 1

            else
                year

        offsetForMonth =
            Array.get (month - 1) monthOffsets
                |> Maybe.withDefault 0

        dow =
            modBy 7 (y + y // 4 - y // 100 + y // 400 + offsetForMonth + day)
    in
    case dow of
        0 ->
            "Sunday"

        1 ->
            "Monday"

        2 ->
            "Tuesday"

        3 ->
            "Wednesday"

        4 ->
            "Thursday"

        5 ->
            "Friday"

        6 ->
            "Saturday"

        _ ->
            "???"
