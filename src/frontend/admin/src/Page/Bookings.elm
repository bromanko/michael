module Page.Bookings exposing (Model, Msg(..), init, update, view)

import Api
import Html exposing (Html, a, button, div, table, tbody, td, text, th, thead, tr)
import Html.Attributes exposing (class, href)
import Html.Events exposing (onClick)
import Http
import Types exposing (Booking, BookingStatus(..), PaginatedBookings)
import View.Components exposing (errorBanner, loadingSpinner, pageHeading, secondaryButton, statusBadge)


type alias Model =
    { bookings : List Booking
    , totalCount : Int
    , page : Int
    , pageSize : Int
    , statusFilter : Maybe String
    , loading : Bool
    , error : Maybe String
    }


type Msg
    = BookingsReceived (Result Http.Error PaginatedBookings)
    | PageChanged Int
    | StatusFilterChanged (Maybe String)


init : ( Model, Cmd Msg )
init =
    ( { bookings = []
      , totalCount = 0
      , page = 1
      , pageSize = 20
      , statusFilter = Nothing
      , loading = True
      , error = Nothing
      }
    , Api.fetchBookings 1 20 Nothing BookingsReceived
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        BookingsReceived (Ok result) ->
            ( { model
                | bookings = result.bookings
                , totalCount = result.totalCount
                , page = result.page
                , pageSize = result.pageSize
                , loading = False
              }
            , Cmd.none
            )

        BookingsReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load bookings." }, Cmd.none )

        PageChanged newPage ->
            ( { model | loading = True, page = newPage }
            , Api.fetchBookings newPage model.pageSize model.statusFilter BookingsReceived
            )

        StatusFilterChanged filter ->
            ( { model | loading = True, statusFilter = filter, page = 1 }
            , Api.fetchBookings 1 model.pageSize filter BookingsReceived
            )


view : Model -> Html Msg
view model =
    div []
        [ pageHeading "Bookings"
        , case model.error of
            Just err ->
                errorBanner err

            Nothing ->
                text ""
        , filterBar model
        , if model.loading then
            loadingSpinner

          else if List.isEmpty model.bookings then
            emptyState

          else
            div []
                [ bookingsTable model.bookings
                , pagination model
                ]
        ]


filterBar : Model -> Html Msg
filterBar model =
    div [ class "flex gap-2 mb-6" ]
        [ filterButton "All" Nothing model.statusFilter
        , filterButton "Confirmed" (Just "confirmed") model.statusFilter
        , filterButton "Cancelled" (Just "cancelled") model.statusFilter
        ]


filterButton : String -> Maybe String -> Maybe String -> Html Msg
filterButton label filterValue currentFilter =
    let
        isActive =
            filterValue == currentFilter

        classes =
            if isActive then
                "bg-coral text-white"

            else
                "border border-sand-300 text-sand-600 hover:bg-sand-100"
    in
    button
        [ class ("px-4 py-2 rounded-lg text-sm font-medium transition-colors " ++ classes)
        , onClick (StatusFilterChanged filterValue)
        ]
        [ text label ]


emptyState : Html msg
emptyState =
    div [ class "text-center py-12" ]
        [ div [ class "text-sand-400 text-sm" ]
            [ text "No bookings found." ]
        ]


bookingsTable : List Booking -> Html Msg
bookingsTable bookings =
    div [ class "bg-white rounded-lg shadow-sm border border-sand-200 overflow-hidden" ]
        [ table [ class "w-full" ]
            [ thead []
                [ tr [ class "border-b border-sand-200 bg-sand-50" ]
                    [ th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Title" ]
                    , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Participant" ]
                    , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "When" ]
                    , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Duration" ]
                    , th [ class "text-left px-6 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider" ] [ text "Status" ]
                    ]
                ]
            , tbody []
                (List.map bookingRow bookings)
            ]
        ]


bookingRow : Booking -> Html Msg
bookingRow booking =
    tr [ class "border-b border-sand-100 hover:bg-sand-50 transition-colors" ]
        [ td [ class "px-6 py-4" ]
            [ a
                [ href ("/admin/bookings/" ++ booking.id)
                , class "text-sm font-medium text-sand-900 hover:text-coral transition-colors"
                ]
                [ text booking.title ]
            ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text booking.participantName ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text (formatDateTime booking.startTime) ]
        , td [ class "px-6 py-4 text-sm text-sand-600" ]
            [ text (String.fromInt booking.durationMinutes ++ " min") ]
        , td [ class "px-6 py-4" ]
            [ statusBadge booking.status ]
        ]


pagination : Model -> Html Msg
pagination model =
    let
        totalPages =
            ceiling (toFloat model.totalCount / toFloat model.pageSize)

        hasPrev =
            model.page > 1

        hasNext =
            model.page < totalPages
    in
    if totalPages <= 1 then
        text ""

    else
        div [ class "flex items-center justify-between mt-4" ]
            [ div [ class "text-sm text-sand-500" ]
                [ text
                    ("Showing page "
                        ++ String.fromInt model.page
                        ++ " of "
                        ++ String.fromInt totalPages
                    )
                ]
            , div [ class "flex gap-2" ]
                [ if hasPrev then
                    secondaryButton
                        { label = "Previous"
                        , onPress = PageChanged (model.page - 1)
                        }

                  else
                    text ""
                , if hasNext then
                    secondaryButton
                        { label = "Next"
                        , onPress = PageChanged (model.page + 1)
                        }

                  else
                    text ""
                ]
            ]


formatDateTime : String -> String
formatDateTime isoString =
    let
        datePart =
            String.left 10 isoString

        timePart =
            String.slice 11 16 isoString
    in
    datePart ++ " " ++ timePart
