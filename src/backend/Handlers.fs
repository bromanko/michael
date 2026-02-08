module Michael.Handlers

open System
open System.Net.Http
open System.Security.Cryptography
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
open Michael.CalendarSync
open Michael.HttpHelpers

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
// CSRF
// ---------------------------------------------------------------------------

type CsrfConfig =
    { SigningKey: byte[]
      Lifetime: Duration
      AlwaysSecureCookie: bool }

let private csrfCookieName = "michael_csrf"

let private fixedTimeEquals (a: string) (b: string) =
    if a.Length <> b.Length then
        false
    else
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b)
        )

let private isHexString (s: string) =
    not (String.IsNullOrWhiteSpace(s))
    && s
       |> Seq.forall (fun c -> Char.IsDigit(c) || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))

let private getUserAgent (ctx: HttpContext) =
    match ctx.Request.Headers.TryGetValue("User-Agent") with
    | true, values when values.Count > 0 -> string values.[0]
    | _ -> ""

let private signCsrfPayload (signingKey: byte[]) (payload: string) =
    use hmac = new HMACSHA256(signingKey)

    payload
    |> System.Text.Encoding.UTF8.GetBytes
    |> hmac.ComputeHash
    |> Convert.ToHexString

let private makeCsrfToken (config: CsrfConfig) (issuedAtUnixSeconds: int64) (userAgent: string) =
    let nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
    let payload = $"{issuedAtUnixSeconds}:{nonce}"
    let signature = signCsrfPayload config.SigningKey $"{payload}:{userAgent}"
    $"{payload}:{signature}"

let private validateCsrfToken (config: CsrfConfig) (now: Instant) (userAgent: string) (token: string) =
    let parts = token.Split(':')

    if parts.Length <> 3 then
        false
    else
        let issuedAtText = parts.[0]
        let nonce = parts.[1]
        let signature = parts.[2]

        match Int64.TryParse(issuedAtText) with
        | false, _ -> false
        | true, issuedAtUnixSeconds ->
            let issuedAt = Instant.FromUnixTimeSeconds(issuedAtUnixSeconds)
            let age = now - issuedAt

            let withinLifetime = age >= Duration.Zero && age <= config.Lifetime

            let payload = $"{issuedAtText}:{nonce}"
            let expectedSignature = signCsrfPayload config.SigningKey $"{payload}:{userAgent}"

            withinLifetime
            && nonce.Length = 32
            && isHexString nonce
            && signature.Length = 64
            && isHexString signature
            && fixedTimeEquals signature expectedSignature

let handleCsrfToken (config: CsrfConfig) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx
            let now = clock.GetCurrentInstant()
            let userAgent = getUserAgent ctx

            let token =
                match ctx.Request.Cookies.TryGetValue(csrfCookieName) with
                | true, existing when validateCsrfToken config now userAgent existing -> existing
                | _ -> makeCsrfToken config (now.ToUnixTimeSeconds()) userAgent

            let cookieOptions =
                CookieOptions(
                    Path = "/",
                    HttpOnly = false,
                    SameSite = SameSiteMode.Strict,
                    Secure = (config.AlwaysSecureCookie || ctx.Request.IsHttps),
                    IsEssential = true,
                    MaxAge = Nullable(config.Lifetime.ToTimeSpan())
                )

            ctx.Response.Cookies.Append(csrfCookieName, token, cookieOptions)
            return! Response.ofJsonOptions jsonOptions {| Ok = true; Token = token |} ctx
        }

let requireCsrfToken (config: CsrfConfig) (clock: IClock) (next: HttpHandler) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx
            let now = clock.GetCurrentInstant()
            let userAgent = getUserAgent ctx

            let providedToken =
                match ctx.Request.Headers.TryGetValue("X-CSRF-Token") with
                | true, values when values.Count > 0 -> Some(string values.[0])
                | _ -> None

            let cookieToken =
                match ctx.Request.Cookies.TryGetValue(csrfCookieName) with
                | true, token when not (String.IsNullOrWhiteSpace(token)) -> Some token
                | _ -> None

            match providedToken, cookieToken with
            | Some headerToken, Some cookie when
                fixedTimeEquals headerToken cookie
                && validateCsrfToken config now userAgent headerToken
                ->
                return! next ctx
            | _ ->
                log().Warning("Rejected request due to CSRF validation failure.")
                ctx.Response.StatusCode <- 403
                return! Response.ofJsonOptions jsonOptions {| Error = "Forbidden." |} ctx
        }

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

let private isValidDurationMinutes (minutes: int) = minutes >= 5 && minutes <= 480

let private isWithinSchedulingWindow (now: Instant) (settings: SchedulingSettings) (slotStart: Instant) =
    let minAllowedStart = now + Duration.FromHours(settings.MinNoticeHours)
    let maxAllowedStart = now + Duration.FromDays(float settings.BookingWindowDays)
    slotStart >= minAllowedStart && slotStart <= maxAllowedStart

let private slotUnavailable (jsonOptions: JsonSerializerOptions) (ctx: HttpContext) =
    task {
        ctx.Response.StatusCode <- 409

        return!
            Response.ofJsonOptions
                jsonOptions
                {| Error = "Selected slot is no longer available."
                   Code = "slot_unavailable" |}
                ctx
    }

let tryParseOdt (field: string) (value: string) : Result<OffsetDateTime, string> =
    let parseResult = odtPattern.Parse(value)

    if parseResult.Success then
        Ok parseResult.Value
    else
        Error $"Invalid datetime format for {field}: '{value}'. Expected ISO-8601 with offset."

let tryResolveTimezone (tzId: string) : Result<DateTimeZone, string> =
    match DateTimeZoneProviders.Tzdb.GetZoneOrNull(tzId) with
    | null -> Error $"Unrecognized timezone: '{tzId}'."
    | z -> Ok z

let isValidEmail (email: string) =
    if String.IsNullOrWhiteSpace(email) then
        false
    else
        match email.Split('@') with
        | [| local; domain |] -> local.Length > 0 && domain.Contains('.') && not (domain.EndsWith('.'))
        | _ -> false

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

let handleParse (httpClient: HttpClient) (geminiConfig: GeminiConfig) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

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

                        let now = clock.GetCurrentInstant().InZone(tz)

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

let handleSlots (createConn: unit -> SqliteConnection) (hostTz: DateTimeZone) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let! bodyResult = tryReadJsonBody<SlotsRequest> jsonOptions ctx

            match bodyResult with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok body ->

                if body.AvailabilityWindows = null || body.AvailabilityWindows.Length = 0 then
                    return! badRequest jsonOptions "At least one availability window is required." ctx
                elif not (isValidDurationMinutes body.DurationMinutes) then
                    return! badRequest jsonOptions "DurationMinutes must be between 5 and 480." ctx
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
                            let calendarBlockers = getCachedBlockers createConn rangeStart rangeEnd

                            let settings = Database.getSchedulingSettings conn
                            let now = clock.GetCurrentInstant()

                            let slots =
                                computeSlots
                                    windows
                                    hostSlots
                                    hostTz
                                    existingBookings
                                    calendarBlockers
                                    body.DurationMinutes
                                    body.Timezone
                                |> List.filter (fun s ->
                                    isWithinSchedulingWindow now settings (s.SlotStart.ToInstant()))

                            let response: SlotsResponse =
                                { Slots =
                                    slots
                                    |> List.map (fun s ->
                                        { TimeSlotDto.Start = odtPattern.Format(s.SlotStart)
                                          End = odtPattern.Format(s.SlotEnd) }) }

                            return! Response.ofJsonOptions jsonOptions response ctx
        }

let handleBook (createConn: unit -> SqliteConnection) (hostTz: DateTimeZone) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

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
                elif not (isValidDurationMinutes body.DurationMinutes) then
                    return! badRequest jsonOptions "DurationMinutes must be between 5 and 480." ctx
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

                            let slotStartInstant = slotStart.ToInstant()
                            let slotEndInstant = slotEnd.ToInstant()
                            let requestedDuration = slotEndInstant - slotStartInstant

                            if not (Instant.op_LessThan (slotStartInstant, slotEndInstant)) then
                                return! badRequest jsonOptions "Slot.End must be after Slot.Start." ctx
                            elif requestedDuration <> Duration.FromMinutes(int64 body.DurationMinutes) then
                                return! badRequest jsonOptions "Slot duration must match DurationMinutes." ctx
                            else
                                use conn = createConn ()

                                let settings = Database.getSchedulingSettings conn
                                let hostSlots = Database.getHostAvailability conn

                                let existingBookings =
                                    Database.getBookingsInRange conn slotStartInstant slotEndInstant

                                let calendarBlockers = getCachedBlockers createConn slotStartInstant slotEndInstant
                                let now = clock.GetCurrentInstant()

                                let requestedWindow: AvailabilityWindow =
                                    { Start = slotStart
                                      End = slotEnd
                                      Timezone = Some body.Timezone }

                                let slotStillAvailable =
                                    computeSlots
                                        [ requestedWindow ]
                                        hostSlots
                                        hostTz
                                        existingBookings
                                        calendarBlockers
                                        body.DurationMinutes
                                        body.Timezone
                                    |> List.filter (fun s ->
                                        isWithinSchedulingWindow now settings (s.SlotStart.ToInstant()))
                                    |> List.exists (fun s ->
                                        s.SlotStart.ToInstant() = slotStartInstant
                                        && s.SlotEnd.ToInstant() = slotEndInstant)

                                if not slotStillAvailable then
                                    log()
                                        .Information(
                                            "Booking rejected due to stale/invalid slot for {ParticipantEmail}",
                                            body.Email
                                        )

                                    return! slotUnavailable jsonOptions ctx
                                else
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
                                          CreatedAt = now }

                                    match Database.insertBookingIfSlotAvailable conn booking with
                                    | Ok true ->
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
                                    | Ok false ->
                                        log()
                                            .Information(
                                                "Booking rejected due to slot conflict for {ParticipantEmail}",
                                                body.Email
                                            )

                                        return! slotUnavailable jsonOptions ctx
                                    | Error err ->
                                        log().Error("Booking insertion failed: {Error}", err)
                                        ctx.Response.StatusCode <- 500

                                        return!
                                            Response.ofJsonOptions
                                                jsonOptions
                                                {| Error = "An internal error occurred." |}
                                                ctx
        }
