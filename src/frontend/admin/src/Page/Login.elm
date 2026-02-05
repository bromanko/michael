module Page.Login exposing (Model, Msg, OutMsg(..), init, update, view)

import Api
import Html exposing (Html, button, div, form, h1, input, p, text)
import Html.Attributes exposing (class, disabled, placeholder, type_, value)
import Html.Events exposing (onInput, onSubmit)
import Http


type alias Model =
    { password : String
    , error : Maybe String
    , loading : Bool
    }


type Msg
    = PasswordUpdated String
    | LoginFormSubmitted
    | LoginResponseReceived (Result Http.Error ())


type OutMsg
    = LoginSucceeded


init : ( Model, Cmd Msg )
init =
    ( { password = ""
      , error = Nothing
      , loading = False
      }
    , Cmd.none
    )


update : Msg -> Model -> ( Model, Cmd Msg, Maybe OutMsg )
update msg model =
    case msg of
        PasswordUpdated pw ->
            ( { model | password = pw }, Cmd.none, Nothing )

        LoginFormSubmitted ->
            if String.isEmpty (String.trim model.password) then
                ( { model | error = Just "Password is required." }, Cmd.none, Nothing )

            else
                ( { model | loading = True, error = Nothing }
                , Api.login model.password LoginResponseReceived
                , Nothing
                )

        LoginResponseReceived (Ok _) ->
            ( { model | loading = False, password = "" }, Cmd.none, Just LoginSucceeded )

        LoginResponseReceived (Err err) ->
            let
                errorMsg =
                    case err of
                        Http.BadStatus 401 ->
                            "Invalid password."

                        Http.NetworkError ->
                            "Network error. Please try again."

                        _ ->
                            "Login failed. Please try again."
            in
            ( { model | loading = False, password = "", error = Just errorMsg }, Cmd.none, Nothing )


view : Model -> Html Msg
view model =
    div [ class "min-h-screen flex items-center justify-center bg-sand-50" ]
        [ div [ class "w-full max-w-sm" ]
            [ h1 [ class "font-display text-2xl text-sand-900 mb-2 text-center" ]
                [ text "Michael Admin" ]
            , p [ class "text-sand-500 text-sm text-center mb-8" ]
                [ text "Sign in to manage your calendar." ]
            , case model.error of
                Just err ->
                    div [ class "mb-4 px-4 py-3 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm" ]
                        [ text err ]

                Nothing ->
                    text ""
            , form [ onSubmit LoginFormSubmitted ]
                [ input
                    [ type_ "password"
                    , class "w-full border border-sand-300 rounded-lg px-4 py-3 text-sand-900 text-sm focus:outline-none focus:border-coral transition-colors mb-4"
                    , placeholder "Password"
                    , value model.password
                    , onInput PasswordUpdated
                    ]
                    []
                , button
                    [ type_ "submit"
                    , class "w-full bg-coral text-white px-5 py-3 rounded-lg text-sm font-medium hover:bg-coral-dark transition-colors disabled:opacity-40"
                    , disabled model.loading
                    ]
                    [ text
                        (if model.loading then
                            "Signing in..."

                         else
                            "Sign in"
                        )
                    ]
                ]
            ]
        ]
