module Api exposing
    ( AvailabilityResponse
    , availabilitySlotDecoder
    , bookingStatusDecoder
    , calendarSourceDecoder
    , cancelBooking
    , checkSession
    , dashboardStatsDecoder
    , dayOfWeekDecoder
    , fetchAvailability
    , fetchBooking
    , fetchBookings
    , fetchCalendarSources
    , fetchCalendarView
    , fetchDashboardStats
    , fetchSettings
    , fetchSyncHistory
    , login
    , logout
    , providerDecoder
    , saveAvailability
    , saveSettings
    , triggerSync
    )

import Http
import Json.Decode as Decode exposing (Decoder)
import Json.Decode.Pipeline exposing (optional, required)
import Json.Encode as Encode
import Types exposing (AvailabilitySlot, AvailabilitySlotInput, Booking, BookingStatus(..), CalDavProvider(..), CalendarEvent, CalendarEventType(..), CalendarSource, DashboardStats, DayOfWeek, PaginatedBookings, SchedulingSettings, StatusFilter(..), SyncHistoryEntry, SyncStatus(..), dayOfWeekFromInt, dayOfWeekToInt)
import Url



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
            String.concat
                [ "/api/admin/bookings?page="
                , String.fromInt page
                , "&pageSize="
                , String.fromInt pageSize
                , statusParam
                ]
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
        { url = "/api/admin/bookings/" ++ Url.percentEncode id
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
        { url = "/api/admin/bookings/" ++ Url.percentEncode id ++ "/cancel"
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
        { url = "/api/admin/calendars/" ++ Url.percentEncode id ++ "/sync"
        , body = Http.emptyBody
        , expect = Http.expectWhatever toMsg
        }


fetchSyncHistory : String -> (Result Http.Error (List SyncHistoryEntry) -> msg) -> Cmd msg
fetchSyncHistory sourceId toMsg =
    Http.get
        { url = "/api/admin/calendars/" ++ Url.percentEncode sourceId ++ "/history?limit=10"
        , expect = Http.expectJson toMsg syncHistoryResponseDecoder
        }


syncHistoryResponseDecoder : Decoder (List SyncHistoryEntry)
syncHistoryResponseDecoder =
    Decode.field "history" (Decode.list syncHistoryEntryDecoder)


syncHistoryEntryDecoder : Decoder SyncHistoryEntry
syncHistoryEntryDecoder =
    Decode.succeed SyncHistoryEntry
        |> required "id" Decode.string
        |> required "sourceId" Decode.string
        |> required "syncedAt" Decode.string
        |> required "status" syncStatusDecoder
        |> optional "errorMessage" (Decode.nullable Decode.string) Nothing


syncStatusDecoder : Decoder SyncStatus
syncStatusDecoder =
    Decode.string
        |> Decode.map
            (\s ->
                case s of
                    "ok" ->
                        SyncOk

                    _ ->
                        SyncError
            )



-- Availability


type alias AvailabilityResponse =
    { slots : List AvailabilitySlot
    , timezone : String
    }


fetchAvailability : (Result Http.Error AvailabilityResponse -> msg) -> Cmd msg
fetchAvailability toMsg =
    Http.get
        { url = "/api/admin/availability"
        , expect = Http.expectJson toMsg availabilityResponseDecoder
        }


availabilityResponseDecoder : Decoder AvailabilityResponse
availabilityResponseDecoder =
    Decode.succeed AvailabilityResponse
        |> required "slots" (Decode.list availabilitySlotDecoder)
        |> required "timezone" Decode.string


availabilitySlotDecoder : Decoder AvailabilitySlot
availabilitySlotDecoder =
    Decode.succeed AvailabilitySlot
        |> required "id" Decode.string
        |> required "dayOfWeek" dayOfWeekDecoder
        |> required "startTime" Decode.string
        |> required "endTime" Decode.string


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
    -> (Result Http.Error AvailabilityResponse -> msg)
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



-- Settings


fetchSettings : (Result Http.Error SchedulingSettings -> msg) -> Cmd msg
fetchSettings toMsg =
    Http.get
        { url = "/api/admin/settings"
        , expect = Http.expectJson toMsg settingsDecoder
        }


settingsDecoder : Decoder SchedulingSettings
settingsDecoder =
    Decode.succeed SchedulingSettings
        |> required "minNoticeHours" Decode.int
        |> required "bookingWindowDays" Decode.int
        |> required "defaultDurationMinutes" Decode.int
        |> optional "videoLink" (Decode.nullable Decode.string) Nothing


saveSettings : SchedulingSettings -> (Result Http.Error SchedulingSettings -> msg) -> Cmd msg
saveSettings settings toMsg =
    Http.request
        { method = "PUT"
        , headers = []
        , url = "/api/admin/settings"
        , body =
            Http.jsonBody
                (Encode.object
                    [ ( "minNoticeHours", Encode.int settings.minNoticeHours )
                    , ( "bookingWindowDays", Encode.int settings.bookingWindowDays )
                    , ( "defaultDurationMinutes", Encode.int settings.defaultDurationMinutes )
                    , ( "videoLink"
                      , case settings.videoLink of
                            Just link ->
                                Encode.string link

                            Nothing ->
                                Encode.null
                      )
                    ]
                )
        , expect = Http.expectJson toMsg settingsDecoder
        , timeout = Nothing
        , tracker = Nothing
        }



-- Calendar View


fetchCalendarView : String -> String -> String -> (Result Http.Error (List CalendarEvent) -> msg) -> Cmd msg
fetchCalendarView start end timezone toMsg =
    Http.get
        { url =
            String.concat
                [ "/api/admin/calendar-view?start="
                , Url.percentEncode start
                , "&end="
                , Url.percentEncode end
                , "&tz="
                , Url.percentEncode timezone
                ]
        , expect = Http.expectJson toMsg calendarViewResponseDecoder
        }


calendarViewResponseDecoder : Decoder (List CalendarEvent)
calendarViewResponseDecoder =
    Decode.field "events" (Decode.list calendarEventDecoder)


calendarEventDecoder : Decoder CalendarEvent
calendarEventDecoder =
    Decode.succeed CalendarEvent
        |> required "id" Decode.string
        |> required "title" Decode.string
        |> required "start" Decode.string
        |> required "end" Decode.string
        |> required "isAllDay" Decode.bool
        |> required "eventType" eventTypeDecoder


eventTypeDecoder : Decoder CalendarEventType
eventTypeDecoder =
    Decode.string
        |> Decode.andThen
            (\s ->
                case s of
                    "calendar" ->
                        Decode.succeed ExternalCalendarEvent

                    "booking" ->
                        Decode.succeed BookingEvent

                    "availability" ->
                        Decode.succeed AvailabilityEvent

                    other ->
                        Decode.fail ("Unknown event type: " ++ other)
            )
