module BookingsTest exposing (suite)

import Expect
import Http
import Page.Bookings exposing (Model, Msg(..), init, update)
import Test exposing (Test, describe, test)
import Types exposing (Booking, BookingStatus(..), PaginatedBookings, StatusFilter(..))


suite : Test
suite =
    describe "Page.Bookings"
        [ updateTests
        , paginationLogicTests
        ]


updateTests : Test
updateTests =
    describe "update"
        [ test "BookingsReceived Ok updates bookings and clears loading" <|
            \_ ->
                let
                    ( initialModel, _ ) =
                        init

                    paginatedBookings =
                        { bookings = [ sampleBooking ]
                        , totalCount = 1
                        , page = 1
                        , pageSize = 20
                        }

                    ( newModel, _ ) =
                        update (BookingsReceived (Ok paginatedBookings)) initialModel
                in
                Expect.all
                    [ \m -> Expect.equal 1 (List.length m.bookings)
                    , \m -> Expect.equal False m.loading
                    , \m -> Expect.equal 1 m.totalCount
                    ]
                    newModel
        , test "BookingsReceived Err sets error and clears loading" <|
            \_ ->
                let
                    ( initialModel, _ ) =
                        init

                    ( newModel, _ ) =
                        update (BookingsReceived (Err mockHttpError)) initialModel
                in
                Expect.all
                    [ \m -> Expect.equal False m.loading
                    , \m -> Expect.notEqual Nothing m.error
                    ]
                    newModel
        , test "PageChanged sets loading and updates page" <|
            \_ ->
                let
                    ( initialModel, _ ) =
                        init

                    model =
                        { initialModel | loading = False }

                    ( newModel, cmd ) =
                        update (PageChanged 3) model
                in
                Expect.all
                    [ \m -> Expect.equal True m.loading
                    , \m -> Expect.equal 3 m.page
                    , \_ -> Expect.notEqual Cmd.none cmd
                    ]
                    newModel
        , test "StatusFilterChanged resets to page 1" <|
            \_ ->
                let
                    ( initialModel, _ ) =
                        init

                    model =
                        { initialModel | page = 5, loading = False }

                    ( newModel, _ ) =
                        update (StatusFilterChanged OnlyCancelled) model
                in
                Expect.all
                    [ \m -> Expect.equal 1 m.page
                    , \m -> Expect.equal OnlyCancelled m.statusFilter
                    , \m -> Expect.equal True m.loading
                    ]
                    newModel
        ]


paginationLogicTests : Test
paginationLogicTests =
    describe "pagination logic"
        [ test "totalPages calculation with exact division" <|
            \_ ->
                let
                    totalPages =
                        ceiling (toFloat 40 / toFloat 20)
                in
                Expect.equal 2 totalPages
        , test "totalPages calculation with remainder" <|
            \_ ->
                let
                    totalPages =
                        ceiling (toFloat 45 / toFloat 20)
                in
                Expect.equal 3 totalPages
        , test "totalPages is 1 for small counts" <|
            \_ ->
                let
                    totalPages =
                        ceiling (toFloat 5 / toFloat 20)
                in
                Expect.equal 1 totalPages
        , test "totalPages is 0 for empty" <|
            \_ ->
                let
                    totalPages =
                        ceiling (toFloat 0 / toFloat 20)
                in
                Expect.equal 0 totalPages
        ]



-- Test helpers


sampleBooking : Booking
sampleBooking =
    { id = "booking-1"
    , participantName = "John Doe"
    , participantEmail = "john@example.com"
    , participantPhone = Nothing
    , title = "Meeting"
    , description = Nothing
    , startTime = "2026-02-10T10:00:00Z"
    , endTime = "2026-02-10T11:00:00Z"
    , durationMinutes = 60
    , timezone = "America/New_York"
    , status = Confirmed
    , createdAt = "2026-02-01T12:00:00Z"
    }


mockHttpError : Http.Error
mockHttpError =
    Http.NetworkError
