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
open Serilog
open Michael.GeminiClient
open Michael.Availability

let private log () =
    Log.ForContext("SourceContext", "Michael.Handlers")

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

type BookResponse = { BookingId: string; Confirmed: bool }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private buildSystemMessage (result: ParseResult) : string =
    let parts =
        [ if result.AvailabilityWindows.Length > 0 then
              let pattern = OffsetDateTimePattern.CreateWithInvariantCulture("ddd MMM d, h:mm tt")

              let windowStrs =
                  result.AvailabilityWindows
                  |> List.map (fun w -> $"{pattern.Format(w.Start)} - {pattern.Format(w.End)}")

              let windowList = String.Join("; ", windowStrs)
              yield $"I understood you are available: {windowList}."

          match result.Description with
          | Some desc -> yield desc
          | None -> ()

          match result.DurationMinutes with
          | Some d -> yield $"Duration: {d} minutes."
          | None -> ()

          match result.Title with
          | Some t -> yield $"Topic: {t}."
          | None -> ()

          match result.Name with
          | Some n -> yield $"Name: {n}."
          | None -> ()

          match result.Email with
          | Some e -> yield $"Email: {e}."
          | None -> ()

          if result.MissingFields.Length > 0 then
              let missing = String.Join(", ", result.MissingFields)
              yield $"I still need: {missing}." ]

    String.Join(" ", parts)

let private odtPattern = OffsetDateTimePattern.ExtendedIso

let private badRequest (jsonOptions: JsonSerializerOptions) (message: string) (ctx: HttpContext) =
    task {
        ctx.Response.StatusCode <- 400
        return! Response.ofJsonOptions jsonOptions {| Error = message |} ctx
    }

let private tryReadJsonBody<'T when 'T: not struct> (jsonOptions: JsonSerializerOptions) (ctx: HttpContext) =
    task {
        try
            let! body = ctx.Request.ReadFromJsonAsync<'T>(jsonOptions)

            if Object.ReferenceEquals(body, null) then
                return Error "Request body is required."
            else
                return Ok body
        with :? JsonException as ex ->
            log().Warning("Malformed JSON in request body: {Error}", ex.Message)
            return Error "Request body contains malformed JSON."
    }

let private conflict (message: string) (ctx: HttpContext) =
    task {
        ctx.Response.StatusCode <- 409
        return! Response.ofJson {| Error = message |} ctx
    }

let private tryParseOdt (field: string) (value: string) : Result<OffsetDateTime, string> =
    let parseResult = odtPattern.Parse(value)

    if parseResult.Success then
        Ok parseResult.Value
    else
        Error $"Invalid datetime format for {field}: '{value}'. Expected ISO-8601 with offset."

let private tryResolveTimezone (tzId: string) : Result<DateTimeZone, string> =
    match DateTimeZoneProviders.Tzdb.GetZoneOrNull(tzId) with
    | null -> Error $"Unrecognized timezone: '{tzId}'."
    | z -> Ok z

let private isValidEmail (email: string) =
    if String.IsNullOrWhiteSpace(email) then
        false
    else
        match email.Split('@') with
        | [| local; domain |] -> local.Length > 0 && domain.Contains('.') && not (domain.EndsWith('.'))
        | _ -> false

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

let handleParse (httpClient: HttpClient) (geminiConfig: GeminiConfig) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            let! bodyResult = tryReadJsonBody<ParseRequest> jsonOptions ctx

            match bodyResult with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok body ->

                if String.IsNullOrWhiteSpace(body.Message) then
                    return! badRequest jsonOptions "Message is required." ctx
                elif String.IsNullOrWhiteSpace(body.Timezone) then
                    return! badRequest jsonOptions "Timezone is required." ctx
                else

                    match tryResolveTimezone body.Timezone with
                    | Error msg -> return! badRequest jsonOptions msg ctx
                    | Ok tz ->

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

                            return! Response.ofJsonOptions jsonOptions response ctx
                        | Error err ->
                            log().Error("Parse request failed: {Error}", err)
                            ctx.Response.StatusCode <- 500
                            return! Response.ofJsonOptions jsonOptions {| Error = "An internal error occurred." |} ctx
        }

let handleSlots (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            let! bodyResult = tryReadJsonBody<SlotsRequest> jsonOptions ctx

            match bodyResult with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok body ->

                if body.AvailabilityWindows = null || body.AvailabilityWindows.Length = 0 then
                    return! badRequest jsonOptions "At least one availability window is required." ctx
                elif body.DurationMinutes <= 0 then
                    return! badRequest jsonOptions "DurationMinutes must be positive." ctx
                elif String.IsNullOrWhiteSpace(body.Timezone) then
                    return! badRequest jsonOptions "Timezone is required." ctx
                else

                    match tryResolveTimezone body.Timezone with
                    | Error msg -> return! badRequest jsonOptions msg ctx
                    | Ok _ ->

                        let windowResults =
                            body.AvailabilityWindows
                            |> Array.toList
                            |> List.mapi (fun i w ->
                                match
                                    tryParseOdt $"AvailabilityWindows[{i}].Start" w.Start,
                                    tryParseOdt $"AvailabilityWindows[{i}].End" w.End
                                with
                                | Ok s, Ok e ->
                                    Ok
                                        { Domain.AvailabilityWindow.Start = s
                                          End = e
                                          Timezone =
                                            if String.IsNullOrEmpty(w.Timezone) then
                                                None
                                            else
                                                Some w.Timezone }
                                | Error msg, _ -> Error msg
                                | _, Error msg -> Error msg)

                        let firstError =
                            windowResults
                            |> List.tryPick (fun r ->
                                match r with
                                | Error msg -> Some msg
                                | _ -> None)

                        match firstError with
                        | Some msg -> return! badRequest jsonOptions msg ctx
                        | None ->

                            let windows =
                                windowResults
                                |> List.map (fun r ->
                                    match r with
                                    | Ok w -> w
                                    | Error _ -> failwith "unreachable")

                            use conn = createConn ()

                            let hostSlots = Database.getHostAvailability conn

                            let rangeStart =
                                windows
                                |> List.minBy (fun w -> w.Start.ToInstant().ToUnixTimeTicks())
                                |> fun w -> w.Start.ToInstant()

                            let rangeEnd =
                                windows
                                |> List.maxBy (fun w -> w.End.ToInstant().ToUnixTimeTicks())
                                |> fun w -> w.End.ToInstant()

                            let existingBookings = Database.getBookingsInRange conn rangeStart rangeEnd

                            let slots =
                                computeSlots windows hostSlots existingBookings [] body.DurationMinutes body.Timezone

                            let response: SlotsResponse =
                                { Slots =
                                    slots
                                    |> List.map (fun s ->
                                        { TimeSlotDto.Start = odtPattern.Format(s.SlotStart)
                                          End = odtPattern.Format(s.SlotEnd) }) }

                            return! Response.ofJsonOptions jsonOptions response ctx
        }

let handleBook (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            let! bodyResult = tryReadJsonBody<BookRequest> jsonOptions ctx

            match bodyResult with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok body ->

                if String.IsNullOrWhiteSpace(body.Name) then
                    return! badRequest jsonOptions "Name is required." ctx
                elif not (isValidEmail body.Email) then
                    return! badRequest jsonOptions "A valid email address is required." ctx
                elif String.IsNullOrWhiteSpace(body.Title) then
                    return! badRequest jsonOptions "Title is required." ctx
                elif body.DurationMinutes <= 0 then
                    return! badRequest jsonOptions "DurationMinutes must be positive." ctx
                elif String.IsNullOrWhiteSpace(body.Timezone) then
                    return! badRequest jsonOptions "Timezone is required." ctx
                else

                    match tryResolveTimezone body.Timezone with
                    | Error msg -> return! badRequest jsonOptions msg ctx
                    | Ok _ ->

                        match tryParseOdt "Slot.Start" body.Slot.Start, tryParseOdt "Slot.End" body.Slot.End with
                        | Error msg, _ -> return! badRequest jsonOptions msg ctx
                        | _, Error msg -> return! badRequest jsonOptions msg ctx
                        | Ok slotStart, Ok slotEnd ->

                            let bookingId = Guid.NewGuid()

                            let booking: Booking =
                                { Id = bookingId
                                  ParticipantName = body.Name
                                  ParticipantEmail = body.Email
                                  ParticipantPhone =
                                    if String.IsNullOrEmpty(body.Phone) then
                                        None
                                    else
                                        Some body.Phone
                                  Title = body.Title
                                  Description =
                                    if String.IsNullOrEmpty(body.Description) then
                                        None
                                    else
                                        Some body.Description
                                  StartTime = slotStart
                                  EndTime = slotEnd
                                  DurationMinutes = body.DurationMinutes
                                  Timezone = body.Timezone
                                  Status = Confirmed
                                  CreatedAt = SystemClock.Instance.GetCurrentInstant() }

                            use conn = createConn ()

                            match Database.insertBooking conn booking with
                            | Ok() ->
                                log()
                                    .Information(
                                        "Booking created {BookingId} for {ParticipantEmail}",
                                        bookingId,
                                        body.Email
                                    )

                                let response =
                                    { BookingId = bookingId.ToString()
                                      Confirmed = true }

                                return! Response.ofJsonOptions jsonOptions response ctx
                            | Error err ->
                                log().Error("Booking insertion failed: {Error}", err)
                                ctx.Response.StatusCode <- 500

                                return!
                                    Response.ofJsonOptions jsonOptions {| Error = "An internal error occurred." |} ctx
        }
