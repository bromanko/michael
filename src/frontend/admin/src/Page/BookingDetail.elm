module Page.BookingDetail exposing (Model, Msg, init, update, view)

import Api
import Html exposing (Html, a, div, p, text)
import Html.Attributes exposing (class, href)
import Http
import Route
import Types exposing (Booking, BookingStatus(..), Route(..))
import View.Components exposing (card, dangerButton, errorBanner, formatDateTime, loadingSpinner, pageHeading, secondaryButton, statusBadge)


type alias Model =
    { bookingId : String
    , booking : Maybe Booking
    , loading : Bool
    , cancelling : Bool
    , error : Maybe String
    , cancelConfirm : Bool
    }


type Msg
    = BookingReceived (Result Http.Error Booking)
    | CancelClicked
    | CancelConfirmed
    | CancelDismissed
    | CancellationCompleted (Result Http.Error ())


init : String -> ( Model, Cmd Msg )
init bookingId =
    ( { bookingId = bookingId
      , booking = Nothing
      , loading = True
      , cancelling = False
      , error = Nothing
      , cancelConfirm = False
      }
    , Api.fetchBooking bookingId BookingReceived
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        BookingReceived (Ok booking) ->
            ( { model | booking = Just booking, loading = False }, Cmd.none )

        BookingReceived (Err _) ->
            ( { model | loading = False, error = Just "Failed to load booking." }, Cmd.none )

        CancelClicked ->
            ( { model | cancelConfirm = True }, Cmd.none )

        CancelDismissed ->
            ( { model | cancelConfirm = False }, Cmd.none )

        CancelConfirmed ->
            ( { model | cancelling = True, cancelConfirm = False }
            , Api.cancelBooking model.bookingId CancellationCompleted
            )

        CancellationCompleted (Ok _) ->
            -- Re-fetch the booking to get updated status
            ( { model | cancelling = False }
            , Api.fetchBooking model.bookingId BookingReceived
            )

        CancellationCompleted (Err _) ->
            ( { model | cancelling = False, error = Just "Failed to cancel booking." }, Cmd.none )


view : Model -> Html Msg
view model =
    div []
        [ div [ class "flex items-center gap-2 mb-6" ]
            [ a [ href (Route.toPath Bookings), class "text-sm text-sand-500 hover:text-sand-700 transition-colors" ]
                [ text "Bookings" ]
            , p [ class "text-sm text-sand-400" ] [ text "/" ]
            , p [ class "text-sm text-sand-700" ] [ text "Detail" ]
            ]
        , case model.error of
            Just err ->
                errorBanner err

            Nothing ->
                text ""
        , if model.loading then
            loadingSpinner

          else
            case model.booking of
                Just booking ->
                    bookingDetailView model booking

                Nothing ->
                    text ""
        ]


bookingDetailView : Model -> Booking -> Html Msg
bookingDetailView model booking =
    div []
        [ div [ class "flex items-center justify-between mb-6" ]
            [ pageHeading booking.title
            , statusBadge booking.status
            ]
        , card
            [ div [ class "grid grid-cols-1 md:grid-cols-2 gap-6" ]
                [ detailField "Participant" booking.participantName
                , detailField "Email" booking.participantEmail
                , case booking.participantPhone of
                    Just phone ->
                        detailField "Phone" phone

                    Nothing ->
                        text ""
                , detailField "Date & Time" (formatDateTime booking.startTime)
                , detailField "Duration" (String.fromInt booking.durationMinutes ++ " minutes")
                , detailField "Timezone" booking.timezone
                , case booking.description of
                    Just desc ->
                        detailField "Description" desc

                    Nothing ->
                        text ""
                , detailField "Created" (formatDateTime booking.createdAt)
                ]
            ]
        , case booking.status of
            Confirmed ->
                div [ class "mt-6" ]
                    [ if model.cancelConfirm then
                        confirmCancelView model

                      else
                        dangerButton
                            { label = "Cancel Booking"
                            , onPress = CancelClicked
                            , isDisabled = False
                            , isLoading = model.cancelling
                            }
                    ]

            Cancelled ->
                text ""
        ]


confirmCancelView : Model -> Html Msg
confirmCancelView model =
    card
        [ p [ class "text-sm text-sand-700 mb-4" ]
            [ text "Are you sure you want to cancel this booking? This cannot be undone." ]
        , div [ class "flex gap-3" ]
            [ dangerButton
                { label = "Yes, Cancel Booking"
                , onPress = CancelConfirmed
                , isDisabled = False
                , isLoading = model.cancelling
                }
            , secondaryButton
                { label = "Keep Booking"
                , onPress = CancelDismissed
                }
            ]
        ]


detailField : String -> String -> Html msg
detailField label value =
    div []
        [ p [ class "text-xs font-medium text-sand-500 uppercase tracking-wider mb-1" ]
            [ text label ]
        , p [ class "text-sm text-sand-900" ]
            [ text value ]
        ]
