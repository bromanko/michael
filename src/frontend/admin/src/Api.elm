module Api exposing
    ( cancelBooking
    , checkSession
    , fetchAvailability
    , fetchBooking
    , fetchBookings
    , fetchCalendarSources
    , fetchDashboardStats
    , login
    , logout
    , saveAvailability
    , triggerSync
    )

import Http
import Json.Decode as Decode exposing (Decoder)
import Json.Decode.Pipeline exposing (optional, required)
import Json.Encode as Encode
import Types exposing (AvailabilitySlot, AvailabilitySlotInput, Booking, BookingStatus(..), CalDavProvider(..), CalendarSource, DashboardStats, DayOfWeek(..), PaginatedBookings, StatusFilter(..), dayOfWeekFromInt, dayOfWeekToInt)



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



-- Calendar sources


fetchCalendarSources : (Result Http.Error (List CalendarSource) -> msg) -> Cmd msg
fetchCalendarSources toMsg =
    Http.get
        { url = "/api/admin/calendars"
        , expect = Http.expectJson toMsg calendarSourcesResponseDecoder
        }


calendarSourcesResponseDecoder : Decoder (List CalendarSource)
calendarSourcesResponseDecoder =
    Decode.field "sources" (Decode.list calendarSourceDecoder)


calendarSourceDecoder : Decoder CalendarSource
calendarSourceDecoder =
    Decode.succeed CalendarSource
        |> required "id" Decode.string
        |> required "provider" providerDecoder
        |> required "baseUrl" Decode.string
        |> optional "lastSyncedAt" (Decode.nullable Decode.string) Nothing
        |> optional "lastSyncResult" (Decode.nullable Decode.string) Nothing


providerDecoder : Decoder CalDavProvider
providerDecoder =
    Decode.string
        |> Decode.andThen
            (\s ->
                case s of
                    "fastmail" ->
                        Decode.succeed Fastmail

                    "icloud" ->
                        Decode.succeed ICloud

                    other ->
                        Decode.fail ("Unknown provider: " ++ other)
            )


triggerSync : String -> (Result Http.Error () -> msg) -> Cmd msg
triggerSync id toMsg =
    Http.post
        { url = "/api/admin/calendars/" ++ id ++ "/sync"
        , body = Http.emptyBody
        , expect = Http.expectWhatever toMsg
        }



-- Availability


fetchAvailability : (Result Http.Error (List AvailabilitySlot) -> msg) -> Cmd msg
fetchAvailability toMsg =
    Http.get
        { url = "/api/admin/availability"
        , expect = Http.expectJson toMsg availabilityResponseDecoder
        }


availabilityResponseDecoder : Decoder (List AvailabilitySlot)
availabilityResponseDecoder =
    Decode.field "slots" (Decode.list availabilitySlotDecoder)


availabilitySlotDecoder : Decoder AvailabilitySlot
availabilitySlotDecoder =
    Decode.succeed AvailabilitySlot
        |> required "id" Decode.string
        |> required "dayOfWeek" dayOfWeekDecoder
        |> required "startTime" Decode.string
        |> required "endTime" Decode.string
        |> required "timezone" Decode.string


dayOfWeekDecoder : Decoder DayOfWeek
dayOfWeekDecoder =
    Decode.int
        |> Decode.andThen
            (\n ->
                case dayOfWeekFromInt n of
                    Just day ->
                        Decode.succeed day

                    Nothing ->
                        Decode.fail ("Unknown day of week: " ++ String.fromInt n)
            )


saveAvailability :
    List AvailabilitySlotInput
    -> (Result Http.Error (List AvailabilitySlot) -> msg)
    -> Cmd msg
saveAvailability slots toMsg =
    Http.request
        { method = "PUT"
        , headers = []
        , url = "/api/admin/availability"
        , body =
            Http.jsonBody
                (Encode.object
                    [ ( "slots"
                      , Encode.list
                            (\s ->
                                Encode.object
                                    [ ( "dayOfWeek", Encode.int (dayOfWeekToInt s.dayOfWeek) )
                                    , ( "startTime", Encode.string s.startTime )
                                    , ( "endTime", Encode.string s.endTime )
                                    , ( "timezone", Encode.string s.timezone )
                                    ]
                            )
                            slots
                      )
                    ]
                )
        , expect = Http.expectJson toMsg availabilityResponseDecoder
        , timeout = Nothing
        , tracker = Nothing
        }
