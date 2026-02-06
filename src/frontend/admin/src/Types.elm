module Types exposing
    ( AvailabilitySlot
    , AvailabilitySlotInput
    , Booking
    , BookingStatus(..)
    , CalDavProvider(..)
    , CalendarEvent
    , CalendarEventType(..)
    , CalendarSource
    , DashboardStats
    , DayOfWeek(..)
    , PaginatedBookings
    , Route(..)
    , SchedulingSettings
    , Session(..)
    , StatusFilter(..)
    , dayOfWeekFromInt
    , dayOfWeekLabel
    , dayOfWeekToInt
    )


type BookingStatus
    = Confirmed
    | Cancelled


type alias Booking =
    { id : String
    , participantName : String
    , participantEmail : String
    , participantPhone : Maybe String
    , title : String
    , description : Maybe String
    , startTime : String
    , endTime : String
    , durationMinutes : Int
    , timezone : String
    , status : BookingStatus
    , createdAt : String
    }


type alias PaginatedBookings =
    { bookings : List Booking
    , totalCount : Int
    , page : Int
    , pageSize : Int
    }


type alias DashboardStats =
    { upcomingCount : Int
    , nextBookingTime : Maybe String
    , nextBookingTitle : Maybe String
    }


type CalDavProvider
    = Fastmail
    | ICloud


type alias CalendarSource =
    { id : String
    , provider : CalDavProvider
    , baseUrl : String
    , lastSyncedAt : Maybe String
    , lastSyncResult : Maybe String
    }


type DayOfWeek
    = Monday
    | Tuesday
    | Wednesday
    | Thursday
    | Friday
    | Saturday
    | Sunday


type alias AvailabilitySlotInput =
    { dayOfWeek : DayOfWeek
    , startTime : String
    , endTime : String
    }


type alias AvailabilitySlot =
    { id : String
    , dayOfWeek : DayOfWeek
    , startTime : String
    , endTime : String
    }


type Route
    = Dashboard
    | Bookings
    | BookingDetail String
    | Calendars
    | CalendarViewRoute
    | Availability
    | Settings
    | Login
    | NotFound


type Session
    = LoggedIn
    | Guest
    | Checking


type StatusFilter
    = AllBookings
    | OnlyConfirmed
    | OnlyCancelled


type alias SchedulingSettings =
    { minNoticeHours : Int
    , bookingWindowDays : Int
    , defaultDurationMinutes : Int
    , videoLink : Maybe String
    }


type CalendarEventType
    = ExternalCalendarEvent
    | BookingEvent
    | AvailabilityEvent


type alias CalendarEvent =
    { id : String
    , title : String
    , start : String
    , end : String
    , isAllDay : Bool
    , eventType : CalendarEventType
    }



-- DayOfWeek helpers


dayOfWeekToInt : DayOfWeek -> Int
dayOfWeekToInt day =
    case day of
        Monday ->
            1

        Tuesday ->
            2

        Wednesday ->
            3

        Thursday ->
            4

        Friday ->
            5

        Saturday ->
            6

        Sunday ->
            7


dayOfWeekFromInt : Int -> Maybe DayOfWeek
dayOfWeekFromInt n =
    case n of
        1 ->
            Just Monday

        2 ->
            Just Tuesday

        3 ->
            Just Wednesday

        4 ->
            Just Thursday

        5 ->
            Just Friday

        6 ->
            Just Saturday

        7 ->
            Just Sunday

        _ ->
            Nothing


dayOfWeekLabel : DayOfWeek -> String
dayOfWeekLabel day =
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
