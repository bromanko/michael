module RouteTest exposing (suite)

import Expect
import Route exposing (fromUrl, toPath)
import Test exposing (Test, describe, test)
import Types exposing (Route(..))
import Url


suite : Test
suite =
    describe "Route"
        [ fromUrlTests
        , toPathTests
        , roundTripTests
        ]


parseUrl : String -> Route
parseUrl path =
    { protocol = Url.Https
    , host = "localhost"
    , port_ = Nothing
    , path = path
    , query = Nothing
    , fragment = Nothing
    }
        |> fromUrl


fromUrlTests : Test
fromUrlTests =
    describe "fromUrl"
        [ test "parses /admin as Dashboard" <|
            \_ ->
                parseUrl "/admin"
                    |> Expect.equal Dashboard
        , test "parses /admin/ as Dashboard" <|
            \_ ->
                parseUrl "/admin/"
                    |> Expect.equal Dashboard
        , test "parses /admin/bookings as Bookings" <|
            \_ ->
                parseUrl "/admin/bookings"
                    |> Expect.equal Bookings
        , test "parses /admin/bookings/:id as BookingDetail" <|
            \_ ->
                parseUrl "/admin/bookings/abc-123"
                    |> Expect.equal (BookingDetail "abc-123")
        , test "parses /admin/calendars as Calendars" <|
            \_ ->
                parseUrl "/admin/calendars"
                    |> Expect.equal Calendars
        , test "parses /admin/calendar as CalendarViewRoute" <|
            \_ ->
                parseUrl "/admin/calendar"
                    |> Expect.equal CalendarViewRoute
        , test "parses /admin/availability as Availability" <|
            \_ ->
                parseUrl "/admin/availability"
                    |> Expect.equal Availability
        , test "parses /admin/settings as Settings" <|
            \_ ->
                parseUrl "/admin/settings"
                    |> Expect.equal Settings
        , test "parses /admin/login as Login" <|
            \_ ->
                parseUrl "/admin/login"
                    |> Expect.equal Login
        , test "unknown path returns NotFound" <|
            \_ ->
                parseUrl "/admin/unknown"
                    |> Expect.equal NotFound
        , test "root path returns NotFound" <|
            \_ ->
                parseUrl "/"
                    |> Expect.equal NotFound
        ]


toPathTests : Test
toPathTests =
    describe "toPath"
        [ test "Dashboard" <|
            \_ ->
                toPath Dashboard
                    |> Expect.equal "/admin/"
        , test "Bookings" <|
            \_ ->
                toPath Bookings
                    |> Expect.equal "/admin/bookings"
        , test "BookingDetail" <|
            \_ ->
                toPath (BookingDetail "abc-123")
                    |> Expect.equal "/admin/bookings/abc-123"
        , test "Calendars" <|
            \_ ->
                toPath Calendars
                    |> Expect.equal "/admin/calendars"
        , test "CalendarViewRoute" <|
            \_ ->
                toPath CalendarViewRoute
                    |> Expect.equal "/admin/calendar"
        , test "Availability" <|
            \_ ->
                toPath Availability
                    |> Expect.equal "/admin/availability"
        , test "Settings" <|
            \_ ->
                toPath Settings
                    |> Expect.equal "/admin/settings"
        , test "Login" <|
            \_ ->
                toPath Login
                    |> Expect.equal "/admin/login"
        , test "NotFound" <|
            \_ ->
                toPath NotFound
                    |> Expect.equal "/admin/"
        ]


roundTripTests : Test
roundTripTests =
    describe "round-trip (toPath >> fromUrl)"
        [ test "Dashboard round-trips" <|
            \_ ->
                parseUrl (toPath Dashboard)
                    |> Expect.equal Dashboard
        , test "Bookings round-trips" <|
            \_ ->
                parseUrl (toPath Bookings)
                    |> Expect.equal Bookings
        , test "BookingDetail round-trips" <|
            \_ ->
                parseUrl (toPath (BookingDetail "test-id"))
                    |> Expect.equal (BookingDetail "test-id")
        , test "Calendars round-trips" <|
            \_ ->
                parseUrl (toPath Calendars)
                    |> Expect.equal Calendars
        , test "CalendarViewRoute round-trips" <|
            \_ ->
                parseUrl (toPath CalendarViewRoute)
                    |> Expect.equal CalendarViewRoute
        , test "Availability round-trips" <|
            \_ ->
                parseUrl (toPath Availability)
                    |> Expect.equal Availability
        , test "Settings round-trips" <|
            \_ ->
                parseUrl (toPath Settings)
                    |> Expect.equal Settings
        , test "Login round-trips" <|
            \_ ->
                parseUrl (toPath Login)
                    |> Expect.equal Login
        ]
