port module Ports exposing (convertWindows, windowsConverted)

{-| Ports for client-side timezone conversion.

The booking frontend uses these to convert availability window timestamps
between IANA timezones without a backend round-trip. JavaScript handles the
actual conversion via the Intl API.

-}


{-| Ask JavaScript to convert a list of ISO-8601 timestamp pairs into
the given IANA timezone. Each window object has `start` and `end` strings.
-}
port convertWindows :
    { windows : List { start : String, end : String }
    , timezone : String
    }
    -> Cmd msg


{-| Receive the converted timestamp pairs back from JavaScript.
Each object has the same `start`/`end` shape, now expressed in the
target timezone.
-}
port windowsConverted :
    (List { start : String, end : String } -> msg)
    -> Sub msg
