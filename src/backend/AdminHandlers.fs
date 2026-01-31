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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private odtPattern = OffsetDateTimePattern.ExtendedIso
let private instantPattern = InstantPattern.ExtendedIso

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
                | Some booking ->
                    return! Response.ofJsonOptions jsonOptions (bookingToDto booking) ctx
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
                | Ok () ->
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
                  NextBookingTime =
                    nextBooking |> Option.map (fun b -> odtPattern.Format(b.StartTime))
                  NextBookingTitle =
                    nextBooking |> Option.map (fun b -> b.Title) }

            return! Response.ofJsonOptions jsonOptions response ctx
        }
