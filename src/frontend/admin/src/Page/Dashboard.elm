module Page.Dashboard exposing (Model, Msg(..), init, update, view)

import Api
import Html exposing (Html, a, div, p, text)
import Html.Attributes exposing (class, href)
import Http
import Types exposing (DashboardStats)
import View.Components exposing (card, errorBanner, loadingSpinner, pageHeading)


type alias Model =
    { stats : Maybe DashboardStats
    , loading : Bool
    , error : Maybe String
    }


type Msg
    = StatsReceived (Result Http.Error DashboardStats)


init : ( Model, Cmd Msg )
init =
    ( { stats = Nothing
      , loading = True
      , error = Nothing
      }
    , Api.fetchDashboardStats StatsReceived
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        StatsReceived (Ok stats) ->
            ( { model | stats = Just stats, loading = False }, Cmd.none )

        StatsReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load dashboard stats." }, Cmd.none )


view : Model -> Html Msg
view model =
    div []
        [ pageHeading "Dashboard"
        , case model.error of
            Just err ->
                errorBanner err

            Nothing ->
                text ""
        , if model.loading then
            loadingSpinner

          else
            case model.stats of
                Just stats ->
                    statsView stats

                Nothing ->
                    text ""
        ]


statsView : DashboardStats -> Html Msg
statsView stats =
    div [ class "grid grid-cols-1 md:grid-cols-2 gap-6" ]
        [ card
            [ p [ class "text-sm font-medium text-sand-500 uppercase tracking-wider mb-2" ]
                [ text "Upcoming Bookings" ]
            , p [ class "text-3xl font-display text-sand-900" ]
                [ text (String.fromInt stats.upcomingCount) ]
            , a [ href "/admin/bookings", class "text-sm text-coral hover:text-coral-dark mt-2 inline-block" ]
                [ text "View all" ]
            ]
        , card
            [ p [ class "text-sm font-medium text-sand-500 uppercase tracking-wider mb-2" ]
                [ text "Next Booking" ]
            , case stats.nextBookingTime of
                Just time ->
                    div []
                        [ p [ class "text-lg text-sand-900" ]
                            [ text (formatDateTime time) ]
                        , case stats.nextBookingTitle of
                            Just title ->
                                p [ class "text-sm text-sand-500 mt-1" ]
                                    [ text title ]

                            Nothing ->
                                text ""
                        ]

                Nothing ->
                    p [ class "text-sand-400 text-lg" ]
                        [ text "No upcoming bookings" ]
            ]
        ]


formatDateTime : String -> String
formatDateTime isoString =
    -- Display date and time portion of ISO string
    let
        datePart =
            String.left 10 isoString

        timePart =
            String.slice 11 16 isoString
    in
    datePart ++ " at " ++ timePart
