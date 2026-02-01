module Api exposing
    ( cancelBooking
    , checkSession
    , fetchBooking
    , fetchBookings
    , fetchDashboardStats
    , login
    , logout
    )

import Http
import Json.Decode as Decode exposing (Decoder)
import Json.Decode.Pipeline exposing (optional, required)
import Json.Encode as Encode
import Types exposing (Booking, BookingStatus(..), DashboardStats, PaginatedBookings, StatusFilter(..))



-- Session


checkSession : (Result Http.Error () -> msg) -> Cmd msg
checkSession toMsg =
    Http.get
        { url = "/api/admin/session"
        , expect = Http.expectWhatever toMsg
        }


login : String -> (Result Http.Error () -> msg) -> Cmd msg
login password toMsg =
    Http.post
        { url = "/api/admin/login"
        , body =
            Http.jsonBody
                (Encode.object
                    [ ( "password", Encode.string password )
                    ]
                )
        , expect = Http.expectWhatever toMsg
        }


logout : (Result Http.Error () -> msg) -> Cmd msg
logout toMsg =
    Http.post
        { url = "/api/admin/logout"
        , body = Http.emptyBody
        , expect = Http.expectWhatever toMsg
        }



-- Dashboard


fetchDashboardStats : (Result Http.Error DashboardStats -> msg) -> Cmd msg
fetchDashboardStats toMsg =
    Http.get
        { url = "/api/admin/dashboard"
        , expect = Http.expectJson toMsg dashboardStatsDecoder
        }


dashboardStatsDecoder : Decoder DashboardStats
dashboardStatsDecoder =
    Decode.succeed DashboardStats
        |> required "upcomingCount" Decode.int
        |> optional "nextBookingTime" (Decode.nullable Decode.string) Nothing
        |> optional "nextBookingTitle" (Decode.nullable Decode.string) Nothing



-- Bookings


fetchBookings : Int -> Int -> StatusFilter -> (Result Http.Error PaginatedBookings -> msg) -> Cmd msg
fetchBookings page pageSize statusFilter toMsg =
    let
        statusParam =
            case statusFilter of
                AllBookings ->
                    ""

                OnlyConfirmed ->
                    "&status=confirmed"

                OnlyCancelled ->
                    "&status=cancelled"

        url =
            "/api/admin/bookings?page="
                ++ String.fromInt page
                ++ "&pageSize="
                ++ String.fromInt pageSize
                ++ statusParam
    in
    Http.get
        { url = url
        , expect = Http.expectJson toMsg paginatedBookingsDecoder
        }


paginatedBookingsDecoder : Decoder PaginatedBookings
paginatedBookingsDecoder =
    Decode.succeed PaginatedBookings
        |> required "bookings" (Decode.list bookingDecoder)
        |> required "totalCount" Decode.int
        |> required "page" Decode.int
        |> required "pageSize" Decode.int


fetchBooking : String -> (Result Http.Error Booking -> msg) -> Cmd msg
fetchBooking id toMsg =
    Http.get
        { url = "/api/admin/bookings/" ++ id
        , expect = Http.expectJson toMsg bookingDecoder
        }


bookingDecoder : Decoder Booking
bookingDecoder =
    Decode.succeed Booking
        |> required "id" Decode.string
        |> required "participantName" Decode.string
        |> required "participantEmail" Decode.string
        |> optional "participantPhone" (Decode.nullable Decode.string) Nothing
        |> required "title" Decode.string
        |> optional "description" (Decode.nullable Decode.string) Nothing
        |> required "startTime" Decode.string
        |> required "endTime" Decode.string
        |> required "durationMinutes" Decode.int
        |> required "timezone" Decode.string
        |> required "status" bookingStatusDecoder
        |> required "createdAt" Decode.string


bookingStatusDecoder : Decoder BookingStatus
bookingStatusDecoder =
    Decode.string
        |> Decode.andThen
            (\str ->
                case str of
                    "confirmed" ->
                        Decode.succeed Confirmed

                    "cancelled" ->
                        Decode.succeed Cancelled

                    other ->
                        Decode.fail ("Unknown booking status: " ++ other)
            )


cancelBooking : String -> (Result Http.Error () -> msg) -> Cmd msg
cancelBooking id toMsg =
    Http.post
        { url = "/api/admin/bookings/" ++ id ++ "/cancel"
        , body = Http.emptyBody
        , expect = Http.expectWhatever toMsg
        }
