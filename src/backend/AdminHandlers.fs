module Michael.AdminHandlers

open System
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open NodaTime.Text
open Serilog
open Michael.Domain
open Michael.Database
open Michael.Email
open Michael.HttpHelpers

let private log () =
    Log.ForContext("SourceContext", "Michael.AdminHandlers")

// ---------------------------------------------------------------------------
// Response DTOs
// ---------------------------------------------------------------------------

type BookingDto =
    { Id: string
      ParticipantName: string
      ParticipantEmail: string
      ParticipantPhone: string option
      Title: string
      Description: string option
      StartTime: string
      EndTime: string
      DurationMinutes: int
      Timezone: string
      Status: string
      CreatedAt: string
      CalDavEventHref: string option }

type PaginatedBookingsResponse =
    { Bookings: BookingDto list
      TotalCount: int
      Page: int
      PageSize: int }

type DashboardStatsResponse =
    { UpcomingCount: int
      NextBookingTime: string option
      NextBookingTitle: string option }

type CalendarSourceDto =
    { Id: string
      Provider: string
      BaseUrl: string
      LastSyncedAt: string option
      LastSyncResult: string option }

type SyncHistoryEntryDto =
    { Id: string
      SourceId: string
      SyncedAt: string
      Status: string
      ErrorMessage: string option }

type AvailabilitySlotDto =
    { Id: string
      DayOfWeek: int
      StartTime: string
      EndTime: string }

type AvailabilitySlotRequest =
    { DayOfWeek: int
      StartTime: string
      EndTime: string }

type SchedulingSettingsDto =
    { MinNoticeHours: int
      BookingWindowDays: int
      DefaultDurationMinutes: int
      VideoLink: string option }

type SchedulingSettingsRequest =
    { MinNoticeHours: int
      BookingWindowDays: int
      DefaultDurationMinutes: int
      VideoLink: string option }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private odtPattern = Formatting.odtFormatPattern

let private instantPattern = InstantPattern.ExtendedIso
let private localDateTimePattern = LocalDateTimePattern.ExtendedIso

let formatInstantInZone (tz: DateTimeZone) (instant: Instant) : string =
    localDateTimePattern.Format(instant.InZone(tz).LocalDateTime)

let private calendarSourceToDto (cs: CalendarSourceStatus) : CalendarSourceDto =
    { Id = cs.Source.Id.ToString()
      Provider =
        match cs.Source.Provider with
        | Fastmail -> "fastmail"
        | ICloud -> "icloud"
      BaseUrl = cs.Source.BaseUrl
      LastSyncedAt = cs.LastSyncedAt |> Option.map instantPattern.Format
      LastSyncResult = cs.LastSyncResult }

let private availabilityToDto (slot: HostAvailabilitySlot) : AvailabilitySlotDto =
    { Id = slot.Id.ToString()
      DayOfWeek = int slot.DayOfWeek
      StartTime = formatTime slot.StartTime
      EndTime = formatTime slot.EndTime }

let private parseTimeString = tryParseTime

let private bookingToDto (booking: Booking) : BookingDto =
    { Id = booking.Id.ToString()
      ParticipantName = booking.ParticipantName
      ParticipantEmail = booking.ParticipantEmail
      ParticipantPhone = booking.ParticipantPhone
      Title = booking.Title
      Description = booking.Description
      StartTime = odtPattern.Format(booking.StartTime)
      EndTime = odtPattern.Format(booking.EndTime)
      DurationMinutes = booking.DurationMinutes
      Timezone = booking.Timezone
      Status =
        match booking.Status with
        | Confirmed -> "confirmed"
        | Cancelled -> "cancelled"
      CreatedAt = instantPattern.Format(booking.CreatedAt)
      CalDavEventHref = booking.CalDavEventHref }

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

let handleListBookings (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let page =
                match ctx.Request.Query.TryGetValue("page") with
                | true, values ->
                    match Int32.TryParse(values.[0]) with
                    | true, p when p > 0 -> p
                    | _ -> 1
                | _ -> 1

            let pageSize =
                match ctx.Request.Query.TryGetValue("pageSize") with
                | true, values ->
                    match Int32.TryParse(values.[0]) with
                    | true, ps when ps > 0 && ps <= 100 -> ps
                    | _ -> 20
                | _ -> 20

            let statusFilter =
                match ctx.Request.Query.TryGetValue("status") with
                | true, values when not (String.IsNullOrEmpty(values.[0])) ->
                    let s = values.[0].ToLowerInvariant()
                    if s = "confirmed" || s = "cancelled" then Some s else None
                | _ -> None

            use conn = createConn ()
            let (bookings, totalCount) = listBookings conn page pageSize statusFilter

            let response: PaginatedBookingsResponse =
                { Bookings = bookings |> List.map bookingToDto
                  TotalCount = totalCount
                  Page = page
                  PageSize = pageSize }

            return! Response.ofJsonOptions jsonOptions response ctx
        }

let handleGetBooking (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let route = Request.getRoute ctx
            let idStr = route.GetString "id"

            match Guid.TryParse(idStr) with
            | false, _ ->
                ctx.Response.StatusCode <- 400
                return! Response.ofJsonOptions jsonOptions {| Error = "Invalid booking ID." |} ctx
            | true, id ->
                use conn = createConn ()

                match getBookingById conn id with
                | Some booking -> return! Response.ofJsonOptions jsonOptions (bookingToDto booking) ctx
                | None ->
                    ctx.Response.StatusCode <- 404
                    return! Response.ofJsonOptions jsonOptions {| Error = "Booking not found." |} ctx
        }

/// sendFn is injectable so that tests can verify cancelledAt is taken from
/// the clock and that email failures are swallowed correctly.
let handleCancelBooking
    (createConn: unit -> SqliteConnection)
    (clock: IClock)
    (notificationConfig: NotificationConfig option)
    (sendFn: NotificationConfig -> Booking -> bool -> Instant -> Task<Result<unit, string>>)
    (deleteCalDavFn: Booking -> Task<unit>)
    : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let route = Request.getRoute ctx
            let idStr = route.GetString "id"

            match Guid.TryParse(idStr) with
            | false, _ ->
                ctx.Response.StatusCode <- 400
                return! Response.ofJsonOptions jsonOptions {| Error = "Invalid booking ID." |} ctx
            | true, id ->
                use conn = createConn ()

                // Get booking before cancelling (for email)
                let bookingOpt = getBookingById conn id

                match cancelBooking conn id with
                | Ok() ->
                    log().Information("Booking {BookingId} cancelled by admin", id)

                    // Send cancellation email if notification is configured
                    match notificationConfig, bookingOpt with
                    | Some config, Some booking ->
                        let cancelledAt = clock.GetCurrentInstant()
                        let! emailResult = sendFn config booking true cancelledAt

                        match emailResult with
                        | Ok() -> log().Information("Cancellation email sent for booking {BookingId}", id)
                        | Error emailErr ->
                            log()
                                .Warning(
                                    "Failed to send cancellation email for booking {BookingId}: {Error}",
                                    id,
                                    emailErr
                                )
                    | None, _ ->
                        log().Debug("SMTP not configured, skipping cancellation email for booking {BookingId}", id)
                    | _, None -> ()

                    // Delete CalDAV event fire-and-forget
                    match bookingOpt with
                    | Some booking -> deleteCalDavFn booking |> ignore
                    | None -> ()

                    return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
                | Error err ->
                    ctx.Response.StatusCode <- 404
                    return! Response.ofJsonOptions jsonOptions {| Error = err |} ctx
        }

let handleDashboard (createConn: unit -> SqliteConnection) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            use conn = createConn ()
            let now = clock.GetCurrentInstant()
            let upcomingCount = getUpcomingBookingsCount conn now
            let nextBooking = getNextBooking conn now

            let response: DashboardStatsResponse =
                { UpcomingCount = upcomingCount
                  NextBookingTime = nextBooking |> Option.map (fun b -> odtPattern.Format(b.StartTime))
                  NextBookingTitle = nextBooking |> Option.map (fun b -> b.Title) }

            return! Response.ofJsonOptions jsonOptions response ctx
        }

// ---------------------------------------------------------------------------
// Calendar source handlers
// ---------------------------------------------------------------------------

let handleListCalendarSources (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            use conn = createConn ()
            let sources = listCalendarSources conn
            let dtos = sources |> List.map calendarSourceToDto
            return! Response.ofJsonOptions jsonOptions {| Sources = dtos |} ctx
        }

let handleTriggerSync
    (createConn: unit -> SqliteConnection)
    (syncSource: Guid -> System.Threading.Tasks.Task<Result<unit, string>>)
    : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let route = Request.getRoute ctx
            let idStr = route.GetString "id"

            match Guid.TryParse(idStr) with
            | false, _ -> return! badRequest jsonOptions "Invalid calendar source ID." ctx
            | true, id ->
                use conn = createConn ()

                match getCalendarSourceById conn id with
                | None ->
                    ctx.Response.StatusCode <- 404
                    return! Response.ofJsonOptions jsonOptions {| Error = "Calendar source not found." |} ctx
                | Some _ ->
                    match! syncSource id with
                    | Ok() ->
                        log().Information("Manual sync triggered for source {SourceId}", id)
                        return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
                    | Error msg ->
                        log().Warning("Manual sync failed for source {SourceId}: {Error}", id, msg)
                        ctx.Response.StatusCode <- 500
                        return! Response.ofJsonOptions jsonOptions {| Error = msg |} ctx
        }

let private syncHistoryEntryToDto (entry: SyncHistoryEntry) : SyncHistoryEntryDto =
    { Id = entry.Id.ToString()
      SourceId = entry.SourceId.ToString()
      SyncedAt = instantPattern.Format(entry.SyncedAt)
      Status = entry.Status
      ErrorMessage = entry.ErrorMessage }

let handleGetSyncHistory (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let route = Request.getRoute ctx
            let idStr = route.GetString "id"

            match Guid.TryParse(idStr) with
            | false, _ -> return! badRequest jsonOptions "Invalid calendar source ID." ctx
            | true, id ->
                let limit =
                    match ctx.Request.Query.TryGetValue("limit") with
                    | true, values ->
                        match Int32.TryParse(values.[0]) with
                        | true, l when l > 0 && l <= 50 -> l
                        | _ -> 10
                    | _ -> 10

                use conn = createConn ()

                match getCalendarSourceById conn id with
                | None ->
                    ctx.Response.StatusCode <- 404
                    return! Response.ofJsonOptions jsonOptions {| Error = "Calendar source not found." |} ctx
                | Some _ ->
                    let history = getSyncHistory conn id limit
                    let dtos = history |> List.map syncHistoryEntryToDto
                    return! Response.ofJsonOptions jsonOptions {| History = dtos |} ctx
        }

// ---------------------------------------------------------------------------
// Availability handlers
// ---------------------------------------------------------------------------

let handleGetAvailability (createConn: unit -> SqliteConnection) (hostTimezone: string) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            use conn = createConn ()
            let slots = getHostAvailability conn
            let dtos = slots |> List.map availabilityToDto

            return!
                Response.ofJsonOptions
                    jsonOptions
                    {| Slots = dtos
                       Timezone = hostTimezone |}
                    ctx
        }

let handlePutAvailability (createConn: unit -> SqliteConnection) (hostTimezone: string) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            match! tryReadJsonBody<{| Slots: AvailabilitySlotRequest array |}> jsonOptions ctx with
            | Error msg -> return! badRequest jsonOptions msg ctx
            // Defensive: JSON null for non-option arrays can still reach here
            | Ok body when body.Slots = null || body.Slots.Length = 0 ->
                return! badRequest jsonOptions "At least one availability slot is required." ctx
            | Ok body ->
                let validationErrors =
                    body.Slots
                    |> Array.toList
                    |> List.indexed
                    |> List.choose (fun (i, slot) ->
                        if slot.DayOfWeek < 1 || slot.DayOfWeek > 7 then
                            Some $"Slot {i}: dayOfWeek must be between 1 (Monday) and 7 (Sunday)."
                        else
                            match parseTimeString slot.StartTime, parseTimeString slot.EndTime with
                            | None, _ -> Some $"Slot {i}: invalid startTime format (expected HH:MM)."
                            | _, None -> Some $"Slot {i}: invalid endTime format (expected HH:MM)."
                            | Some startT, Some endT ->
                                if startT >= endT then
                                    Some $"Slot {i}: startTime must be before endTime."
                                else
                                    None)

                match validationErrors with
                | _ :: _ -> return! badRequest jsonOptions (String.concat " " validationErrors) ctx
                | [] ->
                    let slots: HostAvailabilitySlot list =
                        body.Slots
                        |> Array.toList
                        |> List.map (fun s ->
                            { Id = Guid.NewGuid()
                              DayOfWeek = enum<IsoDayOfWeek> s.DayOfWeek
                              StartTime = (parseTimeString s.StartTime).Value
                              EndTime = (parseTimeString s.EndTime).Value })

                    use conn = createConn ()

                    match replaceHostAvailability conn slots with
                    | Ok() ->
                        log().Information("Host availability updated ({Count} slots)", slots.Length)
                        let dtos = slots |> List.map availabilityToDto

                        return!
                            Response.ofJsonOptions
                                jsonOptions
                                {| Slots = dtos
                                   Timezone = hostTimezone |}
                                ctx
                    | Error msg ->
                        log().Error("Failed to update availability: {Error}", msg)
                        ctx.Response.StatusCode <- 500
                        return! Response.ofJsonOptions jsonOptions {| Error = "Failed to update availability." |} ctx
        }

// ---------------------------------------------------------------------------
// Settings handlers
// ---------------------------------------------------------------------------

let private settingsToDto (settings: SchedulingSettings) : SchedulingSettingsDto =
    { MinNoticeHours = settings.MinNoticeHours
      BookingWindowDays = settings.BookingWindowDays
      DefaultDurationMinutes = settings.DefaultDurationMinutes
      VideoLink = settings.VideoLink }

let handleGetSettings (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            use conn = createConn ()
            let settings = getSchedulingSettings conn
            return! Response.ofJsonOptions jsonOptions (settingsToDto settings) ctx
        }

let handlePutSettings (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            match! tryReadJsonBody<SchedulingSettingsRequest> jsonOptions ctx with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok req ->
                let validationErrors =
                    [ if req.MinNoticeHours < 0 then
                          "minNoticeHours must be non-negative."
                      if req.BookingWindowDays < 1 then
                          "bookingWindowDays must be at least 1."
                      if req.DefaultDurationMinutes < 5 then
                          "defaultDurationMinutes must be at least 5."
                      if req.DefaultDurationMinutes > 480 then
                          "defaultDurationMinutes must be at most 480 (8 hours)." ]

                match validationErrors with
                | _ :: _ -> return! badRequest jsonOptions (String.concat " " validationErrors) ctx
                | [] ->
                    let settings: SchedulingSettings =
                        { MinNoticeHours = req.MinNoticeHours
                          BookingWindowDays = req.BookingWindowDays
                          DefaultDurationMinutes = req.DefaultDurationMinutes
                          VideoLink = req.VideoLink |> Option.filter (String.IsNullOrWhiteSpace >> not) }

                    use conn = createConn ()

                    match updateSchedulingSettings conn settings with
                    | Ok() ->
                        log()
                            .Information(
                                "Scheduling settings updated: minNotice={MinNotice}h, window={Window}d, duration={Duration}m",
                                settings.MinNoticeHours,
                                settings.BookingWindowDays,
                                settings.DefaultDurationMinutes
                            )

                        return! Response.ofJsonOptions jsonOptions (settingsToDto settings) ctx
                    | Error msg ->
                        log().Error("Failed to update scheduling settings: {Error}", msg)
                        ctx.Response.StatusCode <- 500
                        return! Response.ofJsonOptions jsonOptions {| Error = "Failed to update settings." |} ctx
        }

// ---------------------------------------------------------------------------
// Calendar view handlers
// ---------------------------------------------------------------------------

type CalendarEventDto =
    { Id: string
      Title: string
      Start: string
      End: string
      IsAllDay: bool
      EventType: string } // "calendar" | "booking" | "availability"

let cachedEventToDto (formatTime: Instant -> string) (evt: CachedEvent) : CalendarEventDto =
    { Id = evt.Id.ToString()
      Title = evt.Summary
      Start = formatTime evt.StartInstant
      End = formatTime evt.EndInstant
      IsAllDay = evt.IsAllDay
      EventType = "calendar" }

let bookingToCalendarDto (formatTime: Instant -> string) (b: Booking) : CalendarEventDto =
    { Id = b.Id.ToString()
      Title = $"📅 {b.Title} ({b.ParticipantName})"
      Start = formatTime (b.StartTime.ToInstant())
      End = formatTime (b.EndTime.ToInstant())
      IsAllDay = false
      EventType = "booking" }

let expandAvailabilitySlots
    (hostTz: DateTimeZone)
    (formatTime: Instant -> string)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    (blockedDates: Set<LocalDate>)
    (slots: HostAvailabilitySlot list)
    : CalendarEventDto list =
    let startLocal = rangeStart.InZone(hostTz).LocalDateTime.Date
    let endLocal = rangeEnd.InZone(hostTz).LocalDateTime.Date

    [ let mutable current = startLocal

      while current <= endLocal do
          if not (Set.contains current blockedDates) then
              let dayOfWeek = current.DayOfWeek

              yield!
                  (slots
                   |> List.filter (fun slot -> slot.DayOfWeek = dayOfWeek)
                   |> List.map (fun slot ->
                       let startDt = current.At(slot.StartTime).InZoneLeniently(hostTz)
                       let endDt = current.At(slot.EndTime).InZoneLeniently(hostTz)

                       { Id = $"avail-{current}-{slot.Id}"
                         Title = "Available"
                         Start = formatTime (startDt.ToInstant())
                         End = formatTime (endDt.ToInstant())
                         IsAllDay = false
                         EventType = "availability" }))

          current <- current.PlusDays(1) ]

let buildCalendarViewEvents
    (hostTz: DateTimeZone)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    (cachedEvents: CachedEvent list)
    (bookings: Booking list)
    (availabilitySlots: HostAvailabilitySlot list)
    : CalendarEventDto list =
    let formatTime = formatInstantInZone hostTz

    let calendarEventDtos = cachedEvents |> List.map (cachedEventToDto formatTime)
    let bookingEventDtos = bookings |> List.map (bookingToCalendarDto formatTime)

    // Dates with all-day events are fully blocked — suppress availability.
    let allDayBlockedDates =
        cachedEvents
        |> List.filter (fun e -> e.IsAllDay)
        |> List.map (fun e -> e.StartInstant.InZone(hostTz).Date)
        |> Set.ofList

    let availabilityEventDtos =
        expandAvailabilitySlots hostTz formatTime rangeStart rangeEnd allDayBlockedDates availabilitySlots

    // Availability first so calendar/booking events render on top (later in DOM = higher z-order)
    availabilityEventDtos @ calendarEventDtos @ bookingEventDtos

let handleCalendarView (createConn: unit -> SqliteConnection) (hostTimezone: string) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let parseInstant (s: string) =
                let result = InstantPattern.ExtendedIso.Parse(s)
                if result.Success then Some result.Value else None

            let startParam =
                match ctx.Request.Query.TryGetValue("start") with
                | true, values -> parseInstant values.[0]
                | _ -> None

            let endParam =
                match ctx.Request.Query.TryGetValue("end") with
                | true, values -> parseInstant values.[0]
                | _ -> None

            match startParam, endParam with
            | None, _ -> return! badRequest jsonOptions "Missing or invalid 'start' query parameter (ISO instant)." ctx
            | _, None -> return! badRequest jsonOptions "Missing or invalid 'end' query parameter (ISO instant)." ctx
            | Some rangeStart, Some rangeEnd when rangeStart >= rangeEnd ->
                return! badRequest jsonOptions "'start' must be before 'end'." ctx
            | Some rangeStart, Some rangeEnd ->
                use conn = createConn ()

                let viewTzParam =
                    match ctx.Request.Query.TryGetValue("tz") with
                    | true, values -> Some values.[0]
                    | _ -> None

                let viewTzId = viewTzParam |> Option.defaultValue hostTimezone

                let viewTz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(viewTzId)

                if viewTz = null then
                    return! badRequest jsonOptions $"Invalid timezone: {viewTzId}" ctx
                else
                    let cachedEvents = getCachedEventsInRange conn rangeStart rangeEnd
                    let bookings = getBookingsInRange conn rangeStart rangeEnd
                    let availabilitySlots = getHostAvailability conn

                    let allEvents =
                        buildCalendarViewEvents viewTz rangeStart rangeEnd cachedEvents bookings availabilitySlots

                    return! Response.ofJsonOptions jsonOptions {| Events = allEvents |} ctx
        }
