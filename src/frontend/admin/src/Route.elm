module Route exposing
    ( fromUrl
    , toPath
    )

import Types exposing (Route(..))
import Url exposing (Url)
import Url.Builder
import Url.Parser as Parser exposing ((</>), Parser, oneOf, s, string)


parser : Parser (Route -> a) a
parser =
    oneOf
        [ Parser.map Dashboard (s "admin")
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
            "/admin/bookings/" ++ Url.percentEncode id

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
