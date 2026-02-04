module Types exposing
    ( AvailabilitySlot
    , Booking
    , BookingStatus(..)
    , CalendarSource
    , DashboardStats
    , DayOfWeek(..)
    , PaginatedBookings
    , Route(..)
    , Session(..)
    , StatusFilter(..)
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


type alias CalendarSource =
    { id : String
    , provider : String
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


type alias AvailabilitySlot =
    { id : String
    , dayOfWeek : DayOfWeek
    , startTime : String
    , endTime : String
    , timezone : String
    }


type Route
    = Dashboard
    | Bookings
    | BookingDetail String
    | Calendars
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
