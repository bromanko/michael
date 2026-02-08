module Main exposing (main)

import Browser
import Model exposing (Flags, Model, init)
import Update exposing (Msg, focusElement, update)
import View exposing (view)


main : Program Flags Model Msg
main =
    Browser.element
        { init =
            \flags ->
                let
                    ( model, cmd ) =
                        init flags
                in
                ( model
                , Cmd.batch
                    [ cmd
                    , focusElement "title-input"
                    ]
                )
        , update = update
        , view = view
        , subscriptions = \_ -> Sub.none
        }
