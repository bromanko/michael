module ViewTest exposing (suite)

import Html.Attributes
import Model
import Test exposing (Test, describe, test)
import Test.Html.Query as Query
import Test.Html.Selector as Sel
import TestFixtures exposing (sampleSlot, validToken)
import Types
import View


baseModel : Model.Model
baseModel =
    let
        ( model, _ ) =
            Model.init
                { timezone = "America/New_York"
                , csrfToken = validToken
                }
    in
    model


roleAlert : Sel.Selector
roleAlert =
    Sel.attribute (Html.Attributes.attribute "role" "alert")


suite : Test
suite =
    describe "View"
        [ describe "error banner"
            [ test "shows error message when model.error is set" <|
                \_ ->
                    { baseModel | error = Just "Something went wrong" }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.find [ roleAlert ]
                        |> Query.has [ Sel.text "Something went wrong" ]
            , test "does not show error banner when model.error is Nothing" <|
                \_ ->
                    { baseModel | error = Nothing }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.hasNot [ roleAlert ]
            ]
        , describe "TitleStep"
            [ test "renders title input" <|
                \_ ->
                    { baseModel | currentStep = Types.TitleStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.id "title-input" ]
            , test "renders heading" <|
                \_ ->
                    { baseModel | currentStep = Types.TitleStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "What would you like to meet about?" ]
            , test "submit button is disabled when title is blank" <|
                \_ ->
                    { baseModel | currentStep = Types.TitleStep, title = "" }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.find [ Sel.id "title-submit-btn" ]
                        |> Query.has [ Sel.disabled True ]
            , test "submit button is enabled when title has text" <|
                \_ ->
                    { baseModel | currentStep = Types.TitleStep, title = "Design sync" }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.find [ Sel.id "title-submit-btn" ]
                        |> Query.has [ Sel.disabled False ]
            ]
        , describe "AvailabilityStep"
            [ test "renders availability textarea" <|
                \_ ->
                    { baseModel | currentStep = Types.AvailabilityStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.id "availability-input" ]
            , test "shows loading label when loading" <|
                \_ ->
                    { baseModel | currentStep = Types.AvailabilityStep, loading = True, availabilityText = "tomorrow" }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.find [ Sel.id "availability-submit-btn" ]
                        |> Query.has [ Sel.text "Finding slots..." ]
            , test "has back button" <|
                \_ ->
                    { baseModel | currentStep = Types.AvailabilityStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.id "availability-back-btn" ]
            ]
        , describe "SlotSelectionStep"
            [ test "shows empty state when no slots" <|
                \_ ->
                    { baseModel | currentStep = Types.SlotSelectionStep, slots = [] }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "No overlapping slots found for those times." ]
            , test "renders slot buttons when slots exist" <|
                \_ ->
                    { baseModel | currentStep = Types.SlotSelectionStep, slots = [ sampleSlot ] }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.id "slot-0" ]
            , test "shows try different times button when empty" <|
                \_ ->
                    { baseModel | currentStep = Types.SlotSelectionStep, slots = [] }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.id "slot-selection-try-different-times-btn" ]
            ]
        , describe "ContactInfoStep"
            [ test "renders name, email, and phone inputs" <|
                \_ ->
                    { baseModel | currentStep = Types.ContactInfoStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has
                            [ Sel.id "name-input"
                            , Sel.id "email-input"
                            , Sel.id "phone-input"
                            ]
            , test "phone field is labeled optional" <|
                \_ ->
                    { baseModel | currentStep = Types.ContactInfoStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "(optional)" ]
            ]
        , describe "ConfirmationStep"
            [ test "shows booking details" <|
                \_ ->
                    { baseModel
                        | currentStep = Types.ConfirmationStep
                        , title = "Design sync"
                        , name = "Taylor"
                        , email = "taylor@example.com"
                        , selectedSlot = Just sampleSlot
                    }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "Design sync", Sel.text "Taylor", Sel.text "taylor@example.com" ]
            , test "hides phone when phone is empty" <|
                \_ ->
                    { baseModel
                        | currentStep = Types.ConfirmationStep
                        , phone = ""
                        , selectedSlot = Just sampleSlot
                    }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.hasNot [ Sel.text "Phone" ]
            , test "shows phone when phone is provided" <|
                \_ ->
                    { baseModel
                        | currentStep = Types.ConfirmationStep
                        , phone = "555-1234"
                        , selectedSlot = Just sampleSlot
                    }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "555-1234" ]
            , test "shows loading label when booking in progress" <|
                \_ ->
                    { baseModel
                        | currentStep = Types.ConfirmationStep
                        , loading = True
                        , selectedSlot = Just sampleSlot
                    }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.find [ Sel.id "confirm-booking-btn" ]
                        |> Query.has [ Sel.text "Booking..." ]
            ]
        , describe "CompleteStep"
            [ test "shows success message" <|
                \_ ->
                    { baseModel | currentStep = Types.CompleteStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "You\u{2019}re booked." ]
            , test "shows booking ID when result is present" <|
                \_ ->
                    { baseModel
                        | currentStep = Types.CompleteStep
                        , bookingResult = Just { bookingId = "bk-42", confirmed = True }
                    }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "Booking ID: bk-42" ]
            , test "shows confirmation email message" <|
                \_ ->
                    { baseModel | currentStep = Types.CompleteStep }
                        |> View.view
                        |> Query.fromHtml
                        |> Query.has [ Sel.text "A confirmation email is on its way." ]
            ]
        ]
