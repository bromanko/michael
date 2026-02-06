module Michael.Tests.EmailTests

open System
open Expecto
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.Email

let private makeBooking () =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.NewGuid()
      ParticipantName = "Alice Smith"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = Some "555-1234"
      Title = "Project Review"
      Description = Some "Quarterly review meeting"
      StartTime = pattern.Parse("2026-02-15T14:00:00-05:00").Value
      EndTime = pattern.Parse("2026-02-15T15:00:00-05:00").Value
      DurationMinutes = 60
      Timezone = "America/New_York"
      Status = Confirmed
      CreatedAt = SystemClock.Instance.GetCurrentInstant() }

[<Tests>]
let emailTests =
    testList "Email" [
        testList "formatBookingDate" [
            test "formats date as YYYY-MM-DD" {
                let pattern = OffsetDateTimePattern.ExtendedIso
                let odt = pattern.Parse("2026-02-15T14:00:00-05:00").Value
                let result = formatBookingDate odt
                Expect.equal result "2026-02-15" "date formatted correctly"
            }

            test "pads single-digit month and day" {
                let pattern = OffsetDateTimePattern.ExtendedIso
                let odt = pattern.Parse("2026-01-05T09:00:00+00:00").Value
                let result = formatBookingDate odt
                Expect.equal result "2026-01-05" "single digits padded"
            }
        ]

        testList "formatBookingTime" [
            test "formats time as HH:MM" {
                let pattern = OffsetDateTimePattern.ExtendedIso
                let odt = pattern.Parse("2026-02-15T14:30:00-05:00").Value
                let result = formatBookingTime odt
                Expect.equal result "14:30" "time formatted correctly"
            }

            test "pads single-digit hour" {
                let pattern = OffsetDateTimePattern.ExtendedIso
                let odt = pattern.Parse("2026-02-15T09:05:00-05:00").Value
                let result = formatBookingTime odt
                Expect.equal result "09:05" "single digit hour padded"
            }

            test "handles midnight" {
                let pattern = OffsetDateTimePattern.ExtendedIso
                let odt = pattern.Parse("2026-02-15T00:00:00+00:00").Value
                let result = formatBookingTime odt
                Expect.equal result "00:00" "midnight formatted correctly"
            }
        ]

        testList "buildCancellationEmailContent" [
            test "includes booking title in subject" {
                let booking = makeBooking ()
                let content = buildCancellationEmailContent booking true None
                Expect.stringContains content.Subject "Project Review" "subject contains title"
                Expect.stringContains content.Subject "Cancelled" "subject indicates cancellation"
            }

            test "body includes all booking details" {
                let booking = makeBooking ()
                let content = buildCancellationEmailContent booking true None
                Expect.stringContains content.Body "Project Review" "body contains title"
                Expect.stringContains content.Body "2026-02-15" "body contains date"
                Expect.stringContains content.Body "14:00" "body contains start time"
                Expect.stringContains content.Body "15:00" "body contains end time"
                Expect.stringContains content.Body "America/New_York" "body contains timezone"
                Expect.stringContains content.Body "60 minutes" "body contains duration"
            }

            test "host cancellation shows correct message" {
                let booking = makeBooking ()
                let content = buildCancellationEmailContent booking true None
                Expect.stringContains content.Body "The host has cancelled" "host cancellation message"
            }

            test "non-host cancellation shows correct message" {
                let booking = makeBooking ()
                let content = buildCancellationEmailContent booking false None
                Expect.stringContains content.Body "This meeting has been cancelled" "generic cancellation message"
            }

            test "includes video link when provided" {
                let booking = makeBooking ()
                let content = buildCancellationEmailContent booking true (Some "https://zoom.us/j/123456")
                Expect.stringContains content.Body "https://zoom.us/j/123456" "body contains video link"
            }

            test "omits video link when None" {
                let booking = makeBooking ()
                let content = buildCancellationEmailContent booking true None
                Expect.isFalse (content.Body.Contains("Video link:")) "no video link line"
            }
        ]

        testList "buildConfirmationEmailContent" [
            test "includes booking title in subject" {
                let booking = makeBooking ()
                let content = buildConfirmationEmailContent booking None
                Expect.stringContains content.Subject "Project Review" "subject contains title"
                Expect.stringContains content.Subject "Confirmed" "subject indicates confirmation"
            }

            test "body includes all booking details" {
                let booking = makeBooking ()
                let content = buildConfirmationEmailContent booking None
                Expect.stringContains content.Body "Project Review" "body contains title"
                Expect.stringContains content.Body "2026-02-15" "body contains date"
                Expect.stringContains content.Body "14:00" "body contains start time"
                Expect.stringContains content.Body "15:00" "body contains end time"
                Expect.stringContains content.Body "America/New_York" "body contains timezone"
                Expect.stringContains content.Body "60 minutes" "body contains duration"
            }

            test "includes description when present" {
                let booking = makeBooking ()
                let content = buildConfirmationEmailContent booking None
                Expect.stringContains content.Body "Quarterly review meeting" "body contains description"
            }

            test "omits description line when None" {
                let booking = { makeBooking () with Description = None }
                let content = buildConfirmationEmailContent booking None
                Expect.isFalse (content.Body.Contains("Description:")) "no description line"
            }

            test "includes video link when provided" {
                let booking = makeBooking ()
                let content = buildConfirmationEmailContent booking (Some "https://meet.google.com/abc-def")
                Expect.stringContains content.Body "https://meet.google.com/abc-def" "body contains video link"
            }

            test "omits video link when None" {
                let booking = makeBooking ()
                let content = buildConfirmationEmailContent booking None
                Expect.isFalse (content.Body.Contains("Video link:")) "no video link line"
            }

            test "omits video link when whitespace" {
                let booking = makeBooking ()
                let content = buildConfirmationEmailContent booking (Some "  ")
                Expect.isFalse (content.Body.Contains("Video link:")) "no video link for whitespace"
            }
        ]
    ]
