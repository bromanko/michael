module Types exposing
    ( Booking
    , BookingStatus(..)
    , DashboardStats
    , PaginatedBookings
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
