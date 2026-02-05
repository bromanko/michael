module Page.Calendars exposing (Model, Msg, init, update, view)

import Api
import Html exposing (Html, button, div, p, span, table, td, text, th, thead, tr)
import Html.Keyed as Keyed
import Html.Lazy exposing (lazy2)
import Html.Attributes exposing (class, disabled)
import Html.Events exposing (onClick)
import Http
import Set exposing (Set)
import Types exposing (CalDavProvider(..), CalendarSource)
import View.Components exposing (card, errorBanner, formatDateTime, loadingSpinner, pageHeading)


type alias Model =
    { sources : List CalendarSource
    , loading : Bool
    , error : Maybe String
    , syncing : Set String
    }


type Msg
    = SourcesReceived (Result Http.Error (List CalendarSource))
    | SyncTriggered String
    | SyncCompleted String (Result Http.Error ())


init : ( Model, Cmd Msg )
init =
    ( { sources = []
      , loading = True
      , error = Nothing
      , syncing = Set.empty
      }
    , Api.fetchCalendarSources SourcesReceived
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        SourcesReceived (Ok sources) ->
            ( { model | sources = sources, loading = False }, Cmd.none )

        SourcesReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load calendar sources." }, Cmd.none )

        SyncTriggered id ->
            ( { model | syncing = Set.insert id model.syncing }
            , Api.triggerSync id (SyncCompleted id)
            )

        SyncCompleted id (Ok _) ->
            ( { model | syncing = Set.remove id model.syncing }
            , Api.fetchCalendarSources SourcesReceived
            )

        SyncCompleted id (Err httpError) ->
            let
                errorMsg =
                    case httpError of
                        Http.Timeout ->
                            "Sync timed out."

                        Http.NetworkError ->
                            "Network error during sync."

                        _ ->
                            "Sync failed. Check the source status for details."
            in
            ( { model
                | syncing = Set.remove id model.syncing
                , error = Just errorMsg
              }
            , Api.fetchCalendarSources SourcesReceived
            )


view : Model -> Html Msg
view model =
    div []
        [ pageHeading "Calendar Sources"
        , p [ class "text-sm text-sand-500 mb-6" ]
            [ text "Calendar sources are configured via environment variables. This page shows their sync status." ]
        , case model.error of
            Just err ->
                errorBanner err

            Nothing ->
                text ""
        , if model.loading then
            loadingSpinner

          else if List.isEmpty model.sources then
            emptyState

          else
            sourcesTable model
        ]


emptyState : Html msg
emptyState =
    card
        [ div [ class "text-center py-8" ]
            [ p [ class "text-sand-400 text-sm" ]
                [ text "No calendar sources configured." ]
            , p [ class "text-sand-400 text-xs mt-2" ]
                [ text "Set MICHAEL_CALDAV_FASTMAIL_URL or MICHAEL_CALDAV_ICLOUD_URL environment variables to connect calendars." ]
            ]
        ]


sourcesTable : Model -> Html Msg
sourcesTable model =
    div [ class "bg-white rounded-lg shadow-sm border border-sand-200 overflow-hidden" ]
        [ table [ class "w-full" ]
            [ thead []
                [ tr [ class "border-b border-sand-200 bg-sand-50" ]
                    [ th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Provider" ]
                    , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Last Synced" ]
                    , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Status" ]
                    , th [ class "text-right px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Actions" ]
                    ]
                ]
            , Keyed.node "tbody"
                []
                (List.map (keyedSourceRow model.syncing) model.sources)
            ]
        ]


keyedSourceRow : Set String -> CalendarSource -> ( String, Html Msg )
keyedSourceRow syncingIds source =
    ( source.id, lazy2 sourceRow syncingIds source )


sourceRow : Set String -> CalendarSource -> Html Msg
sourceRow syncingIds source =
    let
        isSyncing =
            Set.member source.id syncingIds
    in
    tr [ class "border-b border-sand-100" ]
        [ td [ class "px-6 py-4" ]
            [ p [ class "text-sm font-medium text-sand-900" ]
                [ text (providerLabel source.provider) ]
            , p [ class "text-xs text-sand-400 mt-1 truncate max-w-xs" ]
                [ text source.baseUrl ]
            ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text
                (case source.lastSyncedAt of
                    Just time ->
                        formatDateTime time

                    Nothing ->
                        "Never"
                )
            ]
        , td [ class "px-6 py-4" ]
            [ syncStatusBadge source.lastSyncResult ]
        , td [ class "px-6 py-4 text-right" ]
            [ button
                [ class "text-sm text-coral hover:text-coral-dark transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                , onClick (SyncTriggered source.id)
                , disabled isSyncing
                ]
                [ text
                    (if isSyncing then
                        "Syncing..."

                     else
                        "Sync now"
                    )
                ]
            ]
        ]


providerLabel : CalDavProvider -> String
providerLabel provider =
    case provider of
        Fastmail ->
            "Fastmail"

        ICloud ->
            "iCloud"


syncStatusBadge : Maybe String -> Html msg
syncStatusBadge result =
    case result of
        Nothing ->
            span [ class "inline-block px-2 py-1 rounded-full text-xs font-medium bg-sand-100 text-sand-500" ]
                [ text "Pending" ]

        Just "ok" ->
            span [ class "inline-block px-2 py-1 rounded-full text-xs font-medium bg-green-100 text-green-800" ]
                [ text "OK" ]

        Just status ->
            span [ class "inline-block px-2 py-1 rounded-full text-xs font-medium bg-red-100 text-red-800" ]
                [ text
                    (if String.startsWith "error:" status then
                        "Error"

                     else
                        status
                    )
                ]
