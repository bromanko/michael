module Michael.Database

open System
open System.Globalization
open Donald
open Microsoft.Data.Sqlite
open NodaTime
open NodaTime.Text
open Michael.Domain

// ---------------------------------------------------------------------------
// Connection
// ---------------------------------------------------------------------------

let createConnection (dbPath: string) =
    let conn = new SqliteConnection($"Data Source={dbPath}")
    conn.Open()
    conn

// ---------------------------------------------------------------------------
// Schema initialization
// ---------------------------------------------------------------------------

let private createTables (conn: SqliteConnection) =
    Db.newCommand
        """
        CREATE TABLE IF NOT EXISTS bookings (
            id                TEXT PRIMARY KEY,
            participant_name  TEXT NOT NULL,
            participant_email TEXT NOT NULL,
            participant_phone TEXT,
            title             TEXT NOT NULL,
            description       TEXT,
            start_time        TEXT NOT NULL,
            end_time          TEXT NOT NULL,
            start_instant     TEXT NOT NULL,
            end_instant       TEXT NOT NULL,
            duration_minutes  INTEGER NOT NULL,
            timezone          TEXT NOT NULL,
            status            TEXT NOT NULL DEFAULT 'confirmed',
            created_at        TEXT NOT NULL DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS host_availability (
            id          TEXT PRIMARY KEY,
            day_of_week INTEGER NOT NULL,
            start_time  TEXT NOT NULL,
            end_time    TEXT NOT NULL,
            timezone    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS calendar_sources (
            id                  TEXT PRIMARY KEY,
            provider            TEXT NOT NULL,
            base_url            TEXT NOT NULL,
            calendar_home_url   TEXT,
            last_synced_at      TEXT,
            last_sync_result    TEXT
        );

        CREATE TABLE IF NOT EXISTS cached_events (
            id              TEXT PRIMARY KEY,
            source_id       TEXT NOT NULL,
            calendar_url    TEXT NOT NULL,
            uid             TEXT NOT NULL,
            summary         TEXT NOT NULL,
            start_instant   TEXT NOT NULL,
            end_instant     TEXT NOT NULL,
            is_all_day      INTEGER NOT NULL DEFAULT 0,
            UNIQUE(source_id, uid, start_instant),
            FOREIGN KEY (source_id) REFERENCES calendar_sources(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_bookings_start_instant ON bookings(start_instant);
        CREATE INDEX IF NOT EXISTS idx_bookings_end_instant ON bookings(end_instant);
        CREATE INDEX IF NOT EXISTS idx_cached_events_start_instant ON cached_events(start_instant);
        CREATE INDEX IF NOT EXISTS idx_cached_events_end_instant ON cached_events(end_instant);
        """
        conn
    |> Db.exec

let private seedHostAvailability (conn: SqliteConnection) (hostTzId: string) =
    let count =
        Db.newCommand "SELECT COUNT(*) FROM host_availability" conn
        |> Db.scalar (fun o -> Convert.ToInt64(o))

    if count = 0L then
        for day in 1..5 do
            Db.newCommand
                """
                INSERT INTO host_availability (id, day_of_week, start_time, end_time, timezone)
                VALUES (@id, @day, @start, @end, @tz)
                """
                conn
            |> Db.setParams [
                "id", SqlType.String (Guid.NewGuid().ToString())
                "day", SqlType.Int32 day
                "start", SqlType.String "09:00"
                "end", SqlType.String "17:00"
                "tz", SqlType.String hostTzId
            ]
            |> Db.exec

let initializeDatabase (conn: SqliteConnection) (hostTzId: string) =
    Db.newCommand "PRAGMA foreign_keys = ON" conn |> Db.exec
    createTables conn
    seedHostAvailability conn hostTzId

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

let private parseTime (s: string) =
    LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(s).GetValueOrThrow()

let private odtPattern = OffsetDateTimePattern.ExtendedIso
let private instantPattern = InstantPattern.ExtendedIso

let private parseOdtFromDb (s: string) =
    let r = odtPattern.Parse(s)
    if r.Success then r.Value
    else failwith $"Corrupt OffsetDateTime in database: {s}"

let getHostAvailability (conn: SqliteConnection) : HostAvailabilitySlot list =
    Db.newCommand
        "SELECT id, day_of_week, start_time, end_time, timezone FROM host_availability"
        conn
    |> Db.query (fun rd ->
        { Id = rd.ReadGuid "id"
          DayOfWeek = enum<IsoDayOfWeek> (rd.ReadInt32 "day_of_week")
          StartTime = parseTime (rd.ReadString "start_time")
          EndTime = parseTime (rd.ReadString "end_time")
          Timezone = rd.ReadString "timezone" })

let private readBooking (rd: System.Data.IDataReader) : Booking =
    { Id = rd.ReadGuid "id"
      ParticipantName = rd.ReadString "participant_name"
      ParticipantEmail = rd.ReadString "participant_email"
      ParticipantPhone = rd.ReadStringOption "participant_phone"
      Title = rd.ReadString "title"
      Description = rd.ReadStringOption "description"
      StartTime = parseOdtFromDb (rd.ReadString "start_time")
      EndTime = parseOdtFromDb (rd.ReadString "end_time")
      DurationMinutes = rd.ReadInt32 "duration_minutes"
      Timezone = rd.ReadString "timezone"
      Status =
          match rd.ReadString "status" with
          | "confirmed" -> Confirmed
          | "cancelled" -> Cancelled
          | other -> failwith $"Unknown booking status: {other}"
      CreatedAt =
          let dt = rd.ReadString "created_at"
          Instant.FromDateTimeUtc(DateTime.Parse(dt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal)) }

let getBookingsInRange
    (conn: SqliteConnection)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    : Booking list =
    Db.newCommand
        """
        SELECT id, participant_name, participant_email, participant_phone,
               title, description, start_time, end_time, duration_minutes,
               timezone, status, created_at
        FROM bookings
        WHERE status = 'confirmed'
          AND start_instant < @rangeEnd
          AND end_instant > @rangeStart
        """
        conn
    |> Db.setParams [
        "rangeStart", SqlType.String (instantPattern.Format(rangeStart))
        "rangeEnd", SqlType.String (instantPattern.Format(rangeEnd))
    ]
    |> Db.query readBooking

let getOverlappingBookings
    (conn: SqliteConnection)
    (startInstant: Instant)
    (endInstant: Instant)
    : Booking list =
    Db.newCommand
        """
        SELECT id, participant_name, participant_email, participant_phone,
               title, description, start_time, end_time, duration_minutes,
               timezone, status, created_at
        FROM bookings
        WHERE status = 'confirmed'
          AND start_instant < @endInstant
          AND end_instant > @startInstant
        """
        conn
    |> Db.setParams [
        "startInstant", SqlType.String (instantPattern.Format(startInstant))
        "endInstant", SqlType.String (instantPattern.Format(endInstant))
    ]
    |> Db.query readBooking

let insertBooking (conn: SqliteConnection) (booking: Booking) : Result<unit, string> =
    // Pragmatic boundary exception: Donald wraps SQLite errors in DbExecutionException.
    // We catch it here at the DB boundary rather than exposing exceptions to callers.
    try
        Db.newCommand
            """
            INSERT INTO bookings (id, participant_name, participant_email, participant_phone,
                                  title, description, start_time, end_time,
                                  start_instant, end_instant,
                                  duration_minutes, timezone, status)
            VALUES (@id, @name, @email, @phone, @title, @desc, @start, @end,
                    @startInstant, @endInstant, @dur, @tz, @status)
            """
            conn
        |> Db.setParams [
            "id", SqlType.String (booking.Id.ToString())
            "name", SqlType.String booking.ParticipantName
            "email", SqlType.String booking.ParticipantEmail
            "phone", (match booking.ParticipantPhone with Some p -> SqlType.String p | None -> SqlType.Null)
            "title", SqlType.String booking.Title
            "desc", (match booking.Description with Some d -> SqlType.String d | None -> SqlType.Null)
            "start", SqlType.String (odtPattern.Format(booking.StartTime))
            "end", SqlType.String (odtPattern.Format(booking.EndTime))
            "startInstant", SqlType.String (instantPattern.Format(booking.StartTime.ToInstant()))
            "endInstant", SqlType.String (instantPattern.Format(booking.EndTime.ToInstant()))
            "dur", SqlType.Int32 booking.DurationMinutes
            "tz", SqlType.String booking.Timezone
            "status", SqlType.String (match booking.Status with Confirmed -> "confirmed" | Cancelled -> "cancelled")
        ]
        |> Db.exec

        Ok ()
    with :? Donald.DbExecutionException as ex ->
        Error ex.Message

// ---------------------------------------------------------------------------
// Calendar source queries
// ---------------------------------------------------------------------------

let private parseInstantFromDb (s: string) =
    let r = instantPattern.Parse(s)
    if r.Success then r.Value
    else failwith $"Corrupt Instant in database: {s}"

let private providerToString (p: CalDavProvider) =
    match p with
    | Fastmail -> "fastmail"
    | ICloud -> "icloud"

let upsertCalendarSource (conn: SqliteConnection) (source: CalendarSource) =
    Db.newCommand
        """
        INSERT INTO calendar_sources (id, provider, base_url, calendar_home_url)
        VALUES (@id, @provider, @baseUrl, @calendarHomeUrl)
        ON CONFLICT(id) DO UPDATE SET
            provider = excluded.provider,
            base_url = excluded.base_url,
            calendar_home_url = excluded.calendar_home_url
        """
        conn
    |> Db.setParams [
        "id", SqlType.String (source.Id.ToString())
        "provider", SqlType.String (providerToString source.Provider)
        "baseUrl", SqlType.String source.BaseUrl
        "calendarHomeUrl",
            (match source.CalendarHomeUrl with
             | Some url -> SqlType.String url
             | None -> SqlType.Null)
    ]
    |> Db.exec

let updateSyncStatus (conn: SqliteConnection) (sourceId: Guid) (syncedAt: Instant) (result: string) =
    Db.newCommand
        """
        UPDATE calendar_sources
        SET last_synced_at = @syncedAt, last_sync_result = @result
        WHERE id = @id
        """
        conn
    |> Db.setParams [
        "id", SqlType.String (sourceId.ToString())
        "syncedAt", SqlType.String (instantPattern.Format(syncedAt))
        "result", SqlType.String result
    ]
    |> Db.exec

let getCachedEventsInRange
    (conn: SqliteConnection)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    : CachedEvent list =
    Db.newCommand
        """
        SELECT id, source_id, calendar_url, uid, summary, start_instant, end_instant, is_all_day
        FROM cached_events
        WHERE start_instant < @rangeEnd
          AND end_instant > @rangeStart
        """
        conn
    |> Db.setParams [
        "rangeStart", SqlType.String (instantPattern.Format(rangeStart))
        "rangeEnd", SqlType.String (instantPattern.Format(rangeEnd))
    ]
    |> Db.query (fun rd ->
        { Id = rd.ReadGuid "id"
          SourceId = rd.ReadGuid "source_id"
          CalendarUrl = rd.ReadString "calendar_url"
          Uid = rd.ReadString "uid"
          Summary = rd.ReadString "summary"
          StartInstant = parseInstantFromDb (rd.ReadString "start_instant")
          EndInstant = parseInstantFromDb (rd.ReadString "end_instant")
          IsAllDay = rd.ReadInt32 "is_all_day" <> 0 })

let replaceEventsForSource (conn: SqliteConnection) (sourceId: Guid) (events: CachedEvent list) =
    // Microsoft.Data.Sqlite auto-enlists commands on a connection into the
    // connection's active transaction, so we don't need to pass the transaction
    // to each command explicitly.
    use tx = conn.BeginTransaction()

    Db.newCommand
        "DELETE FROM cached_events WHERE source_id = @sourceId"
        conn
    |> Db.setParams [ "sourceId", SqlType.String (sourceId.ToString()) ]
    |> Db.exec

    for evt in events do
        Db.newCommand
            """
            INSERT INTO cached_events (id, source_id, calendar_url, uid, summary, start_instant, end_instant, is_all_day)
            VALUES (@id, @sourceId, @calendarUrl, @uid, @summary, @startInstant, @endInstant, @isAllDay)
            """
            conn
        |> Db.setParams [
            "id", SqlType.String (evt.Id.ToString())
            "sourceId", SqlType.String (evt.SourceId.ToString())
            "calendarUrl", SqlType.String evt.CalendarUrl
            "uid", SqlType.String evt.Uid
            "summary", SqlType.String evt.Summary
            "startInstant", SqlType.String (instantPattern.Format(evt.StartInstant))
            "endInstant", SqlType.String (instantPattern.Format(evt.EndInstant))
            "isAllDay", SqlType.Int32 (if evt.IsAllDay then 1 else 0)
        ]
        |> Db.exec

    tx.Commit()
