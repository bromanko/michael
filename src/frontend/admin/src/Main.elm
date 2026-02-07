module Main exposing (main)

import Api
import Browser
import Browser.Navigation as Nav
import Html exposing (Html, text)
import Html.Attributes
import Http
import Page.Availability as Availability
import Page.BookingDetail as BookingDetail
import Page.Bookings as Bookings
import Page.CalendarView as CalendarView
import Page.Calendars as Calendars
import Page.Dashboard as Dashboard
import Page.Login as Login
import Page.Settings as Settings
import Route
import Types exposing (Route(..), Session(..))
import Url exposing (Url)
import View.Layout as Layout


type alias Flags =
    { timezone : String
    , currentDate : String
    }


type alias Model =
    { key : Nav.Key
    , session : Session
    , route : Route
    , page : PageModel
    , navOpen : Bool
    , timezone : String
    , currentDate : String
    }


type PageModel
    = DashboardPage Dashboard.Model
    | BookingsPage Bookings.Model
    | BookingDetailPage BookingDetail.Model
    | CalendarsPage Calendars.Model
    | CalendarViewPage CalendarView.Model
    | AvailabilityPage Availability.Model
    | SettingsPage Settings.Model
    | LoginPage Login.Model
    | NotFoundPage


type Msg
    = UrlRequested Browser.UrlRequest
    | UrlChanged Url
    | SessionChecked (Result Http.Error ())
    | LogoutCompleted (Result Http.Error ())
    | NavToggled
    | DashboardMsg Dashboard.Msg
    | BookingsMsg Bookings.Msg
    | BookingDetailMsg BookingDetail.Msg
    | CalendarsMsg Calendars.Msg
    | CalendarViewMsg CalendarView.Msg
    | AvailabilityMsg Availability.Msg
    | SettingsMsg Settings.Msg
    | LoginMsg Login.Msg
    | LogoutClicked


main : Program Flags Model Msg
main =
    Browser.application
        { init = init
        , view = view
        , update = update
        , subscriptions = \_ -> Sub.none
        , onUrlRequest = UrlRequested
        , onUrlChange = UrlChanged
        }


init : Flags -> Url -> Nav.Key -> ( Model, Cmd Msg )
init flags url key =
    let
        route =
            Route.fromUrl url
    in
    ( { key = key
      , session = Checking
      , route = route
      , page = NotFoundPage
      , navOpen = False
      , timezone = flags.timezone
      , currentDate = flags.currentDate
      }
    , Api.checkSession SessionChecked
    )


update : Msg -> Model -> ( Model, Cmd Msg )
update msg model =
    case msg of
        UrlRequested urlRequest ->
            case urlRequest of
                Browser.Internal url ->
                    ( model, Nav.pushUrl model.key (Url.toString url) )

                Browser.External href ->
                    ( model, Nav.load href )

        UrlChanged url ->
            let
                route =
                    Route.fromUrl url
            in
            loadPage route { model | route = route, navOpen = False }

        SessionChecked (Ok _) ->
            let
                newModel =
                    { model | session = LoggedIn }
            in
            case model.route of
                Login ->
                    ( newModel, Nav.replaceUrl model.key (Route.toPath Dashboard) )

                _ ->
                    loadPage model.route newModel

        SessionChecked (Err _) ->
            let
                newModel =
                    { model | session = Guest }
            in
            case model.route of
                Login ->
                    loadPage Login newModel

                _ ->
                    ( newModel, Nav.replaceUrl model.key (Route.toPath Login) )

        LogoutClicked ->
            ( model, Api.logout LogoutCompleted )

        LogoutCompleted (Ok ()) ->
            ( { model | session = Guest }
            , Nav.replaceUrl model.key (Route.toPath Login)
            )

        LogoutCompleted (Err _) ->
            ( { model | session = Guest }
            , Nav.replaceUrl model.key (Route.toPath Login)
            )

        NavToggled ->
            ( { model | navOpen = not model.navOpen }, Cmd.none )

        DashboardMsg subMsg ->
            case model.page of
                DashboardPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            Dashboard.update subMsg subModel
                    in
                    ( { model | page = DashboardPage newSubModel }
                    , Cmd.map DashboardMsg subCmd
                    )

                _ ->
                    ( model, Cmd.none )

        BookingsMsg subMsg ->
            case model.page of
                BookingsPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            Bookings.update subMsg subModel
                    in
                    ( { model | page = BookingsPage newSubModel }
                    , Cmd.map BookingsMsg subCmd
                    )

                _ ->
                    ( model, Cmd.none )

        BookingDetailMsg subMsg ->
            case model.page of
                BookingDetailPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            BookingDetail.update subMsg subModel
                    in
                    ( { model | page = BookingDetailPage newSubModel }
                    , Cmd.map BookingDetailMsg subCmd
                    )

                _ ->
                    ( model, Cmd.none )

        CalendarsMsg subMsg ->
            case model.page of
                CalendarsPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            Calendars.update subMsg subModel
                    in
                    ( { model | page = CalendarsPage newSubModel }
                    , Cmd.map CalendarsMsg subCmd
                    )

                _ ->
                    ( model, Cmd.none )

        AvailabilityMsg subMsg ->
            case model.page of
                AvailabilityPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            Availability.update subMsg subModel
                    in
                    ( { model | page = AvailabilityPage newSubModel }
                    , Cmd.map AvailabilityMsg subCmd
                    )

                _ ->
                    ( model, Cmd.none )

        SettingsMsg subMsg ->
            case model.page of
                SettingsPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            Settings.update subMsg subModel
                    in
                    ( { model | page = SettingsPage newSubModel }
                    , Cmd.map SettingsMsg subCmd
                    )

                _ ->
                    ( model, Cmd.none )

        CalendarViewMsg subMsg ->
            case model.page of
                CalendarViewPage subModel ->
                    let
                        ( newSubModel, subCmd ) =
                            CalendarView.update subMsg subModel

                        urlCmd =
                            if newSubModel.currentWeekStart /= subModel.currentWeekStart then
                                Nav.replaceUrl model.key
                                    (Route.toPath (CalendarViewRoute (Just (CalendarView.urlWeekStart newSubModel))))

                            else
                                Cmd.none
                    in
                    ( { model | page = CalendarViewPage newSubModel }
                    , Cmd.batch [ Cmd.map CalendarViewMsg subCmd, urlCmd ]
                    )

                _ ->
                    ( model, Cmd.none )

        LoginMsg subMsg ->
            case model.page of
                LoginPage subModel ->
                    let
                        ( newSubModel, subCmd, maybeOutMsg ) =
                            Login.update subMsg subModel
                    in
                    case maybeOutMsg of
                        Just Login.LoginSucceeded ->
                            ( { model | session = LoggedIn }
                            , Nav.replaceUrl model.key (Route.toPath Dashboard)
                            )

                        Nothing ->
                            ( { model | page = LoginPage newSubModel }
                            , Cmd.map LoginMsg subCmd
                            )

                _ ->
                    ( model, Cmd.none )


loadPage : Route -> Model -> ( Model, Cmd Msg )
loadPage route model =
    case route of
        Dashboard ->
            let
                ( subModel, subCmd ) =
                    Dashboard.init
            in
            ( { model | page = DashboardPage subModel }
            , Cmd.map DashboardMsg subCmd
            )

        Bookings ->
            let
                ( subModel, subCmd ) =
                    Bookings.init
            in
            ( { model | page = BookingsPage subModel }
            , Cmd.map BookingsMsg subCmd
            )

        BookingDetail id ->
            let
                ( subModel, subCmd ) =
                    BookingDetail.init id
            in
            ( { model | page = BookingDetailPage subModel }
            , Cmd.map BookingDetailMsg subCmd
            )

        Login ->
            let
                ( subModel, subCmd ) =
                    Login.init
            in
            ( { model | page = LoginPage subModel }
            , Cmd.map LoginMsg subCmd
            )

        Calendars ->
            let
                ( subModel, subCmd ) =
                    Calendars.init
            in
            ( { model | page = CalendarsPage subModel }
            , Cmd.map CalendarsMsg subCmd
            )

        Availability ->
            let
                ( subModel, subCmd ) =
                    Availability.init
            in
            ( { model | page = AvailabilityPage subModel }
            , Cmd.map AvailabilityMsg subCmd
            )

        Settings ->
            let
                ( subModel, subCmd ) =
                    Settings.init
            in
            ( { model | page = SettingsPage subModel }
            , Cmd.map SettingsMsg subCmd
            )

        CalendarViewRoute maybeDate ->
            let
                ( subModel, subCmd ) =
                    CalendarView.init model.timezone model.currentDate maybeDate
            in
            ( { model | page = CalendarViewPage subModel }
            , Cmd.map CalendarViewMsg subCmd
            )

        NotFound ->
            ( { model | page = NotFoundPage }, Cmd.none )


view : Model -> Browser.Document Msg
view model =
    { title = "Michael Admin"
    , body =
        [ case model.session of
            Checking ->
                Html.div [ Html.Attributes.class "min-h-screen flex items-center justify-center bg-sand-50" ]
                    [ Html.div [ Html.Attributes.class "text-sand-400 text-sm" ]
                        [ text "Loading..." ]
                    ]

            Guest ->
                case model.page of
                    LoginPage subModel ->
                        Html.map LoginMsg (Login.view subModel)

                    _ ->
                        text ""

            LoggedIn ->
                Layout.view
                    { route = model.route
                    , navOpen = model.navOpen
                    , onToggleNav = NavToggled
                    , onLogout = LogoutClicked
                    , content = pageView model
                    }
        ]
    }


pageView : Model -> Html Msg
pageView model =
    case model.page of
        DashboardPage subModel ->
            Html.map DashboardMsg (Dashboard.view subModel)

        BookingsPage subModel ->
            Html.map BookingsMsg (Bookings.view subModel)

        BookingDetailPage subModel ->
            Html.map BookingDetailMsg (BookingDetail.view subModel)

        CalendarsPage subModel ->
            Html.map CalendarsMsg (Calendars.view subModel)

        CalendarViewPage subModel ->
            Html.map CalendarViewMsg (CalendarView.view subModel)

        AvailabilityPage subModel ->
            Html.map AvailabilityMsg (Availability.view subModel)

        SettingsPage subModel ->
            Html.map SettingsMsg (Settings.view subModel)

        LoginPage subModel ->
            Html.map LoginMsg (Login.view subModel)

        NotFoundPage ->
            Html.div [ Html.Attributes.class "text-center py-12" ]
                [ Html.p [ Html.Attributes.class "text-sand-400" ]
                    [ text "Page not found." ]
                ]
