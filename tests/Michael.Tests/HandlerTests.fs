module Michael.Tests.HandlerTests

open System
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.Database
open Michael.Handlers

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
// confirmBookingWithEmail tests
// ---------------------------------------------------------------------------

let private migrationsDir =
    System.IO.Path.Combine(AppContext.BaseDirectory, "migrations")

let private withMemoryDb f =
    use conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()

    match initializeDatabase conn migrationsDir SystemClock.Instance with
    | Error msg -> failtestf "initializeDatabase failed: %s" msg
    | Ok() -> ()

    f conn

let private makeTestBooking () =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.NewGuid()
      ParticipantName = "Alice Smith"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = Some "555-1234"
      Title = "Test Meeting"
      Description = Some "A test meeting"
      StartTime = pattern.Parse("2026-02-15T14:00:00-05:00").Value
      EndTime = pattern.Parse("2026-02-15T15:00:00-05:00").Value
      DurationMinutes = 60
      Timezone = "America/New_York"
      Status = Confirmed
      CreatedAt = SystemClock.Instance.GetCurrentInstant() }

/// Insert a booking into the DB so cancelBooking can find it during rollback.
let private insertTestBooking (conn: SqliteConnection) (booking: Booking) =
    match insertBooking conn booking with
    | Ok() -> ()
    | Error msg -> failtestf "insertBooking failed: %s" msg

[<Tests>]
let confirmBookingWithEmailTests =
    testList
        "confirmBookingWithEmail"
        [ testAsync "SMTP not configured — booking confirmed without email" {
              withMemoryDb (fun conn ->
                  let booking = makeTestBooking ()
                  insertTestBooking conn booking

                  let result =
                      confirmBookingWithEmail conn None None booking
                      |> Async.AwaitTask
                      |> Async.RunSynchronously

                  match result with
                  | BookingConfirmed id -> Expect.equal id booking.Id "booking ID matches"
                  | other -> failtestf "expected BookingConfirmed, got %A" other

                  // Booking should remain confirmed in DB
                  let dbBooking = getBookingById conn booking.Id
                  Expect.isSome dbBooking "booking exists"
                  Expect.equal dbBooking.Value.Status Confirmed "booking still confirmed")
          }

          testAsync "email succeeds — booking confirmed" {
              withMemoryDb (fun conn ->
                  let booking = makeTestBooking ()
                  insertTestBooking conn booking

                  let sendOk: Booking -> string option -> Task<Result<unit, string>> =
                      fun _booking _videoLink -> Task.FromResult(Ok())

                  let result =
                      confirmBookingWithEmail conn (Some sendOk) (Some "https://meet.example.com/123") booking
                      |> Async.AwaitTask
                      |> Async.RunSynchronously

                  match result with
                  | BookingConfirmed id -> Expect.equal id booking.Id "booking ID matches"
                  | other -> failtestf "expected BookingConfirmed, got %A" other

                  let dbBooking = getBookingById conn booking.Id
                  Expect.isSome dbBooking "booking exists"
                  Expect.equal dbBooking.Value.Status Confirmed "booking still confirmed")
          }

          testAsync "email fails — booking cancelled and EmailFailed returned" {
              withMemoryDb (fun conn ->
                  let booking = makeTestBooking ()
                  insertTestBooking conn booking

                  let sendFail: Booking -> string option -> Task<Result<unit, string>> =
                      fun _booking _videoLink -> Task.FromResult(Error "SMTP connection refused")

                  let result =
                      confirmBookingWithEmail conn (Some sendFail) None booking
                      |> Async.AwaitTask
                      |> Async.RunSynchronously

                  match result with
                  | EmailFailed(id, err) ->
                      Expect.equal id booking.Id "booking ID matches"
                      Expect.stringContains err "SMTP connection refused" "error message preserved"
                  | other -> failtestf "expected EmailFailed, got %A" other

                  // Booking should be cancelled in DB
                  let dbBooking = getBookingById conn booking.Id
                  Expect.isSome dbBooking "booking exists"
                  Expect.equal dbBooking.Value.Status Cancelled "booking was cancelled")
          }

          testAsync "email sender receives booking and video link URL" {
              withMemoryDb (fun conn ->
                  let booking = makeTestBooking ()
                  insertTestBooking conn booking

                  let mutable capturedBooking: Booking option = None
                  let mutable capturedVideoLink: string option option = None

                  let sendCapture: Booking -> string option -> Task<Result<unit, string>> =
                      fun b vl ->
                          capturedBooking <- Some b
                          capturedVideoLink <- Some vl
                          Task.FromResult(Ok())

                  let videoLink = Some "https://meet.example.com/456"

                  confirmBookingWithEmail conn (Some sendCapture) videoLink booking
                  |> Async.AwaitTask
                  |> Async.RunSynchronously
                  |> ignore

                  Expect.equal capturedBooking (Some booking) "booking passed to email sender"
                  Expect.equal capturedVideoLink (Some videoLink) "video link passed to email sender")
          }

          testAsync "email sender receives None when no video link configured" {
              withMemoryDb (fun conn ->
                  let booking = makeTestBooking ()
                  insertTestBooking conn booking

                  let mutable capturedVideoLink: string option option = None

                  let sendCapture: Booking -> string option -> Task<Result<unit, string>> =
                      fun _b vl ->
                          capturedVideoLink <- Some vl
                          Task.FromResult(Ok())

                  confirmBookingWithEmail conn (Some sendCapture) None booking
                  |> Async.AwaitTask
                  |> Async.RunSynchronously
                  |> ignore

                  Expect.equal capturedVideoLink (Some None) "None video link passed to email sender")
          } ]
