module Page.Bookings exposing (Model, Msg(..), init, update, view)

import Api
import Html exposing (Html, a, button, div, table, td, text, th, thead, tr)
import Html.Attributes exposing (class, href)
import Html.Events exposing (onClick)
import Html.Keyed as Keyed
import Html.Lazy exposing (lazy)
import Http
import Route
import Types exposing (Booking, PaginatedBookings, Route(..), StatusFilter(..))
import View.Components exposing (errorBanner, formatDateTime, loadingSpinner, pageHeading, secondaryButton, statusBadge)


type alias Model =
    { bookings : List Booking
    , totalCount : Int
    , page : Int
    , pageSize : Int
    , statusFilter : StatusFilter
    , loading : Bool
    , error : Maybe String
    }


type Msg
    = BookingsReceived (Result Http.Error PaginatedBookings)
    | PageChanged Int
    | StatusFilterChanged StatusFilter


init : ( Model, Cmd Msg )
init =
    ( { bookings = []
      , totalCount = 0
      , page = 1
      , pageSize = 20
      , statusFilter = AllBookings
      , loading = True
      , error = Nothing
      }
    , Api.fetchBookings 1 20 AllBookings BookingsReceived
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
    div [ class "flex flex-wrap gap-2 mb-6" ]
        [ filterButton "All" AllBookings model.statusFilter
        , filterButton "Confirmed" OnlyConfirmed model.statusFilter
        , filterButton "Cancelled" OnlyCancelled model.statusFilter
        ]


filterButton : String -> StatusFilter -> StatusFilter -> Html Msg
filterButton label filterValue currentFilter =
    let
        isActive =
            case ( filterValue, currentFilter ) of
                ( AllBookings, AllBookings ) ->
                    True

                ( OnlyConfirmed, OnlyConfirmed ) ->
                    True

                ( OnlyCancelled, OnlyCancelled ) ->
                    True

                _ ->
                    False

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
    div [ class "bg-white rounded-lg shadow-sm border border-sand-200 overflow-x-auto" ]
        [ table [ class "w-full min-w-[600px]" ]
            [ thead []
                [ tr [ class "border-b border-sand-200 bg-sand-50" ]
                    [ th [ class "text-left px-4 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider sm:px-6" ] [ text "Title" ]
                    , th [ class "text-left px-4 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider sm:px-6" ] [ text "Participant" ]
                    , th [ class "text-left px-4 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider sm:px-6" ] [ text "When" ]
                    , th [ class "text-left px-4 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider sm:px-6" ] [ text "Duration" ]
                    , th [ class "text-left px-4 py-3 text-xs font-medium text-sand-500 uppercase tracking-wider sm:px-6" ] [ text "Status" ]
                    ]
                ]
            , Keyed.node "tbody"
                []
                (List.map keyedBookingRow bookings)
            ]
        ]


keyedBookingRow : Booking -> ( String, Html Msg )
keyedBookingRow booking =
    ( booking.id, lazy bookingRow booking )


bookingRow : Booking -> Html Msg
bookingRow booking =
    tr [ class "border-b border-sand-100 hover:bg-sand-50 transition-colors" ]
        [ td [ class "px-4 py-4 sm:px-6" ]
            [ a
                [ href (Route.toPath (BookingDetail booking.id))
                , class "text-sm font-medium text-sand-900 hover:text-coral transition-colors"
                ]
                [ text booking.title ]
            ]
        , td [ class "px-4 py-4 text-sm text-sand-600 sm:px-6" ]
            [ text booking.participantName ]
        , td [ class "px-4 py-4 text-sm text-sand-600 whitespace-nowrap sm:px-6" ]
            [ text (formatDateTime booking.startTime) ]
        , td [ class "px-4 py-4 text-sm text-sand-600 whitespace-nowrap sm:px-6" ]
            [ text (String.fromInt booking.durationMinutes ++ " min") ]
        , td [ class "px-4 py-4 sm:px-6" ]
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
        div [ class "flex flex-col sm:flex-row items-start sm:items-center justify-between gap-2 mt-4" ]
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
