module Route exposing
    ( Route(..)
    , fromUrl
    , toPath
    )

import Url exposing (Url)
import Url.Parser as Parser exposing (Parser, (</>), oneOf, s, string, top)


type Route
    = Dashboard
    | Bookings
    | BookingDetail String
    | Calendars
    | Availability
    | Settings
    | Login
    | NotFound


parser : Parser (Route -> a) a
parser =
    oneOf
        [ Parser.map Dashboard (s "admin")
        , Parser.map Dashboard (s "admin" </> top)
        , Parser.map Bookings (s "admin" </> s "bookings")
        , Parser.map BookingDetail (s "admin" </> s "bookings" </> string)
        , Parser.map Calendars (s "admin" </> s "calendars")
        , Parser.map Availability (s "admin" </> s "availability")
        , Parser.map Settings (s "admin" </> s "settings")
        , Parser.map Login (s "admin" </> s "login")
        ]


fromUrl : Url -> Route
fromUrl url =
    Parser.parse parser url
        |> Maybe.withDefault NotFound


toPath : Route -> String
toPath route =
    case route of
        Dashboard ->
            "/admin/"

        Bookings ->
            "/admin/bookings"

        BookingDetail id ->
            "/admin/bookings/" ++ id

        Calendars ->
            "/admin/calendars"

        Availability ->
            "/admin/availability"

        Settings ->
            "/admin/settings"

        Login ->
            "/admin/login"

        NotFound ->
            "/admin/"
