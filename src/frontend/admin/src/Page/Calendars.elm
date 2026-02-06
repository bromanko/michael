module Page.Calendars exposing (Model, Msg, init, update, view)

import Api
import Html exposing (Html, button, div, h3, li, p, span, table, td, text, th, thead, tr, ul)
import Html.Attributes exposing (class, disabled)
import Html.Events exposing (onClick)
import Html.Keyed as Keyed
import Html.Lazy exposing (lazy2)
import Http
import Set exposing (Set)
import Types exposing (CalDavProvider(..), CalendarSource, SyncHistoryEntry, SyncStatus(..))
import View.Components exposing (card, errorBanner, formatDateTime, loadingSpinner, pageHeading)


type alias Model =
    { sources : List CalendarSource
    , loading : Bool
    , error : Maybe String
    , syncing : Set String
    , expandedSource : Maybe String
    , history : List SyncHistoryEntry
    , historyLoading : Bool
    }


type Msg
    = SourcesReceived (Result Http.Error (List CalendarSource))
    | SyncTriggered String
    | SyncCompleted String (Result Http.Error ())
    | HistoryToggled String
    | HistoryReceived String (Result Http.Error (List SyncHistoryEntry))


init : ( Model, Cmd Msg )
init =
    ( { sources = []
      , loading = True
      , error = Nothing
      , syncing = Set.empty
      , expandedSource = Nothing
      , history = []
      , historyLoading = False
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
            let
                cmds =
                    [ Api.fetchCalendarSources SourcesReceived
                    , case model.expandedSource of
                        Just expandedId ->
                            if expandedId == id then
                                Api.fetchSyncHistory id (HistoryReceived id)

                            else
                                Cmd.none

                        Nothing ->
                            Cmd.none
                    ]
            in
            ( { model | syncing = Set.remove id model.syncing }
            , Cmd.batch cmds
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

                cmds =
                    [ Api.fetchCalendarSources SourcesReceived
                    , case model.expandedSource of
                        Just expandedId ->
                            if expandedId == id then
                                Api.fetchSyncHistory id (HistoryReceived id)

                            else
                                Cmd.none

                        Nothing ->
                            Cmd.none
                    ]
            in
            ( { model
                | syncing = Set.remove id model.syncing
                , error = Just errorMsg
              }
            , Cmd.batch cmds
            )

        HistoryToggled sourceId ->
            case model.expandedSource of
                Just currentId ->
                    if currentId == sourceId then
                        ( { model | expandedSource = Nothing, history = [] }, Cmd.none )

                    else
                        ( { model | expandedSource = Just sourceId, history = [], historyLoading = True }
                        , Api.fetchSyncHistory sourceId (HistoryReceived sourceId)
                        )

                Nothing ->
                    ( { model | expandedSource = Just sourceId, history = [], historyLoading = True }
                    , Api.fetchSyncHistory sourceId (HistoryReceived sourceId)
                    )

        HistoryReceived sourceId (Ok entries) ->
            case model.expandedSource of
                Just currentId ->
                    if currentId == sourceId then
                        ( { model | history = entries, historyLoading = False }, Cmd.none )

                    else
                        ( model, Cmd.none )

                Nothing ->
                    ( model, Cmd.none )

        HistoryReceived _ (Err _) ->
            ( { model | historyLoading = False }, Cmd.none )


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
            sourcesList model
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


sourcesList : Model -> Html Msg
sourcesList model =
    div [ class "space-y-4" ]
        (List.map (sourceCard model) model.sources)


sourceCard : Model -> CalendarSource -> Html Msg
sourceCard model source =
    let
        isSyncing =
            Set.member source.id model.syncing

        isExpanded =
            model.expandedSource == Just source.id

        hasError =
            case source.lastSyncResult of
                Just result ->
                    String.startsWith "error:" result

                Nothing ->
                    False
    in
    div
        [ class
            ("bg-white rounded-lg shadow-sm border overflow-hidden "
                ++ (if hasError then
                        "border-red-200"

                    else
                        "border-sand-200"
                   )
            )
        ]
        [ div [ class "px-4 py-4 sm:px-6" ]
            [ div [ class "flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3" ]
                [ div [ class "min-w-0 flex-1" ]
                    [ div [ class "flex items-center gap-2 flex-wrap" ]
                        [ p [ class "text-sm font-medium text-sand-900" ]
                            [ text (providerLabel source.provider) ]
                        , syncStatusBadge source.lastSyncResult
                        ]
                    , p [ class "text-xs text-sand-400 mt-1 truncate" ]
                        [ text source.baseUrl ]
                    , p [ class "text-xs text-sand-500 mt-1" ]
                        [ text
                            ("Last synced: "
                                ++ (case source.lastSyncedAt of
                                        Just time ->
                                            formatDateTime time

                                        Nothing ->
                                            "Never"
                                   )
                            )
                        ]
                    ]
                , div [ class "flex items-center gap-2 flex-shrink-0" ]
                    [ button
                        [ class "text-sm text-sand-500 hover:text-sand-700 transition-colors"
                        , onClick (HistoryToggled source.id)
                        ]
                        [ text
                            (if isExpanded then
                                "Hide history"

                             else
                                "Sync history"
                            )
                        ]
                    , button
                        [ class "text-sm text-coral hover:text-coral-dark transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                        , onClick (SyncTriggered source.id)
                        , disabled isSyncing
                        ]
                        [ text
                            (if isSyncing then
                                "Syncing…"

                             else
                                "Sync now"
                            )
                        ]
                    ]
                ]
            , errorDetail source
            ]
        , if isExpanded then
            historyPanel model

          else
            text ""
        ]


errorDetail : CalendarSource -> Html msg
errorDetail source =
    case source.lastSyncResult of
        Just result ->
            if String.startsWith "error:" result then
                div [ class "mt-3 p-3 bg-red-50 rounded-md" ]
                    [ p [ class "text-xs font-medium text-red-800" ]
                        [ text "Last sync error" ]
                    , p [ class "text-xs text-red-700 mt-1 break-words" ]
                        [ text (String.dropLeft 7 result) ]
                    ]

            else
                text ""

        Nothing ->
            text ""


historyPanel : Model -> Html msg
historyPanel model =
    div [ class "border-t border-sand-200 px-4 py-4 sm:px-6 bg-sand-50" ]
        [ h3 [ class "text-xs font-medium text-sand-500 uppercase tracking-wider mb-3" ]
            [ text "Recent sync history" ]
        , if model.historyLoading then
            div [ class "text-xs text-sand-400 py-2" ] [ text "Loading…" ]

          else if List.isEmpty model.history then
            div [ class "text-xs text-sand-400 py-2" ] [ text "No sync history recorded yet." ]

          else
            ul [ class "space-y-2" ]
                (List.map historyEntry model.history)
        ]


historyEntry : SyncHistoryEntry -> Html msg
historyEntry entry =
    li [ class "flex flex-col sm:flex-row sm:items-start gap-1 sm:gap-3 text-xs" ]
        [ span [ class "text-sand-500 flex-shrink-0 whitespace-nowrap" ]
            [ text (formatDateTime entry.syncedAt) ]
        , historyStatusBadge entry.status
        , case entry.errorMessage of
            Just msg ->
                span [ class "text-red-600 break-words min-w-0" ]
                    [ text msg ]

            Nothing ->
                text ""
        ]


historyStatusBadge : SyncStatus -> Html msg
historyStatusBadge status =
    case status of
        SyncOk ->
            span [ class "inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800 flex-shrink-0" ]
                [ text "OK" ]

        SyncError ->
            span [ class "inline-block px-2 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800 flex-shrink-0" ]
                [ text "Error" ]


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

        Just _ ->
            span [ class "inline-block px-2 py-1 rounded-full text-xs font-medium bg-red-100 text-red-800" ]
                [ text "Error" ]
