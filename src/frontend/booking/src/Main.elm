module Main exposing (main)

import Browser
import Model exposing (Flags, Model, init)
import Update exposing (Msg, update)
import View exposing (view)


main : Program Flags Model Msg
main =
    Browser.element
        { init = init
        , update = update
        , view = view
        , subscriptions = \_ -> Sub.none
        }
