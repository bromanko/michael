module UpdateTest exposing (suite)

import Expect
import Http
import Model
import Test exposing (Test, describe, test)
import TestFixtures
    exposing
        ( sampleParseResponse
        , sampleSlot
        , sampleWindow
        , validToken
        )
import Types
import Update


initModel : Model.Model
initModel =
    let
        ( model, _ ) =
            Model.init
                { timezone = "America/New_York"
                , csrfToken = validToken
                }
    in
    model


modelWithoutToken : Model.Model
modelWithoutToken =
    { initModel | csrfToken = Nothing }


contactInfoModel : Model.Model
contactInfoModel =
    { initModel
        | currentStep = Types.ContactInfoStep
        , name = "Taylor"
        , email = "taylor@example.com"
        , phone = ""
    }


bookingReadyModel : Model.Model
bookingReadyModel =
    { contactInfoModel
        | currentStep = Types.ConfirmationStep
        , selectedSlot = Just sampleSlot
        , parsedWindows = [ sampleWindow ]
    }


updatedModel : Update.Msg -> Model.Model -> Model.Model
updatedModel msg model =
    model
        |> Update.update msg
        |> Tuple.first


suite : Test
suite =
    describe "Update.update"
        [ describe "field updates"
            [ test "TitleUpdated stores text" <|
                \_ ->
                    updatedModel (Update.TitleUpdated "Design sync") initModel
                        |> .title
                        |> Expect.equal "Design sync"
            , test "AvailabilityTextUpdated stores text" <|
                \_ ->
                    updatedModel (Update.AvailabilityTextUpdated "Monday morning") initModel
                        |> .availabilityText
                        |> Expect.equal "Monday morning"
            , test "NameUpdated stores text" <|
                \_ ->
                    updatedModel (Update.NameUpdated "Jordan") initModel
                        |> .name
                        |> Expect.equal "Jordan"
            , test "EmailUpdated stores text" <|
                \_ ->
                    updatedModel (Update.EmailUpdated "jordan@example.com") initModel
                        |> .email
                        |> Expect.equal "jordan@example.com"
            , test "PhoneUpdated stores text" <|
                \_ ->
                    updatedModel (Update.PhoneUpdated "555-1212") initModel
                        |> .phone
                        |> Expect.equal "555-1212"
            ]
        , describe "step completion validation"
            [ test "TitleStepCompleted rejects whitespace-only title" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.TitleStepCompleted { initModel | title = "   " }
                    in
                    model.error
                        |> Expect.equal (Just "Please enter a meeting title.")
            , test "TitleStepCompleted moves to availability step when title is present" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.TitleStepCompleted { initModel | title = "Roadmap" }
                    in
                    model.currentStep
                        |> Expect.equal Types.AvailabilityStep
            , test "AvailabilityStepCompleted rejects whitespace-only availability text" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.AvailabilityStepCompleted { initModel | availabilityText = "\n  " }
                    in
                    model.error
                        |> Expect.equal (Just "Please describe your availability.")
            , test "AvailabilityStepCompleted starts loading when csrf token is present" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.AvailabilityStepCompleted { initModel | availabilityText = "tomorrow at 9" }
                    in
                    model.loading
                        |> Expect.equal True
            , test "AvailabilityStepCompleted without csrf token fails fast" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.AvailabilityStepCompleted
                                { modelWithoutToken | availabilityText = "tomorrow at 9" }
                    in
                    model.error
                        |> Expect.equal (Just "Failed to initialize booking session. Please refresh and try again.")
            , test "ContactInfoStepCompleted rejects missing name" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.ContactInfoStepCompleted { contactInfoModel | name = "   " }
                    in
                    model.error
                        |> Expect.equal (Just "Please enter your name.")
            , test "ContactInfoStepCompleted rejects missing email" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.ContactInfoStepCompleted { contactInfoModel | email = "" }
                    in
                    model.error
                        |> Expect.equal (Just "Please enter your email address.")
            , test "ContactInfoStepCompleted rejects invalid email" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.ContactInfoStepCompleted { contactInfoModel | email = "no-domain@invalid." }
                    in
                    model.error
                        |> Expect.equal (Just "Please enter a valid email address.")
            , test "ContactInfoStepCompleted accepts trimmed valid contact info" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                Update.ContactInfoStepCompleted
                                { contactInfoModel
                                    | name = "  Taylor  "
                                    , email = "  taylor@example.com  "
                                }
                    in
                    model.currentStep
                        |> Expect.equal Types.ConfirmationStep
            ]
        , describe "parse response handling"
            [ test "ParseResponseReceived success moves to availability confirmation when windows exist" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.ParseResponseReceived (Ok sampleParseResponse)) { initModel | loading = True }
                    in
                    model.currentStep
                        |> Expect.equal Types.AvailabilityConfirmStep
            , test "ParseResponseReceived success stores parsed windows on model" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.ParseResponseReceived (Ok sampleParseResponse)) { initModel | loading = True }
                    in
                    Expect.all
                        [ \m -> m.parsedWindows |> Expect.equal [ sampleWindow ]
                        , \m -> m.loading |> Expect.equal False
                        , \m -> m.error |> Expect.equal Nothing
                        ]
                        model
            , test "ParseResponseReceived success with empty windows sets parse error" <|
                \_ ->
                    let
                        baseParseResult =
                            sampleParseResponse.parseResult

                        emptyParseResult =
                            { baseParseResult | availabilityWindows = [] }

                        emptyResponse =
                            { sampleParseResponse | parseResult = emptyParseResult }

                        model =
                            updatedModel (Update.ParseResponseReceived (Ok emptyResponse)) { initModel | loading = True }
                    in
                    model.error
                        |> Expect.equal (Just "Could not parse availability windows. Please try describing your availability differently.")
            , test "ParseResponseReceived 403 triggers csrf refresh on first failure" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.ParseResponseReceived (Err (Http.BadStatus 403))) { initModel | csrfRefreshAttempted = False }
                    in
                    ( model.loading, model.csrfRefreshAttempted )
                        |> Expect.equal ( True, True )
            , test "ParseResponseReceived 403 after retry exhaustion returns session-expired error" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.ParseResponseReceived (Err (Http.BadStatus 403))) { initModel | csrfRefreshAttempted = True }
                    in
                    model.error
                        |> Expect.equal (Just "Booking session expired. Please refresh and try again.")
            , test "ParseResponseReceived network error returns network message" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.ParseResponseReceived (Err Http.NetworkError)) initModel
                    in
                    model.error
                        |> Expect.equal (Just "Network error. Please try again.")
            , test "ParseResponseReceived timeout returns timeout message" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.ParseResponseReceived (Err Http.Timeout)) initModel
                    in
                    model.error
                        |> Expect.equal (Just "Request timed out. Please try again.")
            ]
        , describe "slots loading"
            [ test "AvailabilityWindowsConfirmed starts loading when csrf token exists" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.AvailabilityWindowsConfirmed { initModel | parsedWindows = [ sampleWindow ] }
                    in
                    ( model.loading, model.error )
                        |> Expect.equal ( True, Nothing )
            , test "AvailabilityWindowsConfirmed without csrf token fails fast" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.AvailabilityWindowsConfirmed modelWithoutToken
                    in
                    model.error
                        |> Expect.equal (Just "Failed to initialize booking session. Please refresh and try again.")
            , test "SlotsReceived success moves to slot selection step" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.SlotsReceived (Ok [ sampleSlot ])) { initModel | loading = True }
                    in
                    model.currentStep
                        |> Expect.equal Types.SlotSelectionStep
            , test "SlotsReceived success with empty list moves to slot selection with no slots" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.SlotsReceived (Ok [])) { initModel | loading = True }
                    in
                    Expect.all
                        [ \m -> m.currentStep |> Expect.equal Types.SlotSelectionStep
                        , \m -> m.slots |> Expect.equal []
                        , \m -> m.loading |> Expect.equal False
                        , \m -> m.error |> Expect.equal Nothing
                        ]
                        model
            , test "SlotsReceived 403 triggers csrf refresh on first failure" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.SlotsReceived (Err (Http.BadStatus 403))) { initModel | csrfRefreshAttempted = False }
                    in
                    ( model.loading, model.csrfRefreshAttempted )
                        |> Expect.equal ( True, True )
            , test "SlotsReceived 403 after retry exhaustion returns session-expired error" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.SlotsReceived (Err (Http.BadStatus 403))) { initModel | csrfRefreshAttempted = True }
                    in
                    model.error
                        |> Expect.equal (Just "Booking session expired. Please refresh and try again.")
            , test "SlotsReceived non-403 failure returns generic slots error" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.SlotsReceived (Err Http.NetworkError)) initModel
                    in
                    model.error
                        |> Expect.equal (Just "Failed to load available slots. Please try again.")
            ]
        , describe "slot selection and booking"
            [ test "SlotSelected stores slot and advances to contact info" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.SlotSelected sampleSlot) initModel
                    in
                    ( model.currentStep, model.selectedSlot )
                        |> Expect.equal ( Types.ContactInfoStep, Just sampleSlot )
            , test "BookingConfirmed does nothing when no slot is selected" <|
                \_ ->
                    updatedModel Update.BookingConfirmed initModel
                        |> Expect.equal initModel
            , test "BookingConfirmed starts loading when booking request can be built" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.BookingConfirmed bookingReadyModel
                    in
                    model.loading
                        |> Expect.equal True
            , test "BookingConfirmed without csrf token fails fast" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.BookingConfirmed
                                { bookingReadyModel | csrfToken = Nothing }
                    in
                    model.error
                        |> Expect.equal (Just "Failed to initialize booking session. Please refresh and try again.")
            , test "BookingResultReceived success moves to complete step" <|
                \_ ->
                    let
                        confirmation =
                            { bookingId = "booking-123", confirmed = True }

                        model =
                            updatedModel (Update.BookingResultReceived (Ok confirmation)) { bookingReadyModel | loading = True }
                    in
                    ( model.currentStep, model.bookingResult )
                        |> Expect.equal ( Types.CompleteStep, Just confirmation )
            , test "BookingResultReceived 409 with csrf token returns to slot selection and refetches" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.BookingResultReceived (Err (Http.BadStatus 409)))
                                { bookingReadyModel | currentStep = Types.ConfirmationStep }
                    in
                    ( model.currentStep, model.selectedSlot, model.loading )
                        |> Expect.equal ( Types.SlotSelectionStep, Nothing, True )
            , test "BookingResultReceived 409 without csrf token fails fast after resetting selection" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.BookingResultReceived (Err (Http.BadStatus 409)))
                                { bookingReadyModel
                                    | csrfToken = Nothing
                                    , currentStep = Types.ConfirmationStep
                                }
                    in
                    ( model.currentStep, model.selectedSlot, model.error )
                        |> Expect.equal
                            ( Types.SlotSelectionStep
                            , Nothing
                            , Just "Failed to initialize booking session. Please refresh and try again."
                            )
            , test "BookingResultReceived 403 triggers csrf refresh on first failure" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.BookingResultReceived (Err (Http.BadStatus 403))) { bookingReadyModel | csrfRefreshAttempted = False }
                    in
                    ( model.loading, model.csrfRefreshAttempted )
                        |> Expect.equal ( True, True )
            , test "BookingResultReceived 403 after retry exhaustion returns session-expired error" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.BookingResultReceived (Err (Http.BadStatus 403))) { bookingReadyModel | csrfRefreshAttempted = True }
                    in
                    model.error
                        |> Expect.equal (Just "Booking session expired. Please refresh and try again.")
            , test "BookingResultReceived non-403/409 failure returns generic booking error" <|
                \_ ->
                    let
                        model =
                            updatedModel (Update.BookingResultReceived (Err Http.NetworkError)) bookingReadyModel
                    in
                    model.error
                        |> Expect.equal (Just "Failed to confirm booking. Please try again.")
            ]
        , describe "csrf refresh result messages"
            [ test "CsrfTokenRefreshedForParse accepts valid token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForParse (Ok validToken))
                                { initModel | csrfToken = Nothing, availabilityText = "tomorrow" }
                    in
                    model.csrfToken
                        |> Expect.equal (Just validToken)
            , test "CsrfTokenRefreshedForParse rejects invalid token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForParse (Ok "bad-token"))
                                initModel
                    in
                    model.error
                        |> Expect.equal (Just "Could not refresh booking session token. Please refresh and try again.")
            , test "CsrfTokenRefreshedForParse handles refresh error" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForParse (Err Http.Timeout))
                                initModel
                    in
                    model.error
                        |> Expect.equal (Just "Could not refresh booking session token. Please refresh and try again.")
            , test "CsrfTokenRefreshedForSlots accepts valid token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForSlots (Ok validToken))
                                { initModel | csrfToken = Nothing, parsedWindows = [ sampleWindow ] }
                    in
                    model.csrfToken
                        |> Expect.equal (Just validToken)
            , test "CsrfTokenRefreshedForSlots rejects invalid token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForSlots (Ok "bad-token"))
                                initModel
                    in
                    model.error
                        |> Expect.equal (Just "Could not refresh booking session token. Please refresh and try again.")
            , test "CsrfTokenRefreshedForSlots handles refresh error" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForSlots (Err Http.NetworkError))
                                initModel
                    in
                    model.error
                        |> Expect.equal (Just "Could not refresh booking session token. Please refresh and try again.")
            , test "CsrfTokenRefreshedForBook accepts valid token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForBook (Ok validToken))
                                { bookingReadyModel | csrfToken = Nothing }
                    in
                    model.csrfToken
                        |> Expect.equal (Just validToken)
            , test "CsrfTokenRefreshedForBook rejects invalid token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForBook (Ok "bad-token"))
                                bookingReadyModel
                    in
                    model.error
                        |> Expect.equal (Just "Could not refresh booking session token. Please refresh and try again.")
            , test "CsrfTokenRefreshedForBook handles refresh error" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.CsrfTokenRefreshedForBook (Err Http.Timeout))
                                bookingReadyModel
                    in
                    model.error
                        |> Expect.equal (Just "Could not refresh booking session token. Please refresh and try again.")
            ]
        , describe "timezone behavior"
            [ test "TimezoneChanged updates timezone and closes dropdown in non-fetch steps" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "Europe/Berlin")
                                { initModel | timezoneDropdownOpen = True }
                    in
                    ( model.timezone, model.timezoneDropdownOpen, model.loading )
                        |> Expect.equal ( "Europe/Berlin", False, False )
            , test "TimezoneChanged with invalid timezone falls back to UTC" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "invalid")
                                initModel
                    in
                    model.timezone
                        |> Expect.equal "UTC"
            , test "TimezoneChanged with empty string falls back to UTC" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "")
                                initModel
                    in
                    model.timezone
                        |> Expect.equal "UTC"
            , test "TimezoneChanged on AvailabilityConfirmStep triggers parse flow when token exists" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "UTC")
                                { initModel
                                    | currentStep = Types.AvailabilityConfirmStep
                                    , availabilityText = "next monday"
                                }
                    in
                    ( model.timezone, model.loading, model.error )
                        |> Expect.equal ( "UTC", True, Nothing )
            , test "TimezoneChanged on AvailabilityConfirmStep fails fast without token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "UTC")
                                { modelWithoutToken
                                    | currentStep = Types.AvailabilityConfirmStep
                                    , availabilityText = "next monday"
                                }
                    in
                    model.error
                        |> Expect.equal (Just "Failed to initialize booking session. Please refresh and try again.")
            , test "TimezoneChanged on SlotSelectionStep triggers slot reload when token exists" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "Europe/London")
                                { initModel
                                    | currentStep = Types.SlotSelectionStep
                                    , parsedWindows = [ sampleWindow ]
                                }
                    in
                    ( model.timezone, model.loading )
                        |> Expect.equal ( "Europe/London", True )
            , test "TimezoneChanged on SlotSelectionStep fails fast without token" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                (Update.TimezoneChanged "Europe/London")
                                { modelWithoutToken
                                    | currentStep = Types.SlotSelectionStep
                                    , parsedWindows = [ sampleWindow ]
                                }
                    in
                    model.error
                        |> Expect.equal (Just "Failed to initialize booking session. Please refresh and try again.")
            , test "TimezoneDropdownToggled flips the open state" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.TimezoneDropdownToggled { initModel | timezoneDropdownOpen = False }
                    in
                    model.timezoneDropdownOpen
                        |> Expect.equal True
            ]
        , describe "back navigation"
            [ test "BackStepClicked from SlotSelectionStep clears slots and selection" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                Update.BackStepClicked
                                { initModel
                                    | currentStep = Types.SlotSelectionStep
                                    , slots = [ sampleSlot ]
                                    , selectedSlot = Just sampleSlot
                                    , error = Just "old"
                                }
                    in
                    { currentStep = model.currentStep
                    , slots = model.slots
                    , selectedSlot = model.selectedSlot
                    , error = model.error
                    }
                        |> Expect.equal
                            { currentStep = Types.AvailabilityConfirmStep
                            , slots = []
                            , selectedSlot = Nothing
                            , error = Nothing
                            }
            , test "BackStepClicked from AvailabilityConfirmStep clears parsed windows" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                Update.BackStepClicked
                                { initModel
                                    | currentStep = Types.AvailabilityConfirmStep
                                    , parsedWindows = [ sampleWindow ]
                                }
                    in
                    ( model.currentStep, model.parsedWindows )
                        |> Expect.equal ( Types.AvailabilityStep, [] )
            , test "BackStepClicked from ContactInfoStep goes to SlotSelectionStep" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                Update.BackStepClicked
                                { initModel
                                    | currentStep = Types.ContactInfoStep
                                    , error = Just "old"
                                }
                    in
                    ( model.currentStep, model.error )
                        |> Expect.equal ( Types.SlotSelectionStep, Nothing )
            , test "BackStepClicked from ConfirmationStep goes to ContactInfoStep" <|
                \_ ->
                    let
                        model =
                            updatedModel
                                Update.BackStepClicked
                                { initModel
                                    | currentStep = Types.ConfirmationStep
                                    , error = Just "old"
                                }
                    in
                    ( model.currentStep, model.error )
                        |> Expect.equal ( Types.ContactInfoStep, Nothing )
            , test "BackStepClicked at TitleStep is idempotent" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.BackStepClicked { initModel | currentStep = Types.TitleStep }
                    in
                    model.currentStep
                        |> Expect.equal Types.TitleStep
            , test "BackStepClicked at CompleteStep is idempotent" <|
                \_ ->
                    let
                        model =
                            updatedModel Update.BackStepClicked { initModel | currentStep = Types.CompleteStep }
                    in
                    model.currentStep
                        |> Expect.equal Types.CompleteStep
            ]
        , describe "NoOp"
            [ test "NoOp leaves model unchanged" <|
                \_ ->
                    updatedModel Update.NoOp bookingReadyModel
                        |> Expect.equal bookingReadyModel
            ]
        ]
