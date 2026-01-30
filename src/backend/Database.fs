module Michael.Database

open System
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
        """
        conn
    |> Db.exec

let private seedHostAvailability (conn: SqliteConnection) =
    let count =
        Db.newCommand "SELECT COUNT(*) FROM host_availability" conn
        |> Db.scalar (fun o -> Convert.ToInt64(o))

    if count = 0L then
        let tz = "America/New_York"

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
                "tz", SqlType.String tz
            ]
            |> Db.exec

let initializeDatabase (conn: SqliteConnection) =
    createTables conn
    seedHostAvailability conn

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

let private parseTime (s: string) =
    let parts = s.Split(':')
    LocalTime(int parts.[0], int parts.[1])

let private odtPattern = OffsetDateTimePattern.ExtendedIso

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

let getBookingsInRange
    (conn: SqliteConnection)
    (rangeStart: OffsetDateTime)
    (rangeEnd: OffsetDateTime)
    : Booking list =
    Db.newCommand
        """
        SELECT id, participant_name, participant_email, participant_phone,
               title, description, start_time, end_time, duration_minutes,
               timezone, status, created_at
        FROM bookings
        WHERE status = 'confirmed'
          AND start_time < @rangeEnd
          AND end_time > @rangeStart
        """
        conn
    |> Db.setParams [
        "rangeStart", SqlType.String (odtPattern.Format(rangeStart))
        "rangeEnd", SqlType.String (odtPattern.Format(rangeEnd))
    ]
    |> Db.query (fun rd ->
        { Id = rd.ReadGuid "id"
          ParticipantName = rd.ReadString "participant_name"
          ParticipantEmail = rd.ReadString "participant_email"
          ParticipantPhone = rd.ReadStringOption "participant_phone"
          Title = rd.ReadString "title"
          Description = rd.ReadStringOption "description"
          StartTime = odtPattern.Parse(rd.ReadString "start_time").Value
          EndTime = odtPattern.Parse(rd.ReadString "end_time").Value
          DurationMinutes = rd.ReadInt32 "duration_minutes"
          Timezone = rd.ReadString "timezone"
          Status =
              match rd.ReadString "status" with
              | "confirmed" -> Confirmed
              | _ -> Cancelled
          CreatedAt =
              let dt = rd.ReadString "created_at"
              Instant.FromDateTimeUtc(DateTime.Parse(dt).ToUniversalTime()) })

let insertBooking (conn: SqliteConnection) (booking: Booking) : Result<unit, string> =
    try
        Db.newCommand
            """
            INSERT INTO bookings (id, participant_name, participant_email, participant_phone,
                                  title, description, start_time, end_time, duration_minutes,
                                  timezone, status)
            VALUES (@id, @name, @email, @phone, @title, @desc, @start, @end, @dur, @tz, @status)
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
            "dur", SqlType.Int32 booking.DurationMinutes
            "tz", SqlType.String booking.Timezone
            "status", SqlType.String (match booking.Status with Confirmed -> "confirmed" | Cancelled -> "cancelled")
        ]
        |> Db.exec

        Ok ()
    with ex ->
        Error ex.Message
