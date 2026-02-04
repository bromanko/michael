module Michael.AdminHandlers

open System
open System.Text.Json
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open NodaTime.Text
open Serilog
open Michael.Domain
open Michael.Database
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
      CreatedAt: string }

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

type AvailabilitySlotDto =
    { Id: string
      DayOfWeek: int
      StartTime: string
      EndTime: string
      Timezone: string }

[<CLIMutable>]
type AvailabilitySlotRequest =
    { DayOfWeek: int
      StartTime: string
      EndTime: string
      Timezone: string }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private odtPattern = OffsetDateTimePattern.ExtendedIso
let private instantPattern = InstantPattern.ExtendedIso

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
      StartTime = sprintf "%02d:%02d" slot.StartTime.Hour slot.StartTime.Minute
      EndTime = sprintf "%02d:%02d" slot.EndTime.Hour slot.EndTime.Minute
      Timezone = slot.Timezone }

let private parseTimeString (s: string) : LocalTime option =
    let parts = s.Split(':')

    if parts.Length = 2 then
        match Int32.TryParse(parts.[0]), Int32.TryParse(parts.[1]) with
        | (true, h), (true, m) when h >= 0 && h <= 23 && m >= 0 && m <= 59 -> Some(LocalTime(h, m))
        | _ -> None
    else
        None

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
      CreatedAt = instantPattern.Format(booking.CreatedAt) }

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

let handleListBookings (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

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
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

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

let handleCancelBooking (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            let route = Request.getRoute ctx
            let idStr = route.GetString "id"

            match Guid.TryParse(idStr) with
            | false, _ ->
                ctx.Response.StatusCode <- 400
                return! Response.ofJsonOptions jsonOptions {| Error = "Invalid booking ID." |} ctx
            | true, id ->
                use conn = createConn ()

                match cancelBooking conn id with
                | Ok() ->
                    log().Information("Booking {BookingId} cancelled by admin", id)
                    return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
                | Error err ->
                    ctx.Response.StatusCode <- 404
                    return! Response.ofJsonOptions jsonOptions {| Error = err |} ctx
        }

let handleDashboard (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            use conn = createConn ()
            let now = SystemClock.Instance.GetCurrentInstant()
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
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

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
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

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

// ---------------------------------------------------------------------------
// Availability handlers
// ---------------------------------------------------------------------------

let handleGetAvailability (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            use conn = createConn ()
            let slots = getHostAvailability conn
            let dtos = slots |> List.map availabilityToDto
            return! Response.ofJsonOptions jsonOptions {| Slots = dtos |} ctx
        }

let handlePutAvailability (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            match! tryReadJsonBody<{| Slots: AvailabilitySlotRequest array |}> jsonOptions ctx with
            | Error msg -> return! badRequest jsonOptions msg ctx
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
                        elif String.IsNullOrWhiteSpace(slot.Timezone) then
                            Some $"Slot {i}: timezone is required."
                        elif DateTimeZoneProviders.Tzdb.GetZoneOrNull(slot.Timezone) = null then
                            Some $"Slot {i}: unknown timezone '{slot.Timezone}'."
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
                | err :: _ -> return! badRequest jsonOptions err ctx
                | [] ->
                    let slots: HostAvailabilitySlot list =
                        body.Slots
                        |> Array.toList
                        |> List.map (fun s ->
                            { Id = Guid.NewGuid()
                              DayOfWeek = enum<IsoDayOfWeek> s.DayOfWeek
                              StartTime = (parseTimeString s.StartTime).Value
                              EndTime = (parseTimeString s.EndTime).Value
                              Timezone = s.Timezone })

                    use conn = createConn ()

                    match replaceHostAvailability conn slots with
                    | Ok() ->
                        log().Information("Host availability updated ({Count} slots)", slots.Length)
                        let dtos = slots |> List.map availabilityToDto
                        return! Response.ofJsonOptions jsonOptions {| Slots = dtos |} ctx
                    | Error msg ->
                        log().Error("Failed to update availability: {Error}", msg)
                        ctx.Response.StatusCode <- 500
                        return! Response.ofJsonOptions jsonOptions {| Error = "Failed to update availability." |} ctx
        }
