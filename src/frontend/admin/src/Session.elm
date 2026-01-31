module Session exposing
    ( Session(..)
    , isLoggedIn
    )


type Session
    = LoggedIn
    | Guest
    | Checking


isLoggedIn : Session -> Bool
isLoggedIn session =
    case session of
        LoggedIn ->
            True

        Guest ->
            False

        Checking ->
            False
