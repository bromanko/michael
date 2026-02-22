module Michael.Tests.HandlerTests

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Http
open NodaTime
open NodaTime.Testing
open NodaTime.Text
open Michael.Domain
open Michael.Email
open Michael.Handlers
open Michael.Tests.TestHelpers

[<Tests>]
let cancellationTokenFormatTests =
    testList
        "cancellation token format"
        [ test "token is 64 characters long" {
              let token = makeFakeCancellationToken ()
              Expect.equal token.Length 64 "token should be 64 hex chars (32 bytes)"
          }

          test "token contains only uppercase hex characters" {
              let token = makeFakeCancellationToken ()

              let isHex =
                  token |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))

              Expect.isTrue isHex "token should be uppercase hex"
          }

          test "tokens are unique across calls" {
              let t1 = makeFakeCancellationToken ()
              let t2 = makeFakeCancellationToken ()
              Expect.notEqual t1 t2 "consecutive tokens should differ"
          } ]

[<Tests>]
let isValidEmailTests =
    testList
        "isValidEmail"
        [ test "accepts valid email" { Expect.isTrue (isValidEmail "alice@example.com") "standard email" }

          test "accepts email with subdomain" { Expect.isTrue (isValidEmail "user@mail.example.com") "subdomain email" }

          test "rejects empty string" { Expect.isFalse (isValidEmail "") "empty string" }

          test "rejects whitespace" { Expect.isFalse (isValidEmail "   ") "whitespace only" }

          test "rejects null" { Expect.isFalse (isValidEmail null) "null" }

          test "rejects missing @" { Expect.isFalse (isValidEmail "aliceexample.com") "no @ sign" }

          test "rejects missing domain" { Expect.isFalse (isValidEmail "alice@") "no domain" }

          test "rejects missing local part" { Expect.isFalse (isValidEmail "@example.com") "no local part" }

          test "rejects domain without dot" { Expect.isFalse (isValidEmail "alice@localhost") "no dot in domain" }

          test "rejects domain ending with dot" {
              Expect.isFalse (isValidEmail "alice@example.") "domain ends with dot"
          }

          test "rejects multiple @ signs" { Expect.isFalse (isValidEmail "alice@bob@example.com") "multiple @" } ]

[<Tests>]
let tryParseOdtTests =
    testList
        "tryParseOdt"
        [ test "parses valid ISO-8601 with offset" {
              let result = tryParseOdt "start" "2026-02-15T14:00:00-05:00"
              Expect.isOk result "should parse successfully"
          }

          test "parses UTC offset" {
              let result = tryParseOdt "start" "2026-02-15T14:00:00Z"
              Expect.isOk result "should parse UTC"
          }

          test "returns error for invalid format" {
              let result = tryParseOdt "start" "not-a-date"
              Expect.isError result "should fail on invalid input"
          }

          test "returns error for date without time" {
              let result = tryParseOdt "start" "2026-02-15"
              Expect.isError result "should fail on date-only"
          }

          test "returns error for empty string" {
              let result = tryParseOdt "start" ""
              Expect.isError result "should fail on empty"
          }

          test "error message includes field name" {
              let result = tryParseOdt "Slot.Start" "bad"

              match result with
              | Error msg -> Expect.stringContains msg "Slot.Start" "error mentions field name"
              | Ok _ -> failtest "expected error"
          }

          test "error message includes invalid value" {
              let result = tryParseOdt "Slot.Start" "not-a-date"

              match result with
              | Error msg -> Expect.stringContains msg "not-a-date" "error mentions invalid value"
              | Ok _ -> failtest "expected error"
          }

          test "error message includes both field name and value" {
              let result = tryParseOdt "AvailabilityWindows[0].End" "12:00"

              match result with
              | Error msg ->
                  Expect.stringContains msg "AvailabilityWindows[0].End" "error mentions field"
                  Expect.stringContains msg "12:00" "error mentions value"
              | Ok _ -> failtest "expected error"
          }

          test "parses shortened offset -08" {
              let result = tryParseOdt "start" "2026-02-20T13:00:00-08"
              Expect.isOk result "should accept shortened offset"

              let odt = Result.defaultValue Unchecked.defaultof<_> result
              Expect.equal odt.Offset (Offset.FromHours(-8)) "offset is -08:00"
          }

          test "parses half-hour offset +05:30" {
              let result = tryParseOdt "start" "2026-03-10T08:00:00+05:30"
              Expect.isOk result "should accept half-hour offset"

              let odt = Result.defaultValue Unchecked.defaultof<_> result
              Expect.equal odt.Offset (Offset.FromHoursAndMinutes(5, 30)) "offset is +05:30"
          }

          test "parsed value has correct date and time" {
              let result = tryParseOdt "start" "2026-07-15T10:30:00+09:00"
              Expect.isOk result "should parse"

              let odt = Result.defaultValue Unchecked.defaultof<_> result
              Expect.equal odt.Year 2026 "year"
              Expect.equal odt.Month 7 "month"
              Expect.equal odt.Day 15 "day"
              Expect.equal odt.Hour 10 "hour"
              Expect.equal odt.Minute 30 "minute"
              Expect.equal odt.Second 0 "second"
          }

          test "returns error for null value" {
              let result = tryParseOdt "start" null
              Expect.isError result "should fail on null"
          }

          test "returns error for datetime without offset" {
              let result = tryParseOdt "start" "2026-02-15T14:00:00"
              Expect.isError result "should fail on datetime without offset"
          }

          test "error truncates very long invalid value" {
              let longValue = String.replicate 200 "x"
              let result = tryParseOdt "start" longValue

              match result with
              | Error msg ->
                  // Should contain first 80 chars + ellipsis, not the full 200
                  Expect.stringContains msg (String.replicate 80 "x") "contains truncated prefix"
                  Expect.stringContains msg "…" "contains ellipsis"
                  Expect.isFalse (msg.Contains(String.replicate 81 "x")) "does not contain chars beyond limit"
              | Ok _ -> failtest "expected error"
          } ]

[<Tests>]
let tryResolveTimezoneTests =
    testList
        "tryResolveTimezone"
        [ test "resolves valid IANA timezone" {
              let result = tryResolveTimezone "America/New_York"
              Expect.isOk result "should resolve"
          }

          test "resolves UTC" {
              let result = tryResolveTimezone "UTC"
              Expect.isOk result "should resolve UTC"
          }

          test "returns error for invalid timezone" {
              let result = tryResolveTimezone "Fake/Timezone"
              Expect.isError result "should fail on invalid tz"
          }

          test "returns error for empty string" {
              let result = tryResolveTimezone ""
              Expect.isError result "should fail on empty"
          }

          test "error message includes timezone id" {
              let result = tryResolveTimezone "Bad/Zone"

              match result with
              | Error msg -> Expect.stringContains msg "Bad/Zone" "error mentions tz id"
              | Ok _ -> failtest "expected error"
          }

          test "error truncates very long timezone id" {
              let longTz = String.replicate 200 "A"
              let result = tryResolveTimezone longTz

              match result with
              | Error msg ->
                  Expect.stringContains msg (String.replicate 80 "A") "contains truncated prefix"
                  Expect.stringContains msg "…" "contains ellipsis"
                  Expect.isFalse (msg.Contains(String.replicate 81 "A")) "does not contain chars beyond limit"
              | Ok _ -> failtest "expected error"
          } ]

[<Tests>]
let isValidDurationMinutesTests =
    testList
        "isValidDurationMinutes"
        [ test "4 is invalid (below lower bound)" { Expect.isFalse (isValidDurationMinutes 4) "4 minutes too short" }

          test "5 is valid (lower bound)" { Expect.isTrue (isValidDurationMinutes 5) "5 minutes is minimum" }

          test "6 is valid (just above lower bound)" { Expect.isTrue (isValidDurationMinutes 6) "6 minutes is valid" }

          test "30 is valid (typical)" { Expect.isTrue (isValidDurationMinutes 30) "30 minutes" }

          test "60 is valid (typical)" { Expect.isTrue (isValidDurationMinutes 60) "60 minutes" }

          test "479 is valid (just below upper bound)" {
              Expect.isTrue (isValidDurationMinutes 479) "479 minutes is valid"
          }

          test "480 is valid (upper bound)" { Expect.isTrue (isValidDurationMinutes 480) "480 minutes is maximum" }

          test "481 is invalid (above upper bound)" {
              Expect.isFalse (isValidDurationMinutes 481) "481 minutes too long"
          }

          test "0 is invalid" { Expect.isFalse (isValidDurationMinutes 0) "zero" }

          test "-1 is invalid" { Expect.isFalse (isValidDurationMinutes -1) "negative" }

          test "Int32.MinValue is invalid" { Expect.isFalse (isValidDurationMinutes System.Int32.MinValue) "min int" }

          test "Int32.MaxValue is invalid" { Expect.isFalse (isValidDurationMinutes System.Int32.MaxValue) "max int" } ]

// ---------------------------------------------------------------------------
// sendConfirmationNotification
// ---------------------------------------------------------------------------



let private makeTestBooking () : Booking =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
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
      CreatedAt = Instant.FromUtc(2026, 2, 14, 12, 0, 0)
      CancellationToken = Some "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890"
      CalDavEventHref = None }

[<Tests>]
let sendConfirmationNotificationTests =
    testList
        "sendConfirmationNotification"
        [ test "completes without error when notificationConfig is None" {
              let booking = makeTestBooking ()

              let failSend (_: NotificationConfig) (_: Booking) (_: string option) =
                  Task.FromResult(Error "should not be called")

              (sendConfirmationNotification failSend None booking None).Wait()
          }

          test "does not call sendFn when notificationConfig is None" {
              let booking = makeTestBooking ()
              let mutable called = false

              let detectSend (_: NotificationConfig) (_: Booking) (_: string option) =
                  called <- true
                  Task.FromResult(Ok())

              (sendConfirmationNotification detectSend None booking None).Wait()
              Expect.isFalse called "sendFn should not be called when config is None"
          }

          test "completes without error when email send succeeds" {
              let booking = makeTestBooking ()

              let succeedSend (_: NotificationConfig) (_: Booking) (_: string option) = Task.FromResult(Ok())

              (sendConfirmationNotification succeedSend (Some testNotificationConfig) booking None).Wait()
          }

          test "completes without error when email send fails (error swallowed)" {
              let booking = makeTestBooking ()

              let failSend (_: NotificationConfig) (_: Booking) (_: string option) =
                  Task.FromResult(Error "SMTP connection timeout")

              (sendConfirmationNotification failSend (Some testNotificationConfig) booking None).Wait()
          }

          test "forwards config, booking, and videoLink to sendFn" {
              let booking = makeTestBooking ()
              let mutable capturedConfig = None
              let mutable capturedBookingId = None
              let mutable capturedVideoLink = None

              let captureSend (c: NotificationConfig) (b: Booking) (v: string option) =
                  capturedConfig <- Some c
                  capturedBookingId <- Some b.Id
                  capturedVideoLink <- Some v
                  Task.FromResult(Ok())

              let videoLink = Some "https://zoom.us/j/123"

              (sendConfirmationNotification captureSend (Some testNotificationConfig) booking videoLink).Wait()

              Expect.equal capturedConfig (Some testNotificationConfig) "config forwarded"
              Expect.equal capturedBookingId (Some booking.Id) "booking forwarded"
              Expect.equal capturedVideoLink (Some videoLink) "videoLink forwarded"
          }

          test "completes without error when sendFn throws exception (exception caught)" {
              let booking = makeTestBooking ()

              let throwSend (_: NotificationConfig) (_: Booking) (_: string option) : Task<Result<unit, string>> =
                  raise (InvalidOperationException("SMTP socket disposed"))

              (sendConfirmationNotification throwSend (Some testNotificationConfig) booking None).Wait()
          }

          test "completes without error when sendFn returns faulted task" {
              let booking = makeTestBooking ()

              let faultSend (_: NotificationConfig) (_: Booking) (_: string option) : Task<Result<unit, string>> =
                  Task.FromException<Result<unit, string>>(ObjectDisposedException("SmtpClient"))

              (sendConfirmationNotification faultSend (Some testNotificationConfig) booking None).Wait()
          } ]

// ---------------------------------------------------------------------------
// handleBook fire-and-forget contract
// ---------------------------------------------------------------------------

let private makeBookRequestJson (slotStart: string) (slotEnd: string) (durationMinutes: int) =
    // camelCase keys — matches JsonSerializerDefaults.Web naming policy
    // used by the production JsonSerializerOptions.
    $"""{{
  "name": "Alice Smith",
  "email": "alice@example.com",
  "phone": null,
  "title": "Test Meeting",
  "description": null,
  "slot": {{ "start": "{slotStart}", "end": "{slotEnd}" }},
  "durationMinutes": {durationMinutes},
  "timezone": "America/New_York"
}}"""

let private makeBookHttpContext (requestJson: string) =
    let bodyBytes = Encoding.UTF8.GetBytes(requestJson)
    let ctx = makeTestHttpContext ()
    ctx.Request.Body <- new MemoryStream(bodyBytes)
    ctx.Request.ContentType <- "application/json; charset=utf-8"
    ctx

[<Tests>]
let handleBookFireAndForgetTests =
    // "now" = Friday 2026-02-20 15:00 UTC (10:00 EST, within business hours)
    // slot  = Tuesday 2026-02-24 10:00–11:00 EST
    //
    // This satisfies every scheduling constraint:
    //   • Tuesday 10–11 EST falls within seeded Mon-Fri 09:00–17:00 host availability
    //   • 96 hours to slot start > MinNoticeHours (6)
    //   • 4 days to slot start < BookingWindowDays (30)
    let now = Instant.FromUtc(2026, 2, 20, 15, 0, 0)
    let slotStart = "2026-02-24T10:00:00-05:00"
    let slotEnd = "2026-02-24T11:00:00-05:00"
    let hostTz = DateTimeZoneProviders.Tzdb.["America/New_York"]

    testList
        "handleBook fire-and-forget"
        [ test "returns 200 Confirmed without waiting for email delivery" {
              // Arrange: email sendFn that would take 30 seconds to complete.
              // If handleBook awaits the email, the handler task will not
              // finish within the 5-second deadline below, failing the test.
              let slowSend (_: NotificationConfig) (_: Booking) (_: string option) : Task<Result<unit, string>> =
                  task {
                      do! Task.Delay(30_000)
                      return Ok()
                  }

              withSharedMemoryDb (fun createConn ->
                  let fakeClock = FakeClock(now)
                  let requestJson = makeBookRequestJson slotStart slotEnd 60
                  let ctx = makeBookHttpContext requestJson

                  let noopWriteBack (_: Booking) (_: string option) : Task<unit> = Task.FromResult()

                  let handler =
                      handleBook
                          createConn
                          hostTz
                          fakeClock
                          (Some testNotificationConfig)
                          (fun () -> None)
                          slowSend
                          noopWriteBack

                  // Act: start the handler but do NOT await it yet.
                  let handlerTask = handler ctx

                  // Assert: the task must complete before the 5-second deadline.
                  // slowSend takes 30s, so any regression that awaits the email
                  // inside the handler will cause this assertion to fail.
                  let completedInTime = handlerTask.Wait(TimeSpan.FromSeconds(5.0))

                  Expect.isTrue completedInTime "handleBook must not block on email delivery"
                  Expect.equal ctx.Response.StatusCode 200 "booking should be confirmed (HTTP 200)")
          }

          test "returns 200 Confirmed when sendFn returns a faulted task immediately" {
              // Regression guard: a fire-and-forget that always faults must
              // not propagate the exception to the HTTP response.
              let faultSend (_: NotificationConfig) (_: Booking) (_: string option) : Task<Result<unit, string>> =
                  Task.FromException<Result<unit, string>>(InvalidOperationException("SMTP down"))

              withSharedMemoryDb (fun createConn ->
                  let fakeClock = FakeClock(now)
                  let requestJson = makeBookRequestJson slotStart slotEnd 60
                  let ctx = makeBookHttpContext requestJson

                  let noopWriteBack (_: Booking) (_: string option) : Task<unit> = Task.FromResult()

                  let handler =
                      handleBook
                          createConn
                          hostTz
                          fakeClock
                          (Some testNotificationConfig)
                          (fun () -> None)
                          faultSend
                          noopWriteBack

                  let completedInTime = (handler ctx).Wait(TimeSpan.FromSeconds(5.0))

                  Expect.isTrue completedInTime "faulted email task must not block the response"
                  Expect.equal ctx.Response.StatusCode 200 "booking should still be confirmed")
          } ]
