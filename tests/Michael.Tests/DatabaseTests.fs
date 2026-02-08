module Michael.Tests.DatabaseTests

open System
open Expecto
open NodaTime
open NodaTime.Text
open Microsoft.Data.Sqlite
open Michael.Domain
open Michael.Database

let private migrationsDir =
    System.IO.Path.Combine(System.AppContext.BaseDirectory, "migrations")

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
                        CreatedAt = SystemClock.Instance.GetCurrentInstant() }

                  let insertResult = insertBooking conn booking
                  Expect.isOk insertResult "insert should succeed"

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 1 "should find one booking"
                  Expect.equal bookings.[0].ParticipantName "Alice" "name matches"
                  Expect.equal bookings.[0].Title "Test meeting" "title matches")
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
                        CreatedAt = SystemClock.Instance.GetCurrentInstant() }

                  insertBooking conn booking |> ignore

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 22, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 0 "should find no bookings in range")
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
                        CreatedAt = SystemClock.Instance.GetCurrentInstant() }

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
                        CreatedAt = SystemClock.Instance.GetCurrentInstant() }

                  insertBooking conn booking |> ignore

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 1 "should find one booking"
                  Expect.isNone bookings.[0].ParticipantPhone "phone should be None"
                  Expect.isNone bookings.[0].Description "description should be None")
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
                        CreatedAt = SystemClock.Instance.GetCurrentInstant() }

                  insertBooking conn booking |> ignore

                  let rangeStart = Instant.FromUtc(2026, 2, 3, 14, 0)
                  let rangeEnd = Instant.FromUtc(2026, 2, 3, 16, 0)
                  let bookings = getBookingsInRange conn rangeStart rangeEnd
                  Expect.hasLength bookings 0 "cancelled bookings should be excluded")
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

                  Expect.equal revCount 2L "migrations applied exactly once each")
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
