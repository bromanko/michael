module Michael.Tests.AdminTests

open System
open System.Threading.Tasks
open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open NodaTime.Testing
open NodaTime.Text
open Michael.Domain
open Michael.Database
open Michael.AdminAuth
open Michael.AdminHandlers
open Michael.Email
open Michael.Tests.TestHelpers

let private migrationsDir =
    System.IO.Path.Combine(System.AppContext.BaseDirectory, "migrations")

let private withMemoryDb f =
    use conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()

    match initializeDatabase conn migrationsDir NodaTime.SystemClock.Instance with
    | Error msg -> failtestf "initializeDatabase failed: %s" msg
    | Ok() -> ()

    f conn

let private makeBooking (startOdt: string) (endOdt: string) (status: BookingStatus) =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.NewGuid()
      ParticipantName = "Alice"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = None
      Title = "Test meeting"
      Description = None
      StartTime = pattern.Parse(startOdt).Value
      EndTime = pattern.Parse(endOdt).Value
      DurationMinutes = 30
      Timezone = "America/New_York"
      Status = status
      CreatedAt = Instant.FromUtc(2026, 1, 1, 0, 0)
      // makeFakeCancellationToken is deliberate: makeBooking is called
      // multiple times within the same test DB in several tests, and the
      // UNIQUE INDEX on cancellation_token requires each booking to carry
      // a distinct value. fixedCancellationToken cannot be used here.
      CancellationToken = Some(makeFakeCancellationToken ()) }

[<Tests>]
let passwordTests =
    testList
        "Password hashing"
        [ test "hashPasswordAtStartup produces correct length hash and salt" {
              let hashed = hashPasswordAtStartup "mysecret"
              Expect.equal hashed.Hash.Length 32 "hash is 32 bytes"
              Expect.equal hashed.Salt.Length 16 "salt is 16 bytes"
          }

          test "same password with same salt produces same hash" {
              let salt = Array.zeroCreate<byte> 16
              let hash1 = hashPassword "mysecret" salt
              let hash2 = hashPassword "mysecret" salt
              Expect.equal hash1 hash2 "deterministic hashing"
          }

          test "different passwords produce different hashes" {
              let salt = Array.zeroCreate<byte> 16
              let hash1 = hashPassword "password1" salt
              let hash2 = hashPassword "password2" salt
              Expect.notEqual hash1 hash2 "different passwords differ"
          }

          test "different salts produce different hashes" {
              let salt1 = Array.zeroCreate<byte> 16
              let salt2 = Array.init 16 (fun _ -> 1uy)
              let hash1 = hashPassword "mysecret" salt1
              let hash2 = hashPassword "mysecret" salt2
              Expect.notEqual hash1 hash2 "different salts differ"
          }

          test "hashPasswordAtStartup produces unique salts" {
              let hashed1 = hashPasswordAtStartup "mysecret"
              let hashed2 = hashPasswordAtStartup "mysecret"
              Expect.notEqual hashed1.Salt hashed2.Salt "salts are unique"
          } ]

[<Tests>]
let adminSessionDbTests =
    testList
        "Admin session database"
        [ test "insertAdminSession and getAdminSession round-trip" {
              withMemoryDb (fun conn ->
                  let now = Instant.FromUtc(2026, 2, 3, 12, 0)

                  let session: AdminSession =
                      { Token = "test-token-123"
                        CreatedAt = now
                        ExpiresAt = now.Plus(Duration.FromDays(7)) }

                  let result = insertAdminSession conn session
                  Expect.isOk result "insert should succeed"

                  let retrieved = getAdminSession conn "test-token-123"
                  Expect.isSome retrieved "session should be found"
                  Expect.equal retrieved.Value.Token "test-token-123" "token matches"
                  Expect.isTrue (retrieved.Value.CreatedAt.Equals(now)) "createdAt matches")
          }

          test "getAdminSession returns None for unknown token" {
              withMemoryDb (fun conn ->
                  let retrieved = getAdminSession conn "nonexistent-token"
                  Expect.isNone retrieved "should not find unknown token")
          }

          test "deleteAdminSession removes session" {
              withMemoryDb (fun conn ->
                  let now = Instant.FromUtc(2026, 2, 3, 12, 0)

                  let session: AdminSession =
                      { Token = "delete-me"
                        CreatedAt = now
                        ExpiresAt = now.Plus(Duration.FromDays(7)) }

                  insertAdminSession conn session |> ignore

                  let result = deleteAdminSession conn "delete-me"
                  Expect.isOk result "delete should succeed"

                  let retrieved = getAdminSession conn "delete-me"
                  Expect.isNone retrieved "session should be gone")
          }

          test "deleteExpiredAdminSessions removes only expired sessions" {
              withMemoryDb (fun conn ->
                  let now = Instant.FromUtc(2026, 2, 3, 12, 0)

                  let expired: AdminSession =
                      { Token = "expired-token"
                        CreatedAt = now.Minus(Duration.FromDays(14))
                        ExpiresAt = now.Minus(Duration.FromDays(7)) }

                  let valid: AdminSession =
                      { Token = "valid-token"
                        CreatedAt = now
                        ExpiresAt = now.Plus(Duration.FromDays(7)) }

                  insertAdminSession conn expired |> ignore
                  insertAdminSession conn valid |> ignore

                  let result = deleteExpiredAdminSessions conn now
                  Expect.isOk result "delete should succeed"

                  Expect.isNone (getAdminSession conn "expired-token") "expired session removed"
                  Expect.isSome (getAdminSession conn "valid-token") "valid session preserved")
          } ]

[<Tests>]
let adminBookingDbTests =
    testList
        "Admin booking queries"
        [ test "getBookingById returns booking" {
              withMemoryDb (fun conn ->
                  let booking =
                      makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed

                  insertBooking conn booking |> ignore

                  let retrieved = getBookingById conn booking.Id
                  Expect.isSome retrieved "booking found"
                  Expect.equal retrieved.Value.Title "Test meeting" "title matches")
          }

          test "getBookingById returns None for unknown id" {
              withMemoryDb (fun conn ->
                  let retrieved = getBookingById conn (Guid.NewGuid())
                  Expect.isNone retrieved "should not find unknown id")
          }

          test "cancelBooking changes status to cancelled" {
              withMemoryDb (fun conn ->
                  let booking =
                      makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed

                  insertBooking conn booking |> ignore

                  let result = cancelBooking conn booking.Id
                  Expect.isOk result "cancel should succeed"

                  let retrieved = getBookingById conn booking.Id
                  Expect.isSome retrieved "booking still exists"
                  Expect.equal retrieved.Value.Status Cancelled "status is cancelled")
          }

          test "cancelBooking returns error for unknown id" {
              withMemoryDb (fun conn ->
                  let result = cancelBooking conn (Guid.NewGuid())
                  Expect.isError result "should fail for unknown id")
          }

          test "cancelBooking returns error for already cancelled booking" {
              withMemoryDb (fun conn ->
                  let booking =
                      makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed

                  insertBooking conn booking |> ignore

                  cancelBooking conn booking.Id |> ignore
                  let result = cancelBooking conn booking.Id
                  Expect.isError result "should fail for already cancelled")
          }

          test "listBookings returns paginated results" {
              withMemoryDb (fun conn ->
                  for i in 1..5 do
                      let booking =
                          { makeBooking
                                $"2026-02-{i + 1:D2}T10:00:00-05:00"
                                $"2026-02-{i + 1:D2}T10:30:00-05:00"
                                Confirmed with
                              Id = Guid.NewGuid()
                              Title = $"Meeting {i}" }

                      insertBooking conn booking |> ignore

                  let (bookings, totalCount) = listBookings conn 1 3 None
                  Expect.equal totalCount 5 "total count is 5"
                  Expect.hasLength bookings 3 "page size is 3"

                  let (page2, _) = listBookings conn 2 3 None
                  Expect.hasLength page2 2 "second page has remainder")
          }

          test "listBookings filters by status" {
              withMemoryDb (fun conn ->
                  let confirmed =
                      makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed

                  let cancelled =
                      { makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Cancelled with
                          Id = Guid.NewGuid() }

                  insertBooking conn confirmed |> ignore
                  insertBooking conn cancelled |> ignore

                  let (confirmedList, confirmedCount) = listBookings conn 1 20 (Some "confirmed")
                  Expect.equal confirmedCount 1 "one confirmed"
                  Expect.hasLength confirmedList 1 "one confirmed booking"

                  let (cancelledList, cancelledCount) = listBookings conn 1 20 (Some "cancelled")
                  Expect.equal cancelledCount 1 "one cancelled"
                  Expect.hasLength cancelledList 1 "one cancelled booking")
          }

          test "getUpcomingBookingsCount counts only future confirmed bookings" {
              withMemoryDb (fun conn ->
                  let now = Instant.FromUtc(2026, 2, 3, 15, 0)

                  // Future confirmed
                  let future =
                      makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Confirmed
                  // Past confirmed
                  let past =
                      { makeBooking "2026-02-02T10:00:00-05:00" "2026-02-02T10:30:00-05:00" Confirmed with
                          Id = Guid.NewGuid() }
                  // Future cancelled
                  let cancelled =
                      { makeBooking "2026-02-05T10:00:00-05:00" "2026-02-05T10:30:00-05:00" Cancelled with
                          Id = Guid.NewGuid() }

                  insertBooking conn future |> ignore
                  insertBooking conn past |> ignore
                  insertBooking conn cancelled |> ignore

                  let count = getUpcomingBookingsCount conn now
                  Expect.equal count 1 "only one future confirmed booking")
          }

          test "getNextBooking returns earliest future confirmed booking" {
              withMemoryDb (fun conn ->
                  let now = Instant.FromUtc(2026, 2, 3, 15, 0)

                  let later =
                      { makeBooking "2026-02-05T10:00:00-05:00" "2026-02-05T10:30:00-05:00" Confirmed with
                          Id = Guid.NewGuid()
                          Title = "Later" }

                  let sooner =
                      { makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Confirmed with
                          Id = Guid.NewGuid()
                          Title = "Sooner" }

                  insertBooking conn later |> ignore
                  insertBooking conn sooner |> ignore

                  let next = getNextBooking conn now
                  Expect.isSome next "should find next booking"
                  Expect.equal next.Value.Title "Sooner" "returns earliest")
          }

          test "getNextBooking returns None when no future bookings" {
              withMemoryDb (fun conn ->
                  let now = Instant.FromUtc(2026, 2, 10, 15, 0)

                  let past =
                      makeBooking "2026-02-02T10:00:00-05:00" "2026-02-02T10:30:00-05:00" Confirmed

                  insertBooking conn past |> ignore

                  let next = getNextBooking conn now
                  Expect.isNone next "no future bookings")
          }

          test "cancelled booking produces valid cancellation email content and ICS" {
              withMemoryDb (fun conn ->
                  let booking =
                      makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Confirmed

                  insertBooking conn booking |> ignore

                  let result = cancelBooking conn booking.Id
                  Expect.isOk result "cancel should succeed"

                  let cancelledAt = Instant.FromUtc(2026, 2, 4, 9, 0, 0)

                  // Verify email content builds correctly for host-initiated cancellation
                  let content = buildCancellationEmailContent booking "Brian" true
                  Expect.stringContains content.Subject "Cancelled" "subject indicates cancellation"
                  Expect.stringContains content.Body "I need to cancel" "host cancellation message"
                  Expect.stringContains content.Body "Test meeting" "body contains title"
                  Expect.isFalse (content.Body.Contains("Video link:")) "cancellation email has no video link"

                  // Verify ICS builds correctly with cancelledAt timestamp
                  let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                  Expect.stringContains ics "METHOD:CANCEL" "ICS has CANCEL method"
                  Expect.stringContains ics "CANCELLED" "ICS has CANCELLED status"
                  Expect.stringContains ics "alice@example.com" "ICS has participant email")
          } ]

// ---------------------------------------------------------------------------
// handleCancelBooking handler tests
// ---------------------------------------------------------------------------

let private cancelNotificationConfig: NotificationConfig =
    { Smtp =
        { Host = "mail.example.com"
          Port = 587
          Username = None
          Password = None
          TlsMode = StartTls
          FromAddress = "cal@example.com"
          FromName = "Michael" }
      HostEmail = "host@example.com"
      HostName = "Brian"
      PublicUrl = "https://cal.example.com" }

/// Create a DefaultHttpContext with the booking id set as a route value,
/// matching the "{id}" route parameter that handleCancelBooking reads.
let private makeCancelContext (bookingId: Guid) =
    let ctx = makeTestHttpContext ()
    ctx.Request.RouteValues.Add("id", bookingId.ToString())
    ctx

[<Tests>]
let handleCancelBookingTests =
    // Fixed "now" used by the FakeClock in all handler tests below.
    let cancelledNow = Instant.FromUtc(2026, 3, 1, 14, 0, 0)

    testList
        "handleCancelBooking"
        [ test "returns 200 when notificationConfig is None and sendFn is never called" {
              // Verifies: handler succeeds without email config; sendFn is not
              // invoked when notificationConfig = None.
              withSharedMemoryDb (fun createConn ->
                  let booking =
                      makeBooking "2026-03-05T10:00:00-05:00" "2026-03-05T10:30:00-05:00" Confirmed

                  use conn = createConn ()
                  insertBooking conn booking |> ignore

                  let mutable sendFnCalled = false

                  let dummySend
                      (_: NotificationConfig)
                      (_: Booking)
                      (_: bool)
                      (_: Instant)
                      : Task<Result<unit, string>> =
                      sendFnCalled <- true
                      Task.FromResult(Ok())

                  let fakeClock = FakeClock(cancelledNow)
                  let ctx = makeCancelContext booking.Id

                  let handler = handleCancelBooking createConn fakeClock None dummySend

                  (handler ctx).Wait()

                  Expect.equal ctx.Response.StatusCode 200 "should return 200 OK"
                  Expect.isFalse sendFnCalled "sendFn must not be called when notificationConfig is None")
          }

          test "forwards cancelledAt obtained from clock to sendFn" {
              // Verifies: the handler uses clock.GetCurrentInstant() for
              // cancelledAt and passes that exact instant to sendFn, not a
              // separately sampled DateTime.UtcNow.
              withSharedMemoryDb (fun createConn ->
                  let booking =
                      makeBooking "2026-03-05T10:00:00-05:00" "2026-03-05T10:30:00-05:00" Confirmed

                  use conn = createConn ()
                  insertBooking conn booking |> ignore

                  let mutable capturedCancelledAt: Instant option = None

                  let capturingSend
                      (_: NotificationConfig)
                      (_: Booking)
                      (_: bool)
                      (at: Instant)
                      : Task<Result<unit, string>> =
                      capturedCancelledAt <- Some at
                      Task.FromResult(Ok())

                  let fakeClock = FakeClock(cancelledNow)
                  let ctx = makeCancelContext booking.Id

                  let handler =
                      handleCancelBooking createConn fakeClock (Some cancelNotificationConfig) capturingSend

                  (handler ctx).Wait()

                  Expect.equal ctx.Response.StatusCode 200 "should return 200 OK"

                  Expect.equal
                      capturedCancelledAt
                      (Some cancelledNow)
                      "cancelledAt must equal clock.GetCurrentInstant()")
          }

          test "returns 200 and swallows email failure" {
              // Verifies: a failed email send is logged but does not cause the
              // handler to return an error — the booking is cancelled regardless.
              withSharedMemoryDb (fun createConn ->
                  let booking =
                      makeBooking "2026-03-05T10:00:00-05:00" "2026-03-05T10:30:00-05:00" Confirmed

                  use conn = createConn ()
                  insertBooking conn booking |> ignore

                  let failSend
                      (_: NotificationConfig)
                      (_: Booking)
                      (_: bool)
                      (_: Instant)
                      : Task<Result<unit, string>> =
                      Task.FromResult(Error "SMTP connection refused")

                  let fakeClock = FakeClock(cancelledNow)
                  let ctx = makeCancelContext booking.Id

                  let handler =
                      handleCancelBooking createConn fakeClock (Some cancelNotificationConfig) failSend

                  (handler ctx).Wait()

                  Expect.equal ctx.Response.StatusCode 200 "email failure must not affect the HTTP response")
          }

          test "returns 404 for unknown booking id" {
              // Verifies: the handler returns 404 when the booking does not
              // exist — cancelBooking returns Error which maps to 404.
              withSharedMemoryDb (fun createConn ->
                  let unknownId = Guid.NewGuid()

                  let dummySend _ _ _ _ : Task<Result<unit, string>> = Task.FromResult(Ok())

                  let fakeClock = FakeClock(cancelledNow)
                  let ctx = makeCancelContext unknownId

                  let handler = handleCancelBooking createConn fakeClock None dummySend

                  (handler ctx).Wait()

                  Expect.equal ctx.Response.StatusCode 404 "unknown booking should return 404")
          } ]
