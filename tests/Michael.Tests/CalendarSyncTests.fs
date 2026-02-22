module Michael.Tests.CalendarSyncTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open NodaTime
open NodaTime.Text
open Microsoft.Data.Sqlite
open Michael.Domain
open Michael.Database
open Michael.CalendarSync
open Michael.Tests.TestHelpers

let private instant y m d h min = Instant.FromUtc(y, m, d, h, min)

let private migrationsDir =
    System.IO.Path.Combine(System.AppContext.BaseDirectory, "migrations")

let private withMemoryDb f =
    use conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()

    match initializeDatabase conn migrationsDir NodaTime.SystemClock.Instance with
    | Error msg -> failtestf "initializeDatabase failed: %s" msg
    | Ok() -> ()

    f conn

let private ensureSource (conn: SqliteConnection) (sourceId: Guid) =
    let source: CalendarSource =
        { Id = sourceId
          Provider = Fastmail
          BaseUrl = "https://example.com"
          CalendarHomeUrl = None }

    upsertCalendarSource conn source

[<Tests>]
let calendarSourceDbTests =
    testList
        "Calendar source database"
        [ test "initializeDatabase creates calendar_sources and cached_events tables" {
              withMemoryDb (fun conn ->
                  use cmd = conn.CreateCommand()
                  cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
                  use reader = cmd.ExecuteReader()

                  let tables =
                      [ while reader.Read() do
                            reader.GetString(0) ]

                  Expect.contains tables "calendar_sources" "calendar_sources table exists"
                  Expect.contains tables "cached_events" "cached_events table exists")
          }

          test "upsertCalendarSource inserts and retrieves a source" {
              withMemoryDb (fun conn ->
                  let source: CalendarSource =
                      { Id = Guid.NewGuid()
                        Provider = Fastmail
                        BaseUrl = "https://caldav.fastmail.com/dav/calendars"
                        CalendarHomeUrl = Some "https://caldav.fastmail.com/dav/calendars/user/" }

                  upsertCalendarSource conn source

                  use cmd = conn.CreateCommand()
                  cmd.CommandText <- "SELECT id, provider, base_url, calendar_home_url FROM calendar_sources"
                  use reader = cmd.ExecuteReader()
                  Expect.isTrue (reader.Read()) "should have a row"
                  Expect.equal (Guid.Parse(reader.GetString(0))) source.Id "id matches"
                  Expect.equal (reader.GetString(1)) "fastmail" "provider matches"
                  Expect.equal (reader.GetString(2)) source.BaseUrl "base_url matches"

                  Expect.equal
                      (reader.GetString(3))
                      "https://caldav.fastmail.com/dav/calendars/user/"
                      "calendar_home_url matches"

                  Expect.isFalse (reader.Read()) "should have exactly one row")
          }

          test "upsertCalendarSource replaces existing source" {
              withMemoryDb (fun conn ->
                  let id = Guid.NewGuid()

                  let source1: CalendarSource =
                      { Id = id
                        Provider = Fastmail
                        BaseUrl = "https://old-url.com"
                        CalendarHomeUrl = None }

                  let source2: CalendarSource =
                      { Id = id
                        Provider = Fastmail
                        BaseUrl = "https://new-url.com"
                        CalendarHomeUrl = Some "https://new-url.com/home" }

                  upsertCalendarSource conn source1
                  upsertCalendarSource conn source2

                  use cmd = conn.CreateCommand()
                  cmd.CommandText <- "SELECT COUNT(*) FROM calendar_sources"
                  let count = Convert.ToInt64(cmd.ExecuteScalar())
                  Expect.equal count 1L "should still have one source"

                  use cmd2 = conn.CreateCommand()
                  cmd2.CommandText <- "SELECT base_url FROM calendar_sources"
                  let baseUrl = cmd2.ExecuteScalar() :?> string
                  Expect.equal baseUrl "https://new-url.com" "base_url updated")
          }

          test "updateSyncStatus updates last_synced_at and last_sync_result" {
              withMemoryDb (fun conn ->
                  let source: CalendarSource =
                      { Id = Guid.NewGuid()
                        Provider = ICloud
                        BaseUrl = "https://caldav.icloud.com/"
                        CalendarHomeUrl = None }

                  upsertCalendarSource conn source

                  let now = instant 2026 2 3 12 0
                  updateSyncStatus conn source.Id now "ok" |> ignore

                  use cmd = conn.CreateCommand()
                  cmd.CommandText <- "SELECT last_synced_at, last_sync_result FROM calendar_sources WHERE id = @id"
                  let param = cmd.CreateParameter()
                  param.ParameterName <- "@id"
                  param.Value <- source.Id.ToString()
                  cmd.Parameters.Add(param) |> ignore
                  use reader = cmd.ExecuteReader()
                  Expect.isTrue (reader.Read()) "should have a row"
                  let syncedAt = reader.GetString(0)
                  let syncResult = reader.GetString(1)
                  Expect.equal syncResult "ok" "sync result is ok"
                  Expect.isTrue (syncedAt.Contains("2026-02-03")) "synced_at contains expected date")
          }

          test "upsertCalendarSource preserves sync status on update" {
              withMemoryDb (fun conn ->
                  let id = Guid.NewGuid()

                  let source: CalendarSource =
                      { Id = id
                        Provider = Fastmail
                        BaseUrl = "https://old-url.com"
                        CalendarHomeUrl = None }

                  upsertCalendarSource conn source

                  // Set sync status
                  let now = instant 2026 2 3 12 0
                  updateSyncStatus conn id now "ok" |> ignore

                  // Upsert with updated base_url
                  let source2: CalendarSource =
                      { Id = id
                        Provider = Fastmail
                        BaseUrl = "https://new-url.com"
                        CalendarHomeUrl = None }

                  upsertCalendarSource conn source2

                  // Verify sync status is preserved
                  use cmd = conn.CreateCommand()

                  cmd.CommandText <-
                      "SELECT last_synced_at, last_sync_result, base_url FROM calendar_sources WHERE id = @id"

                  let param = cmd.CreateParameter()
                  param.ParameterName <- "@id"
                  param.Value <- id.ToString()
                  cmd.Parameters.Add(param) |> ignore
                  use reader = cmd.ExecuteReader()
                  Expect.isTrue (reader.Read()) "should have a row"
                  let syncedAt = reader.GetString(0)
                  let syncResult = reader.GetString(1)
                  let baseUrl = reader.GetString(2)
                  Expect.equal syncResult "ok" "sync result preserved"
                  Expect.isTrue (syncedAt.Contains("2026-02-03")) "synced_at preserved"
                  Expect.equal baseUrl "https://new-url.com" "base_url updated")
          } ]

[<Tests>]
let cachedEventsDbTests =
    testList
        "Cached events database"
        [ test "replaceEventsForSource inserts and getCachedEventsInRange queries" {
              withMemoryDb (fun conn ->
                  let sourceId = Guid.NewGuid()
                  ensureSource conn sourceId

                  let events =
                      [ { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "event-1@example.com"
                          Summary = "Morning meeting"
                          StartInstant = instant 2026 2 3 14 0 // 9:00 ET
                          EndInstant = instant 2026 2 3 15 0 // 10:00 ET
                          IsAllDay = false }
                        { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "event-2@example.com"
                          Summary = "Afternoon meeting"
                          StartInstant = instant 2026 2 3 18 0 // 1:00 ET
                          EndInstant = instant 2026 2 3 19 0 // 2:00 ET
                          IsAllDay = false } ]

                  replaceEventsForSource conn sourceId events |> ignore

                  let result =
                      getCachedEventsInRange conn (instant 2026 2 3 0 0) (instant 2026 2 4 0 0)

                  Expect.hasLength result 2 "should find both events"
                  Expect.equal result.[0].Summary "Morning meeting" "first event summary"
                  Expect.equal result.[1].Summary "Afternoon meeting" "second event summary")
          }

          test "replaceEventsForSource deletes old events before inserting" {
              withMemoryDb (fun conn ->
                  let sourceId = Guid.NewGuid()
                  ensureSource conn sourceId

                  let oldEvents =
                      [ { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "old-event@example.com"
                          Summary = "Old event"
                          StartInstant = instant 2026 2 3 14 0
                          EndInstant = instant 2026 2 3 15 0
                          IsAllDay = false } ]

                  replaceEventsForSource conn sourceId oldEvents |> ignore

                  let newEvents =
                      [ { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "new-event@example.com"
                          Summary = "New event"
                          StartInstant = instant 2026 2 3 16 0
                          EndInstant = instant 2026 2 3 17 0
                          IsAllDay = false } ]

                  replaceEventsForSource conn sourceId newEvents |> ignore

                  let result =
                      getCachedEventsInRange conn (instant 2026 2 3 0 0) (instant 2026 2 4 0 0)

                  Expect.hasLength result 1 "old events replaced with new"
                  Expect.equal result.[0].Summary "New event" "only new event remains")
          }

          test "getCachedEventsInRange excludes out-of-range events" {
              withMemoryDb (fun conn ->
                  let sourceId = Guid.NewGuid()
                  ensureSource conn sourceId

                  let events =
                      [ { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "event-1@example.com"
                          Summary = "In range"
                          StartInstant = instant 2026 2 3 14 0
                          EndInstant = instant 2026 2 3 15 0
                          IsAllDay = false }
                        { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "event-2@example.com"
                          Summary = "Out of range"
                          StartInstant = instant 2026 2 5 14 0
                          EndInstant = instant 2026 2 5 15 0
                          IsAllDay = false } ]

                  replaceEventsForSource conn sourceId events |> ignore

                  let result =
                      getCachedEventsInRange conn (instant 2026 2 3 0 0) (instant 2026 2 4 0 0)

                  Expect.hasLength result 1 "should only find in-range event"
                  Expect.equal result.[0].Summary "In range" "correct event returned")
          }

          test "getCachedEventsInRange handles all-day events" {
              withMemoryDb (fun conn ->
                  let sourceId = Guid.NewGuid()
                  ensureSource conn sourceId

                  let events =
                      [ { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "allday-1@example.com"
                          Summary = "Holiday"
                          StartInstant = instant 2026 2 3 5 0 // midnight ET in UTC
                          EndInstant = instant 2026 2 4 5 0 // next midnight ET in UTC
                          IsAllDay = true } ]

                  replaceEventsForSource conn sourceId events |> ignore

                  let result =
                      getCachedEventsInRange conn (instant 2026 2 3 0 0) (instant 2026 2 5 0 0)

                  Expect.hasLength result 1 "should find all-day event"
                  Expect.isTrue result.[0].IsAllDay "should be marked as all-day")
          }

          test "replaceEventsForSource rolls back on failure" {
              withMemoryDb (fun conn ->
                  let sourceId = Guid.NewGuid()
                  // Insert source first for FK
                  let source: CalendarSource =
                      { Id = sourceId
                        Provider = Fastmail
                        BaseUrl = "https://example.com"
                        CalendarHomeUrl = None }

                  upsertCalendarSource conn source

                  let oldEvents =
                      [ { Id = Guid.NewGuid()
                          SourceId = sourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "old-event@example.com"
                          Summary = "Old event"
                          StartInstant = instant 2026 2 3 14 0
                          EndInstant = instant 2026 2 3 15 0
                          IsAllDay = false } ]

                  replaceEventsForSource conn sourceId oldEvents |> ignore

                  // Attempt a replace that should fail: use an event referencing a
                  // non-existent source_id, which violates the FK constraint.
                  // Note: FK enforcement is enabled by initializeDatabase.
                  let bogusSourceId = Guid.NewGuid()

                  let badEvents =
                      [ { Id = Guid.NewGuid()
                          SourceId = bogusSourceId
                          CalendarUrl = "https://example.com/cal/1"
                          Uid = "bad-event@example.com"
                          Summary = "Bad event"
                          StartInstant = instant 2026 2 3 16 0
                          EndInstant = instant 2026 2 3 17 0
                          IsAllDay = false } ]

                  // Plain INSERT (no OR IGNORE) should fail on FK violation,
                  // and the transaction rolls back preserving old events.
                  let result = replaceEventsForSource conn sourceId badEvents
                  Expect.isError result "FK-violating insert should return Error"

                  let result =
                      getCachedEventsInRange conn (instant 2026 2 3 0 0) (instant 2026 2 4 0 0)

                  Expect.hasLength result 1 "old events preserved after rollback"
                  Expect.equal result.[0].Summary "Old event" "original event still present")
          } ]

[<Tests>]
let getCachedBlockersTests =
    testList
        "getCachedBlockers"
        [ test "converts cached events to Interval list" {
              // Use a shared in-memory DB via a named data source
              let dbName = $"blockers-test-{Guid.NewGuid()}"
              let connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared"

              // Keep one connection open to keep the in-memory DB alive
              use keepAlive = new SqliteConnection(connStr)
              keepAlive.Open()

              match initializeDatabase keepAlive migrationsDir NodaTime.SystemClock.Instance with
              | Error msg -> failtestf "initializeDatabase failed: %s" msg
              | Ok() -> ()

              let sourceId = Guid.NewGuid()
              ensureSource keepAlive sourceId

              let events =
                  [ { Id = Guid.NewGuid()
                      SourceId = sourceId
                      CalendarUrl = "https://example.com/cal/1"
                      Uid = "blocker-1@example.com"
                      Summary = "Blocker event"
                      StartInstant = instant 2026 2 3 14 0
                      EndInstant = instant 2026 2 3 15 0
                      IsAllDay = false }
                    { Id = Guid.NewGuid()
                      SourceId = sourceId
                      CalendarUrl = "https://example.com/cal/1"
                      Uid = "blocker-2@example.com"
                      Summary = "Another blocker"
                      StartInstant = instant 2026 2 3 18 0
                      EndInstant = instant 2026 2 3 19 0
                      IsAllDay = false } ]

              replaceEventsForSource keepAlive sourceId events |> ignore

              let createConn () =
                  let conn = new SqliteConnection(connStr)
                  conn.Open()
                  conn

              let blockers =
                  getCachedBlockers createConn (instant 2026 2 3 0 0) (instant 2026 2 4 0 0)

              Expect.hasLength blockers 2 "should return two blocker intervals"
              Expect.isTrue (blockers.[0].Start.Equals(instant 2026 2 3 14 0)) "first blocker start"
              Expect.isTrue (blockers.[0].End.Equals(instant 2026 2 3 15 0)) "first blocker end"
              Expect.isTrue (blockers.[1].Start.Equals(instant 2026 2 3 18 0)) "second blocker start"
              Expect.isTrue (blockers.[1].End.Equals(instant 2026 2 3 19 0)) "second blocker end"
          } ]

// ---------------------------------------------------------------------------
// Write-back orchestration tests
// ---------------------------------------------------------------------------

type private MockHttpHandler(handler: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult(handler request)

let private makeClient (handler: HttpRequestMessage -> HttpResponseMessage) =
    new HttpClient(new MockHttpHandler(handler))

let private makeBookingForWriteBack () : Booking =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.NewGuid()
      ParticipantName = "Alice Smith"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = Some "+1-555-0100"
      Title = "Product Review"
      Description = Some "Discuss metrics"
      StartTime = pattern.Parse("2026-03-05T14:00:00-05:00").Value
      EndTime = pattern.Parse("2026-03-05T14:30:00-05:00").Value
      DurationMinutes = 30
      Timezone = "America/New_York"
      Status = Confirmed
      CreatedAt = Instant.FromUtc(2026, 3, 1, 10, 0)
      CancellationToken = Some(makeFakeCancellationToken ())
      CalDavEventHref = None }

let private makeWriteConfig () : CalDavWriteBackConfig =
    { SourceConfig =
        { Source =
            { Id = Guid.NewGuid()
              Provider = Fastmail
              BaseUrl = "https://caldav.example.com/dav/calendars"
              CalendarHomeUrl = None }
          Username = "user"
          Password = "pass" }
      CalendarUrl = "https://caldav.example.com/dav/calendars/user/test@example.com/Default/" }

[<Tests>]
let writeBackTests =
    testList
        "Write-back orchestration"
        [ test "writeBackBookingEvent calls putEvent with correct URL" {
              let mutable capturedUrl = ""

              let client =
                  makeClient (fun req ->
                      capturedUrl <- req.RequestUri.ToString()
                      new HttpResponseMessage(HttpStatusCode.Created, Content = new StringContent("")))

              let booking = makeBookingForWriteBack ()
              let config = makeWriteConfig ()

              withSharedMemoryDb (fun createConn ->
                  (writeBackBookingEvent createConn client config booking "host@example.com" None)
                      .Wait()

                  let expectedUrl =
                      $"https://caldav.example.com/dav/calendars/user/test@example.com/Default/{booking.Id}.ics"

                  Expect.equal capturedUrl expectedUrl "PUT URL should include booking ID")
          }

          test "writeBackBookingEvent stores href in DB on success" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(HttpStatusCode.Created, Content = new StringContent("")))

              let booking = makeBookingForWriteBack ()
              let config = makeWriteConfig ()

              withSharedMemoryDb (fun createConn ->
                  use setupConn = createConn ()
                  insertBooking setupConn booking |> ignore

                  (writeBackBookingEvent createConn client config booking "host@example.com" None)
                      .Wait()

                  use readConn = createConn ()
                  let loaded = getBookingById readConn booking.Id

                  Expect.isSome loaded "booking should exist"
                  Expect.isSome loaded.Value.CalDavEventHref "CalDavEventHref should be set"

                  let expectedUrl =
                      $"https://caldav.example.com/dav/calendars/user/test@example.com/Default/{booking.Id}.ics"

                  Expect.equal loaded.Value.CalDavEventHref (Some expectedUrl) "href should match PUT URL")
          }

          test "writeBackBookingEvent does not update DB on PUT failure" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(
                          HttpStatusCode.Forbidden,
                          Content = new StringContent("Access denied")
                      ))

              let booking = makeBookingForWriteBack ()
              let config = makeWriteConfig ()

              withSharedMemoryDb (fun createConn ->
                  use setupConn = createConn ()
                  insertBooking setupConn booking |> ignore

                  (writeBackBookingEvent createConn client config booking "host@example.com" None)
                      .Wait()

                  use readConn = createConn ()
                  let loaded = getBookingById readConn booking.Id

                  Expect.isSome loaded "booking should exist"
                  Expect.isNone loaded.Value.CalDavEventHref "CalDavEventHref should remain None on failure")
          }

          test "writeBackBookingEvent does not throw on PUT failure" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(
                          HttpStatusCode.InternalServerError,
                          Content = new StringContent("Server error")
                      ))

              let booking = makeBookingForWriteBack ()
              let config = makeWriteConfig ()

              withSharedMemoryDb (fun createConn ->
                  // Should complete without throwing
                  let task =
                      writeBackBookingEvent createConn client config booking "host@example.com" None

                  let completed = task.Wait(TimeSpan.FromSeconds(5.0))
                  Expect.isTrue completed "should complete without hanging")
          }

          test "deleteWriteBackEvent calls deleteEvent with stored href" {
              let mutable capturedUrl = ""

              let client =
                  makeClient (fun req ->
                      capturedUrl <- req.RequestUri.ToString()
                      new HttpResponseMessage(HttpStatusCode.NoContent))

              let href = "https://caldav.example.com/dav/calendars/user/test@example.com/Default/abc.ics"
              let booking = { makeBookingForWriteBack () with CalDavEventHref = Some href }

              (deleteWriteBackEvent client booking).Wait()

              Expect.equal capturedUrl href "DELETE URL should match stored href"
          }

          test "deleteWriteBackEvent is a no-op when CalDavEventHref is None" {
              let mutable requestMade = false

              let client =
                  makeClient (fun _ ->
                      requestMade <- true
                      new HttpResponseMessage(HttpStatusCode.NoContent))

              let booking = { makeBookingForWriteBack () with CalDavEventHref = None }

              (deleteWriteBackEvent client booking).Wait()

              Expect.isFalse requestMade "no HTTP request should be made when href is None"
          }

          test "deleteWriteBackEvent does not throw on DELETE failure" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(
                          HttpStatusCode.InternalServerError,
                          Content = new StringContent("Server error")
                      ))

              let booking =
                  { makeBookingForWriteBack () with
                      CalDavEventHref = Some "https://caldav.example.com/event.ics" }

              // Should complete without throwing
              let task = deleteWriteBackEvent client booking
              let completed = task.Wait(TimeSpan.FromSeconds(5.0))
              Expect.isTrue completed "should complete without hanging"
          } ]
