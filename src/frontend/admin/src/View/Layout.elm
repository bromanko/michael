module View.Layout exposing (view)

import Html exposing (Html, a, button, div, nav, p, span, text)
import Html.Attributes exposing (class, href)
import Html.Events exposing (onClick)
import Route
import Types exposing (Route(..))


type alias Config msg =
    { route : Route
    , navOpen : Bool
    , onToggleNav : msg
    , onLogout : msg
    , content : Html msg
    }


view : Config msg -> Html msg
view config =
    div [ class "min-h-screen flex" ]
        [ sidebar config
        , div [ class "flex-1 flex flex-col min-h-screen" ]
            [ topBar config
            , div [ class "flex-1 bg-sand-50 p-6 md:p-8" ]
                [ config.content ]
            ]
        ]


sidebar : Config msg -> Html msg
sidebar config =
    div
        [ class
            ("fixed inset-y-0 left-0 z-30 w-64 bg-sand-800 text-sand-200 transform transition-transform duration-200 md:relative md:translate-x-0 "
                ++ (if config.navOpen then
                        "translate-x-0"

                    else
                        "-translate-x-full"
                   )
            )
        ]
        [ div [ class "p-6" ]
            [ p [ class "font-display text-xl text-white mb-8" ]
                [ text "Michael" ]
            , nav [ class "space-y-1" ]
                [ navLink config.route Dashboard "Dashboard"
                , navLink config.route Bookings "Bookings"
                , navLink config.route Calendars "Calendars"
                , navLink config.route Availability "Availability"
                , navLink config.route Settings "Settings"
                ]
            ]
        ]


navLink : Route -> Route -> String -> Html msg
navLink currentRoute targetRoute label =
    let
        isActive =
            case ( currentRoute, targetRoute ) of
                ( Dashboard, Dashboard ) ->
                    True

                ( Bookings, Bookings ) ->
                    True

                ( BookingDetail _, Bookings ) ->
                    True

                ( Calendars, Calendars ) ->
                    True

                ( Availability, Availability ) ->
                    True

                ( Settings, Settings ) ->
                    True

                _ ->
                    False

        activeClasses =
            if isActive then
                "bg-sand-700 text-white"

            else
                "text-sand-400 hover:text-white hover:bg-sand-700/50"
    in
    a
        [ href (Route.toPath targetRoute)
        , class ("block px-4 py-2 rounded-lg text-sm font-medium transition-colors " ++ activeClasses)
        ]
        [ text label ]


topBar : Config msg -> Html msg
topBar config =
    div [ class "bg-white border-b border-sand-200 px-6 py-4 flex items-center justify-between md:justify-end" ]
        [ button
            [ class "md:hidden text-sand-600 hover:text-sand-900"
            , onClick config.onToggleNav
            ]
            [ text "Menu" ]
        , div [ class "flex items-center gap-4" ]
            [ span [ class "text-sm text-sand-500" ]
                [ text "Admin" ]
            , button
                [ class "text-sm text-sand-400 hover:text-sand-700 transition-colors"
                , onClick config.onLogout
                ]
                [ text "Log out" ]
            ]
        ]
