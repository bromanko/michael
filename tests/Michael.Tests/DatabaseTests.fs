module Michael.Tests.DatabaseTests

open System
open Expecto
open NodaTime
open NodaTime.Text
open Microsoft.Data.Sqlite
open Michael.Domain
open Michael.Database
open Michael.Tests.TestHelpers

let private migrationsDir =
    System.IO.Path.Combine(System.AppContext.BaseDirectory, "migrations")

/// Fixed timestamp used across all DB fixtures. A live SystemClock call
/// produces nanosecond precision; SQLite stores seconds (Unix epoch integers).
/// The mismatch can silently hide readBooking parsing bugs because the
/// round-tripped value differs from the original without any assertion failing.
/// Using a fixed second-aligned instant makes CreatedAt equality assertions
/// reliable and keeps tests deterministic.
let private fixedCreatedAt = Instant.FromUtc(2026, 1, 1, 10, 0, 0)

let private withMemoryDb f =
    use conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()

    match initializeDatabase conn migrationsDir NodaTime.SystemClock.Instance with
    | Error msg -> failtestf "initializeDatabase failed: %s" msg
    | Ok() -> ()

    f conn

[<Tests>]
let databaseTests =
    testList
        "Database"
        [ test "initializeDatabase creates tables" {
              withMemoryDb (fun conn ->
                  use cmd = conn.CreateCommand()
                  cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
                  use reader = cmd.ExecuteReader()

                  let tables =
                      [ while reader.Read() do
                            reader.GetString(0) ]

                  Expect.contains tables "bookings" "bookings table exists"
                  Expect.contains tables "host_availability" "host_availability table exists")
          }

          test "seedHostAvailability creates 5 weekday slots" {
              withMemoryDb (fun conn ->
                  let slots = getHostAvailability conn
                  Expect.hasLength slots 5 "Mon-Fri = 5 slots")
          }

          test "getHostAvailability returns correct data" {
              withMemoryDb (fun conn ->
                  let slots = getHostAvailability conn
                  let monday = slots |> List.find (fun s -> s.DayOfWeek = IsoDayOfWeek.Monday)
                  Expect.equal monday.StartTime (LocalTime(9, 0)) "start 9:00"
                  Expect.equal monday.EndTime (LocalTime(17, 0)) "end 17:00")
          }

          test "insertBooking and getBookingsInRange round-trip" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let startOdt = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                  let endOdt = pattern.Parse("2026-02-03T10:30:00-05:00").Value

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Alice"
                        ParticipantEmail = "alice@example.com"
                        ParticipantPhone = Some "555-1234"
                        Title = "Test meeting"
                        Description = Some "A test"
                        StartTime = startOdt
                        EndTime = endOdt
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some fixedCancellationToken
                        CalDavEventHref = None }

                  let insertResult = insertBooking conn booking
                  Expect.isOk insertResult "insert should succeed"

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 1 "should find one booking"
                  Expect.equal bookings.[0].ParticipantName "Alice" "name matches"
                  Expect.equal bookings.[0].Title "Test meeting" "title matches"
                  Expect.equal bookings.[0].CreatedAt booking.CreatedAt "CreatedAt round-trips")
          }

          test "getBookingsInRange excludes out-of-range bookings" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Bob"
                        ParticipantEmail = "bob@example.com"
                        ParticipantPhone = None
                        Title = "Other meeting"
                        Description = None
                        StartTime = pattern.Parse("2026-02-05T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-05T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some fixedCancellationToken
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 22, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 0 "should find no bookings in range")
          }

          test "insertBookingIfSlotAvailable rejects overlapping confirmed booking" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso

                  let firstBooking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Alice"
                        ParticipantEmail = "alice@example.com"
                        ParticipantPhone = None
                        Title = "First"
                        Description = None
                        StartTime = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-03T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        // Each booking in the same DB needs a distinct token:
                        // the DB has a UNIQUE INDEX on cancellation_token.
                        CancellationToken = Some(makeFakeCancellationToken ())
                        CalDavEventHref = None }

                  let overlappingBooking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Bob"
                        ParticipantEmail = "bob@example.com"
                        ParticipantPhone = None
                        Title = "Overlap"
                        Description = None
                        StartTime = pattern.Parse("2026-02-03T10:15:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-03T10:45:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some(makeFakeCancellationToken ())
                        CalDavEventHref = None }

                  let first = insertBookingIfSlotAvailable conn firstBooking
                  Expect.equal first (Ok true) "first booking should be inserted"

                  let second = insertBookingIfSlotAvailable conn overlappingBooking
                  Expect.equal second (Ok false) "overlapping booking should be rejected"

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 1 "only first booking remains")
          }

          test "insertBookingIfSlotAvailable allows adjacent booking" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso

                  let firstBooking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Alice"
                        ParticipantEmail = "alice@example.com"
                        ParticipantPhone = None
                        Title = "First"
                        Description = None
                        StartTime = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-03T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        // Each booking in the same DB needs a distinct token:
                        // the DB has a UNIQUE INDEX on cancellation_token.
                        CancellationToken = Some(makeFakeCancellationToken ())
                        CalDavEventHref = None }

                  let adjacentBooking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Bob"
                        ParticipantEmail = "bob@example.com"
                        ParticipantPhone = None
                        Title = "Adjacent"
                        Description = None
                        StartTime = pattern.Parse("2026-02-03T10:30:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-03T11:00:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some(makeFakeCancellationToken ())
                        CalDavEventHref = None }

                  let first = insertBookingIfSlotAvailable conn firstBooking
                  let second = insertBookingIfSlotAvailable conn adjacentBooking

                  Expect.equal first (Ok true) "first booking should be inserted"
                  Expect.equal second (Ok true) "adjacent booking should be inserted"

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 17, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 2 "both non-overlapping bookings should exist")
          }

          test "duplicate booking ID returns Error" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let sharedId = Guid.NewGuid()

                  let booking =
                      { Id = sharedId
                        ParticipantName = "Alice"
                        ParticipantEmail = "alice@example.com"
                        ParticipantPhone = None
                        Title = "First"
                        Description = None
                        StartTime = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-03T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some fixedCancellationToken
                        CalDavEventHref = None }

                  let first = insertBooking conn booking
                  Expect.isOk first "first insert should succeed"

                  let duplicate = insertBooking conn { booking with Title = "Second" }
                  Expect.isError duplicate "duplicate ID should fail")
          }

          test "booking with None optional fields round-trips correctly" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let startOdt = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                  let endOdt = pattern.Parse("2026-02-03T10:30:00-05:00").Value

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Bob"
                        ParticipantEmail = "bob@example.com"
                        ParticipantPhone = None
                        Title = "No extras"
                        Description = None
                        StartTime = startOdt
                        EndTime = endOdt
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some fixedCancellationToken
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 1 "should find one booking"
                  Expect.isNone bookings.[0].ParticipantPhone "phone should be None"
                  Expect.isNone bookings.[0].Description "description should be None"
                  Expect.equal bookings.[0].CreatedAt booking.CreatedAt "CreatedAt round-trips")
          }

          test "booking with no cancellation token round-trips through DB" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let startOdt = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                  let endOdt = pattern.Parse("2026-02-03T10:30:00-05:00").Value

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Dave"
                        ParticipantEmail = "dave@example.com"
                        ParticipantPhone = None
                        Title = "No token"
                        Description = None
                        StartTime = startOdt
                        EndTime = endOdt
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = None
                        CalDavEventHref = None }

                  let result = insertBooking conn booking
                  Expect.isOk result "insert succeeds"

                  let loaded = getBookingById conn booking.Id
                  Expect.isSome loaded "booking found"
                  Expect.isNone loaded.Value.CancellationToken "token is None"
                  Expect.equal loaded.Value.CreatedAt booking.CreatedAt "CreatedAt round-trips")
          }

          test "cancellation token round-trips through insert and read" {
              // Token is the production format: 64-char uppercase hex (32 random bytes).
              // Format validation is intentionally application-level only; the DB column
              // is plain TEXT with no CHECK constraint. The companion test below
              // ('cancellation_token column has no format constraint') confirms that.
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let token = "AABBCCDD11223344AABBCCDD11223344AABBCCDD11223344AABBCCDD11223344"

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Token Test"
                        ParticipantEmail = "token@example.com"
                        ParticipantPhone = None
                        Title = "Token round-trip"
                        Description = None
                        StartTime = pattern.Parse("2026-02-10T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-10T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some token
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore

                  let retrieved = getBookingById conn booking.Id
                  Expect.isSome retrieved "booking found"
                  Expect.equal retrieved.Value.CancellationToken (Some token) "cancellation token round-trips"
                  Expect.equal retrieved.Value.CreatedAt booking.CreatedAt "CreatedAt round-trips")
          }

          test "cancellation_token column has no format constraint" {
              // The DB column is plain TEXT. Format enforcement (64-char hex) is
              // application-level only, enforced by sanitisation before insertion.
              // A short, non-hex string must round-trip unchanged to confirm no
              // CHECK constraint, trigger, or silent coercion silently alters the value.
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let arbitraryToken = "short"

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Format Test"
                        ParticipantEmail = "format@example.com"
                        ParticipantPhone = None
                        Title = "No-constraint token"
                        Description = None
                        StartTime = pattern.Parse("2026-02-10T11:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-10T11:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some arbitraryToken
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore

                  let retrieved = getBookingById conn booking.Id
                  Expect.isSome retrieved "booking found"

                  Expect.equal
                      retrieved.Value.CancellationToken
                      (Some arbitraryToken)
                      "arbitrary token string round-trips unchanged")
          }

          test "getBookingsInRange excludes cancelled bookings" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso
                  let startOdt = pattern.Parse("2026-02-03T10:00:00-05:00").Value
                  let endOdt = pattern.Parse("2026-02-03T10:30:00-05:00").Value

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Carol"
                        ParticipantEmail = "carol@example.com"
                        ParticipantPhone = None
                        Title = "Cancelled meeting"
                        Description = None
                        StartTime = startOdt
                        EndTime = endOdt
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Cancelled
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some fixedCancellationToken
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 0 "cancelled bookings should be excluded")
          }

          test "insert booking with CalDavEventHref = None, read back, verify None" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Eve"
                        ParticipantEmail = "eve@example.com"
                        ParticipantPhone = None
                        Title = "Test"
                        Description = None
                        StartTime = pattern.Parse("2026-02-10T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-10T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some(makeFakeCancellationToken ())
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore
                  let loaded = getBookingById conn booking.Id
                  Expect.isSome loaded "booking found"
                  Expect.isNone loaded.Value.CalDavEventHref "CalDavEventHref should be None")
          }

          test "updateBookingCalDavEventHref sets the column" {
              withMemoryDb (fun conn ->
                  let pattern = OffsetDateTimePattern.ExtendedIso

                  let booking =
                      { Id = Guid.NewGuid()
                        ParticipantName = "Frank"
                        ParticipantEmail = "frank@example.com"
                        ParticipantPhone = None
                        Title = "Test"
                        Description = None
                        StartTime = pattern.Parse("2026-02-11T10:00:00-05:00").Value
                        EndTime = pattern.Parse("2026-02-11T10:30:00-05:00").Value
                        DurationMinutes = 30
                        Timezone = "America/New_York"
                        Status = Confirmed
                        CreatedAt = fixedCreatedAt
                        CancellationToken = Some(makeFakeCancellationToken ())
                        CalDavEventHref = None }

                  insertBooking conn booking |> ignore

                  let href = "https://caldav.example.com/dav/calendars/user/test/Default/abc.ics"
                  let result = updateBookingCalDavEventHref conn booking.Id href
                  Expect.isOk result "update should succeed"

                  let loaded = getBookingById conn booking.Id
                  Expect.isSome loaded "booking found"
                  Expect.equal loaded.Value.CalDavEventHref (Some href) "CalDavEventHref should match")
          }

          test "initializeDatabase is idempotent" {
              withMemoryDb (fun conn ->
                  // Call initializeDatabase again — should not fail or duplicate data
                  match initializeDatabase conn migrationsDir NodaTime.SystemClock.Instance with
                  | Error msg -> failtestf "second initializeDatabase failed: %s" msg
                  | Ok() -> ()

                  let slots = getHostAvailability conn
                  Expect.hasLength slots 5 "still 5 slots after second init"

                  let revCount =
                      Donald.Db.newCommand "SELECT COUNT(*) FROM atlas_schema_revisions" conn
                      |> Donald.Db.scalar (fun o -> System.Convert.ToInt64(o))

                  Expect.equal revCount 4L "migrations applied exactly once each")
          }

          test "getSchedulingSettings returns defaults when no settings exist" {
              withMemoryDb (fun conn ->
                  let settings = getSchedulingSettings conn
                  Expect.equal settings.MinNoticeHours 6 "default minNoticeHours"
                  Expect.equal settings.BookingWindowDays 30 "default bookingWindowDays"
                  Expect.equal settings.DefaultDurationMinutes 30 "default defaultDurationMinutes"
                  Expect.isNone settings.VideoLink "default videoLink is None")
          }

          test "updateSchedulingSettings and getSchedulingSettings round-trip" {
              withMemoryDb (fun conn ->
                  let settings: SchedulingSettings =
                      { MinNoticeHours = 12
                        BookingWindowDays = 60
                        DefaultDurationMinutes = 45
                        VideoLink = Some "https://zoom.us/j/123456" }

                  let result = updateSchedulingSettings conn settings
                  Expect.isOk result "update should succeed"

                  let retrieved = getSchedulingSettings conn
                  Expect.equal retrieved.MinNoticeHours 12 "minNoticeHours persisted"
                  Expect.equal retrieved.BookingWindowDays 60 "bookingWindowDays persisted"
                  Expect.equal retrieved.DefaultDurationMinutes 45 "defaultDurationMinutes persisted"
                  Expect.equal retrieved.VideoLink (Some "https://zoom.us/j/123456") "videoLink persisted")
          }

          test "updateSchedulingSettings with None videoLink clears it" {
              withMemoryDb (fun conn ->
                  // First set a video link
                  let withLink: SchedulingSettings =
                      { MinNoticeHours = 6
                        BookingWindowDays = 30
                        DefaultDurationMinutes = 30
                        VideoLink = Some "https://meet.google.com/abc" }

                  updateSchedulingSettings conn withLink |> ignore

                  // Then clear it
                  let withoutLink = { withLink with VideoLink = None }
                  updateSchedulingSettings conn withoutLink |> ignore

                  let retrieved = getSchedulingSettings conn
                  Expect.isNone retrieved.VideoLink "videoLink should be cleared")
          }

          test "updateSchedulingSettings is idempotent" {
              withMemoryDb (fun conn ->
                  let settings: SchedulingSettings =
                      { MinNoticeHours = 24
                        BookingWindowDays = 14
                        DefaultDurationMinutes = 60
                        VideoLink = None }

                  updateSchedulingSettings conn settings |> ignore
                  updateSchedulingSettings conn settings |> ignore

                  let retrieved = getSchedulingSettings conn
                  Expect.equal retrieved.MinNoticeHours 24 "value unchanged after second update")
          } ]
