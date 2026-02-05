module Michael.Database

open System
open System.Data
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
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS bookings (
            id                TEXT PRIMARY KEY,
            participant_name  TEXT NOT NULL,
            participant_email TEXT NOT NULL,
            participant_phone TEXT,
            title             TEXT NOT NULL,
            description       TEXT,
            start_time        TEXT NOT NULL,
            end_time          TEXT NOT NULL,
            start_epoch       INTEGER NOT NULL,
            end_epoch         INTEGER NOT NULL,
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
            id                TEXT PRIMARY KEY,
            provider          TEXT NOT NULL,
            base_url          TEXT NOT NULL,
            calendar_home_url TEXT,
            last_synced_at    TEXT,
            last_sync_result  TEXT
        );

        CREATE TABLE IF NOT EXISTS cached_events (
            id            TEXT PRIMARY KEY,
            source_id     TEXT NOT NULL REFERENCES calendar_sources(id),
            calendar_url  TEXT NOT NULL,
            uid           TEXT NOT NULL,
            summary       TEXT NOT NULL,
            start_instant TEXT NOT NULL,
            end_instant   TEXT NOT NULL,
            is_all_day    INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_cached_events_source
            ON cached_events (source_id);
        CREATE INDEX IF NOT EXISTS idx_cached_events_range
            ON cached_events (start_instant, end_instant);

        CREATE TABLE IF NOT EXISTS scheduling_settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS sync_status (
            source_id    TEXT PRIMARY KEY,
            last_sync_at TEXT NOT NULL,
            status       TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS admin_sessions (
            token      TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            expires_at TEXT NOT NULL
        );
        """
        conn
    |> Db.exec

let private seedHostAvailability (conn: SqliteConnection) (timezone: string) =
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
            |> Db.setParams
                [ "id", SqlType.String(Guid.NewGuid().ToString())
                  "day", SqlType.Int32 day
                  "start", SqlType.String "09:00"
                  "end", SqlType.String "17:00"
                  "tz", SqlType.String timezone ]
            |> Db.exec

let initializeDatabase (conn: SqliteConnection) (timezone: string) =
    // Enable FK enforcement for this connection
    Db.newCommand "PRAGMA foreign_keys = ON" conn |> Db.exec
    createTables conn
    seedHostAvailability conn timezone

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

let tryParseTime (s: string) : LocalTime option =
    let parts = s.Split(':')

    if parts.Length = 2 then
        match Int32.TryParse(parts.[0]), Int32.TryParse(parts.[1]) with
        | (true, h), (true, m) when h >= 0 && h <= 23 && m >= 0 && m <= 59 -> Some(LocalTime(h, m))
        | _ -> None
    else
        None

let private parseTime (s: string) =
    // For reading from DB where format is guaranteed valid
    tryParseTime s
    |> Option.defaultWith (fun () -> failwith $"Invalid time format in database: '{s}'")

let formatTime (t: LocalTime) = sprintf "%02d:%02d" t.Hour t.Minute

let private odtPattern = OffsetDateTimePattern.ExtendedIso
let private instantPattern = InstantPattern.ExtendedIso

let getHostAvailability (conn: SqliteConnection) : HostAvailabilitySlot list =
    Db.newCommand "SELECT id, day_of_week, start_time, end_time, timezone FROM host_availability" conn
    |> Db.query (fun rd ->
        { Id = rd.ReadGuid "id"
          DayOfWeek = enum<IsoDayOfWeek> (rd.ReadInt32 "day_of_week")
          StartTime = parseTime (rd.ReadString "start_time")
          EndTime = parseTime (rd.ReadString "end_time")
          Timezone = rd.ReadString "timezone" })

let replaceHostAvailability (conn: SqliteConnection) (slots: HostAvailabilitySlot list) : Result<unit, string> =
    use txn = conn.BeginTransaction()

    try
        Db.newCommand "DELETE FROM host_availability" conn |> Db.exec

        for slot in slots do
            Db.newCommand
                """
                INSERT INTO host_availability (id, day_of_week, start_time, end_time, timezone)
                VALUES (@id, @day, @start, @end, @tz)
                """
                conn
            |> Db.setParams
                [ "id", SqlType.String(slot.Id.ToString())
                  "day", SqlType.Int32(int slot.DayOfWeek)
                  "start", SqlType.String(formatTime slot.StartTime)
                  "end", SqlType.String(formatTime slot.EndTime)
                  "tz", SqlType.String slot.Timezone ]
            |> Db.exec

        txn.Commit()
        Ok()
    with ex ->
        txn.Rollback()
        Error ex.Message

let private parseOdt (fieldName: string) (bookingId: Guid) (value: string) =
    let result = odtPattern.Parse(value)

    if result.Success then
        result.Value
    else
        failwith $"Invalid {fieldName} in booking {bookingId}: '{value}'"

let private readBooking (rd: IDataReader) : Booking =
    let id = rd.ReadGuid "id"

    { Id = id
      ParticipantName = rd.ReadString "participant_name"
      ParticipantEmail = rd.ReadString "participant_email"
      ParticipantPhone = rd.ReadStringOption "participant_phone"
      Title = rd.ReadString "title"
      Description = rd.ReadStringOption "description"
      StartTime = parseOdt "start_time" id (rd.ReadString "start_time")
      EndTime = parseOdt "end_time" id (rd.ReadString "end_time")
      DurationMinutes = rd.ReadInt32 "duration_minutes"
      Timezone = rd.ReadString "timezone"
      Status =
        match rd.ReadString "status" with
        | "confirmed" -> Confirmed
        | "cancelled" -> Cancelled
        | other -> failwith $"Unknown booking status in database: '{other}'"
      CreatedAt =
        let dt = rd.ReadString "created_at"
        Instant.FromDateTimeUtc(DateTime.Parse(dt).ToUniversalTime()) }

let getBookingsInRange (conn: SqliteConnection) (rangeStart: Instant) (rangeEnd: Instant) : Booking list =
    Db.newCommand
        """
        SELECT id, participant_name, participant_email, participant_phone,
               title, description, start_time, end_time, duration_minutes,
               timezone, status, created_at
        FROM bookings
        WHERE status = 'confirmed'
          AND start_epoch < @rangeEnd
          AND end_epoch > @rangeStart
        """
        conn
    |> Db.setParams
        [ "rangeStart", SqlType.Int64(rangeStart.ToUnixTimeSeconds())
          "rangeEnd", SqlType.Int64(rangeEnd.ToUnixTimeSeconds()) ]
    |> Db.query readBooking

// ---------------------------------------------------------------------------
// Calendar sources
// ---------------------------------------------------------------------------

let private providerToString (p: CalDavProvider) =
    match p with
    | Fastmail -> "fastmail"
    | ICloud -> "icloud"

let private providerFromString (s: string) =
    match s.ToLowerInvariant() with
    | "fastmail" -> Fastmail
    | "icloud" -> ICloud
    | other -> failwith $"Unknown CalDAV provider in database: '{other}'"

let upsertCalendarSource (conn: SqliteConnection) (source: CalendarSource) =
    Db.newCommand
        """
        INSERT INTO calendar_sources (id, provider, base_url, calendar_home_url)
        VALUES (@id, @provider, @baseUrl, @homeUrl)
        ON CONFLICT (id) DO UPDATE SET
            provider = @provider,
            base_url = @baseUrl,
            calendar_home_url = @homeUrl
        """
        conn
    |> Db.setParams
        [ "id", SqlType.String(source.Id.ToString())
          "provider", SqlType.String(providerToString source.Provider)
          "baseUrl", SqlType.String source.BaseUrl
          "homeUrl",
          (match source.CalendarHomeUrl with
           | Some url -> SqlType.String url
           | None -> SqlType.Null) ]
    |> Db.exec

let private readCalendarSourceStatus (rd: IDataReader) : CalendarSourceStatus =
    { Source =
        { Id = rd.ReadGuid "id"
          Provider = providerFromString (rd.ReadString "provider")
          BaseUrl = rd.ReadString "base_url"
          CalendarHomeUrl = rd.ReadStringOption "calendar_home_url" }
      LastSyncedAt =
        rd.ReadStringOption "last_synced_at"
        |> Option.bind (fun s ->
            let result = instantPattern.Parse(s)
            if result.Success then Some result.Value else None)
      LastSyncResult = rd.ReadStringOption "last_sync_result" }

let listCalendarSources (conn: SqliteConnection) : CalendarSourceStatus list =
    Db.newCommand
        "SELECT id, provider, base_url, calendar_home_url, last_synced_at, last_sync_result FROM calendar_sources"
        conn
    |> Db.query readCalendarSourceStatus

let getCalendarSourceById (conn: SqliteConnection) (id: Guid) : CalendarSourceStatus option =
    Db.newCommand
        "SELECT id, provider, base_url, calendar_home_url, last_synced_at, last_sync_result FROM calendar_sources WHERE id = @id"
        conn
    |> Db.setParams [ "id", SqlType.String(id.ToString()) ]
    |> Db.query readCalendarSourceStatus
    |> List.tryHead

// ---------------------------------------------------------------------------
// Cached events
// ---------------------------------------------------------------------------

let replaceEventsForSource (conn: SqliteConnection) (sourceId: Guid) (events: CachedEvent list) : Result<unit, string> =
    use txn = conn.BeginTransaction()

    try
        Db.newCommand "DELETE FROM cached_events WHERE source_id = @sourceId" conn
        |> Db.setParams [ "sourceId", SqlType.String(sourceId.ToString()) ]
        |> Db.exec

        for evt in events do
            Db.newCommand
                """
                INSERT INTO cached_events (id, source_id, calendar_url, uid, summary, start_instant, end_instant, is_all_day)
                VALUES (@id, @sourceId, @calUrl, @uid, @summary, @start, @end, @allDay)
                """
                conn
            |> Db.setParams
                [ "id", SqlType.String(evt.Id.ToString())
                  "sourceId", SqlType.String(evt.SourceId.ToString())
                  "calUrl", SqlType.String evt.CalendarUrl
                  "uid", SqlType.String evt.Uid
                  "summary", SqlType.String evt.Summary
                  "start", SqlType.String(instantPattern.Format(evt.StartInstant))
                  "end", SqlType.String(instantPattern.Format(evt.EndInstant))
                  "allDay", SqlType.Int32(if evt.IsAllDay then 1 else 0) ]
            |> Db.exec

        txn.Commit()
        Ok()
    with ex ->
        txn.Rollback()
        Error ex.Message

let updateSyncStatus
    (conn: SqliteConnection)
    (sourceId: Guid)
    (syncedAt: Instant)
    (status: string)
    : Result<unit, string> =
    try
        Db.newCommand
            """
            UPDATE calendar_sources
            SET last_synced_at = @syncedAt, last_sync_result = @status
            WHERE id = @sourceId
            """
            conn
        |> Db.setParams
            [ "sourceId", SqlType.String(sourceId.ToString())
              "syncedAt", SqlType.String(instantPattern.Format(syncedAt))
              "status", SqlType.String status ]
        |> Db.exec

        Ok()
    with ex ->
        Error ex.Message

let getCachedEventsInRange (conn: SqliteConnection) (rangeStart: Instant) (rangeEnd: Instant) : CachedEvent list =
    Db.newCommand
        """
        SELECT id, source_id, calendar_url, uid, summary, start_instant, end_instant, is_all_day
        FROM cached_events
        WHERE start_instant < @rangeEnd
          AND end_instant > @rangeStart
        """
        conn
    |> Db.setParams
        [ "rangeStart", SqlType.String(instantPattern.Format(rangeStart))
          "rangeEnd", SqlType.String(instantPattern.Format(rangeEnd)) ]
    |> Db.query (fun rd ->
        { Id = rd.ReadGuid "id"
          SourceId = rd.ReadGuid "source_id"
          CalendarUrl = rd.ReadString "calendar_url"
          Uid = rd.ReadString "uid"
          Summary = rd.ReadString "summary"
          StartInstant = instantPattern.Parse(rd.ReadString "start_instant").Value
          EndInstant = instantPattern.Parse(rd.ReadString "end_instant").Value
          IsAllDay = rd.ReadInt32 "is_all_day" = 1 })

// ---------------------------------------------------------------------------
// Bookings
// ---------------------------------------------------------------------------

let insertBooking (conn: SqliteConnection) (booking: Booking) : Result<unit, string> =
    try
        Db.newCommand
            """
            INSERT INTO bookings (id, participant_name, participant_email, participant_phone,
                                  title, description, start_time, end_time, start_epoch, end_epoch,
                                  duration_minutes, timezone, status)
            VALUES (@id, @name, @email, @phone, @title, @desc, @start, @end, @startEpoch, @endEpoch, @dur, @tz, @status)
            """
            conn
        |> Db.setParams
            [ "id", SqlType.String(booking.Id.ToString())
              "name", SqlType.String booking.ParticipantName
              "email", SqlType.String booking.ParticipantEmail
              "phone",
              (match booking.ParticipantPhone with
               | Some p -> SqlType.String p
               | None -> SqlType.Null)
              "title", SqlType.String booking.Title
              "desc",
              (match booking.Description with
               | Some d -> SqlType.String d
               | None -> SqlType.Null)
              "start", SqlType.String(odtPattern.Format(booking.StartTime))
              "end", SqlType.String(odtPattern.Format(booking.EndTime))
              "startEpoch", SqlType.Int64(booking.StartTime.ToInstant().ToUnixTimeSeconds())
              "endEpoch", SqlType.Int64(booking.EndTime.ToInstant().ToUnixTimeSeconds())
              "dur", SqlType.Int32 booking.DurationMinutes
              "tz", SqlType.String booking.Timezone
              "status",
              SqlType.String(
                  match booking.Status with
                  | Confirmed -> "confirmed"
                  | Cancelled -> "cancelled"
              ) ]
        |> Db.exec

        Ok()
    with ex ->
        Error ex.Message

// ---------------------------------------------------------------------------
// Admin sessions
// ---------------------------------------------------------------------------

let insertAdminSession (conn: SqliteConnection) (session: AdminSession) : Result<unit, string> =
    try
        Db.newCommand
            """
            INSERT INTO admin_sessions (token, created_at, expires_at)
            VALUES (@token, @createdAt, @expiresAt)
            """
            conn
        |> Db.setParams
            [ "token", SqlType.String session.Token
              "createdAt", SqlType.String(instantPattern.Format(session.CreatedAt))
              "expiresAt", SqlType.String(instantPattern.Format(session.ExpiresAt)) ]
        |> Db.exec

        Ok()
    with ex ->
        Error ex.Message

let getAdminSession (conn: SqliteConnection) (token: string) : AdminSession option =
    Db.newCommand "SELECT token, created_at, expires_at FROM admin_sessions WHERE token = @token" conn
    |> Db.setParams [ "token", SqlType.String token ]
    |> Db.query (fun rd ->
        { Token = rd.ReadString "token"
          CreatedAt = instantPattern.Parse(rd.ReadString "created_at").Value
          ExpiresAt = instantPattern.Parse(rd.ReadString "expires_at").Value })
    |> List.tryHead

let deleteAdminSession (conn: SqliteConnection) (token: string) : Result<unit, string> =
    try
        Db.newCommand "DELETE FROM admin_sessions WHERE token = @token" conn
        |> Db.setParams [ "token", SqlType.String token ]
        |> Db.exec

        Ok()
    with ex ->
        Error ex.Message

let deleteExpiredAdminSessions (conn: SqliteConnection) (now: Instant) : Result<unit, string> =
    try
        Db.newCommand "DELETE FROM admin_sessions WHERE expires_at < @now" conn
        |> Db.setParams [ "now", SqlType.String(instantPattern.Format(now)) ]
        |> Db.exec

        Ok()
    with ex ->
        Error ex.Message

// ---------------------------------------------------------------------------
// Admin booking queries
// ---------------------------------------------------------------------------

let listBookings
    (conn: SqliteConnection)
    (page: int)
    (pageSize: int)
    (statusFilter: string option)
    : (Booking list * int) =
    let whereClause =
        match statusFilter with
        | Some status -> $"WHERE status = @status"
        | None -> ""

    let statusParams =
        match statusFilter with
        | Some status -> [ "status", SqlType.String status ]
        | None -> []

    let totalCount =
        Db.newCommand $"SELECT COUNT(*) FROM bookings {whereClause}" conn
        |> Db.setParams statusParams
        |> Db.scalar (fun o -> Convert.ToInt64(o) |> int)

    let offset = (page - 1) * pageSize

    let bookings =
        Db.newCommand
            $"""
            SELECT id, participant_name, participant_email, participant_phone,
                   title, description, start_time, end_time, duration_minutes,
                   timezone, status, created_at
            FROM bookings
            {whereClause}
            ORDER BY start_epoch DESC
            LIMIT @limit OFFSET @offset
            """
            conn
        |> Db.setParams (
            statusParams
            @ [ "limit", SqlType.Int32 pageSize; "offset", SqlType.Int32 offset ]
        )
        |> Db.query readBooking

    (bookings, totalCount)

let getBookingById (conn: SqliteConnection) (id: Guid) : Booking option =
    Db.newCommand
        """
        SELECT id, participant_name, participant_email, participant_phone,
               title, description, start_time, end_time, duration_minutes,
               timezone, status, created_at
        FROM bookings
        WHERE id = @id
        """
        conn
    |> Db.setParams [ "id", SqlType.String(id.ToString()) ]
    |> Db.query readBooking
    |> List.tryHead

let cancelBooking (conn: SqliteConnection) (id: Guid) : Result<unit, string> =
    match getBookingById conn id with
    | None -> Error "Booking not found."
    | Some b ->
        match b.Status with
        | Cancelled -> Error "Booking is already cancelled."
        | Confirmed ->
            Db.newCommand
                """
                UPDATE bookings SET status = 'cancelled'
                WHERE id = @id AND status = 'confirmed'
                """
                conn
            |> Db.setParams [ "id", SqlType.String(id.ToString()) ]
            |> Db.exec

            Ok()

let getUpcomingBookingsCount (conn: SqliteConnection) (now: Instant) : int =
    Db.newCommand "SELECT COUNT(*) FROM bookings WHERE status = 'confirmed' AND start_epoch > @now" conn
    |> Db.setParams [ "now", SqlType.Int64(now.ToUnixTimeSeconds()) ]
    |> Db.scalar (fun o -> Convert.ToInt64(o) |> int)

let getNextBooking (conn: SqliteConnection) (now: Instant) : Booking option =
    Db.newCommand
        """
        SELECT id, participant_name, participant_email, participant_phone,
               title, description, start_time, end_time, duration_minutes,
               timezone, status, created_at
        FROM bookings
        WHERE status = 'confirmed' AND start_epoch > @now
        ORDER BY start_epoch ASC
        LIMIT 1
        """
        conn
    |> Db.setParams [ "now", SqlType.Int64(now.ToUnixTimeSeconds()) ]
    |> Db.query readBooking
    |> List.tryHead

// ---------------------------------------------------------------------------
// Scheduling settings
// ---------------------------------------------------------------------------

let private defaultSettings: SchedulingSettings =
    { MinNoticeHours = 6
      BookingWindowDays = 30
      DefaultDurationMinutes = 30
      VideoLink = None }

let private getSetting (conn: SqliteConnection) (key: string) : string option =
    Db.newCommand "SELECT value FROM scheduling_settings WHERE key = @key" conn
    |> Db.setParams [ "key", SqlType.String key ]
    |> Db.query (fun rd -> rd.ReadString "value")
    |> List.tryHead

let private setSetting (conn: SqliteConnection) (key: string) (value: string) =
    Db.newCommand
        """
        INSERT INTO scheduling_settings (key, value) VALUES (@key, @value)
        ON CONFLICT (key) DO UPDATE SET value = @value
        """
        conn
    |> Db.setParams [ "key", SqlType.String key; "value", SqlType.String value ]
    |> Db.exec

let getSchedulingSettings (conn: SqliteConnection) : SchedulingSettings =
    let minNotice =
        getSetting conn "min_notice_hours"
        |> Option.bind (fun s ->
            match Int32.TryParse(s) with
            | true, v -> Some v
            | _ -> None)
        |> Option.defaultValue defaultSettings.MinNoticeHours

    let bookingWindow =
        getSetting conn "booking_window_days"
        |> Option.bind (fun s ->
            match Int32.TryParse(s) with
            | true, v -> Some v
            | _ -> None)
        |> Option.defaultValue defaultSettings.BookingWindowDays

    let defaultDuration =
        getSetting conn "default_duration_minutes"
        |> Option.bind (fun s ->
            match Int32.TryParse(s) with
            | true, v -> Some v
            | _ -> None)
        |> Option.defaultValue defaultSettings.DefaultDurationMinutes

    let videoLink =
        getSetting conn "video_link"
        |> Option.bind (fun s -> if String.IsNullOrWhiteSpace(s) then None else Some s)

    { MinNoticeHours = minNotice
      BookingWindowDays = bookingWindow
      DefaultDurationMinutes = defaultDuration
      VideoLink = videoLink }

let updateSchedulingSettings (conn: SqliteConnection) (settings: SchedulingSettings) : Result<unit, string> =
    try
        setSetting conn "min_notice_hours" (string settings.MinNoticeHours)
        setSetting conn "booking_window_days" (string settings.BookingWindowDays)
        setSetting conn "default_duration_minutes" (string settings.DefaultDurationMinutes)

        match settings.VideoLink with
        | Some link -> setSetting conn "video_link" link
        | None -> setSetting conn "video_link" ""

        Ok()
    with ex ->
        Error ex.Message
