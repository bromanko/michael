module Michael.Handlers

open System
open System.Net.Http
open System.Text.Json
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.GeminiClient
open Michael.Availability

// ---------------------------------------------------------------------------
// Request/Response DTOs
// ---------------------------------------------------------------------------

[<CLIMutable>]
type ParseRequest =
    { Message: string
      Timezone: string
      PreviousMessages: string array }

type ParseResponse =
    { ParseResult: ParseResult
      SystemMessage: string }

[<CLIMutable>]
type SlotsRequest =
    { AvailabilityWindows: AvailabilityWindowDto array
      DurationMinutes: int
      Timezone: string }

and [<CLIMutable>] AvailabilityWindowDto =
    { Start: string
      End: string
      Timezone: string }

type SlotsResponse = { Slots: TimeSlotDto list }

and TimeSlotDto = { Start: string; End: string }

[<CLIMutable>]
type BookRequest =
    { Name: string
      Email: string
      Phone: string
      Title: string
      Description: string
      Slot: TimeSlotDto
      DurationMinutes: int
      Timezone: string }

type BookResponse =
    { BookingId: string
      Confirmed: bool }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private buildSystemMessage (result: ParseResult) : string =
    let parts = ResizeArray<string>()

    if result.AvailabilityWindows.Length > 0 then
        let pattern = OffsetDateTimePattern.CreateWithInvariantCulture("ddd MMM d, h:mm tt")

        let windowStrs =
            result.AvailabilityWindows
            |> List.map (fun w ->
                $"{pattern.Format(w.Start)} - {pattern.Format(w.End)}")

        let windowList = String.Join("; ", windowStrs)
        parts.Add($"I understood you are available: {windowList}.")

    match result.Description with
    | Some desc -> parts.Add(desc)
    | None -> ()

    match result.DurationMinutes with
    | Some d -> parts.Add($"Duration: {d} minutes.")
    | None -> ()

    match result.Title with
    | Some t -> parts.Add($"Topic: {t}.")
    | None -> ()

    match result.Name with
    | Some n -> parts.Add($"Name: {n}.")
    | None -> ()

    match result.Email with
    | Some e -> parts.Add($"Email: {e}.")
    | None -> ()

    if result.MissingFields.Length > 0 then
        let missing = String.Join(", ", result.MissingFields)
        parts.Add($"I still need: {missing}.")

    String.Join(" ", parts)

let private odtPattern = OffsetDateTimePattern.ExtendedIso

let private badRequest (message: string) (ctx: HttpContext) =
    task {
        ctx.Response.StatusCode <- 400
        return! Response.ofJson {| Error = message |} ctx
    }

let private isValidEmail (email: string) =
    if String.IsNullOrWhiteSpace(email) then false
    else
        match email.Split('@') with
        | [| local; domain |] ->
            local.Length > 0
            && domain.Contains('.')
            && not (domain.EndsWith('.'))
        | _ -> false

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

let handleParse
    (httpClient: HttpClient)
    (geminiConfig: GeminiConfig)
    : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions
            let! body = ctx.Request.ReadFromJsonAsync<ParseRequest>(jsonOptions)

            if String.IsNullOrWhiteSpace(body.Message) then
                return! badRequest "Message is required." ctx
            else

            let tzId =
                if String.IsNullOrEmpty(body.Timezone) then
                    "America/New_York"
                else
                    body.Timezone

            let tz =
                match DateTimeZoneProviders.Tzdb.GetZoneOrNull(tzId) with
                | null -> DateTimeZoneProviders.Tzdb.["America/New_York"]
                | z -> z
            let now = SystemClock.Instance.GetCurrentInstant().InZone(tz)

            // Concatenate previous messages with current
            let allMessages =
                if body.PreviousMessages <> null && body.PreviousMessages.Length > 0 then
                    let prev = String.Join("\n", body.PreviousMessages)
                    $"{prev}\n{body.Message}"
                else
                    body.Message

            let! result = parseInput httpClient geminiConfig allMessages now

            match result with
            | Ok parseResult ->
                let response =
                    { ParseResult = parseResult
                      SystemMessage = buildSystemMessage parseResult }

                return! Response.ofJson response ctx
            | Error err ->
                ctx.Response.StatusCode <- 500
                return! Response.ofJson {| Error = err |} ctx
        }

let handleSlots
    (createConn: unit -> SqliteConnection)
    : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions
            let! body = ctx.Request.ReadFromJsonAsync<SlotsRequest>(jsonOptions)

            if body.AvailabilityWindows = null || body.AvailabilityWindows.Length = 0 then
                return! badRequest "At least one availability window is required." ctx
            elif body.DurationMinutes <= 0 then
                return! badRequest "DurationMinutes must be positive." ctx
            elif String.IsNullOrWhiteSpace(body.Timezone) then
                return! badRequest "Timezone is required." ctx
            else

            let windows : Domain.AvailabilityWindow list =
                body.AvailabilityWindows
                |> Array.toList
                |> List.map (fun w ->
                    { Domain.AvailabilityWindow.Start = odtPattern.Parse(w.Start).Value
                      End = odtPattern.Parse(w.End).Value
                      Timezone =
                          if String.IsNullOrEmpty(w.Timezone) then None
                          else Some w.Timezone })

            use conn = createConn ()

            let hostSlots = Database.getHostAvailability conn

            let rangeStart =
                windows
                |> List.minBy (fun w -> w.Start.ToInstant().ToUnixTimeTicks())
                |> fun w -> w.Start

            let rangeEnd =
                windows
                |> List.maxBy (fun w -> w.End.ToInstant().ToUnixTimeTicks())
                |> fun w -> w.End

            let existingBookings =
                Database.getBookingsInRange conn rangeStart rangeEnd

            let slots =
                computeSlots windows hostSlots existingBookings body.DurationMinutes body.Timezone

            let response : SlotsResponse =
                { Slots =
                    slots
                    |> List.map (fun s ->
                        { TimeSlotDto.Start = odtPattern.Format(s.SlotStart)
                          End = odtPattern.Format(s.SlotEnd) }) }

            return! Response.ofJson response ctx
        }

let handleBook
    (createConn: unit -> SqliteConnection)
    : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions
            let! body = ctx.Request.ReadFromJsonAsync<BookRequest>(jsonOptions)

            if String.IsNullOrWhiteSpace(body.Name) then
                return! badRequest "Name is required." ctx
            elif not (isValidEmail body.Email) then
                return! badRequest "A valid email address is required." ctx
            elif String.IsNullOrWhiteSpace(body.Title) then
                return! badRequest "Title is required." ctx
            elif body.DurationMinutes <= 0 then
                return! badRequest "DurationMinutes must be positive." ctx
            elif String.IsNullOrWhiteSpace(body.Timezone) then
                return! badRequest "Timezone is required." ctx
            else

            let bookingId = Guid.NewGuid()

            let booking: Booking =
                { Id = bookingId
                  ParticipantName = body.Name
                  ParticipantEmail = body.Email
                  ParticipantPhone =
                      if String.IsNullOrEmpty(body.Phone) then None
                      else Some body.Phone
                  Title = body.Title
                  Description =
                      if String.IsNullOrEmpty(body.Description) then None
                      else Some body.Description
                  StartTime = odtPattern.Parse(body.Slot.Start).Value
                  EndTime = odtPattern.Parse(body.Slot.End).Value
                  DurationMinutes = body.DurationMinutes
                  Timezone = body.Timezone
                  Status = Confirmed
                  CreatedAt = SystemClock.Instance.GetCurrentInstant() }

            use conn = createConn ()

            match Database.insertBooking conn booking with
            | Ok () ->
                let response =
                    { BookingId = bookingId.ToString()
                      Confirmed = true }

                return! Response.ofJson response ctx
            | Error err ->
                ctx.Response.StatusCode <- 500
                return! Response.ofJson {| Error = err |} ctx
        }
