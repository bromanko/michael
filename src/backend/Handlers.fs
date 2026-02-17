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

/// Rate limit handler combinator. Rejects requests exceeding the named
/// policy with 429 Too Many Requests.
let requireRateLimit (policyName: string) (next: HttpHandler) : HttpHandler =
    fun ctx ->
        task {
            if RateLimiting.tryAcquire policyName ctx then
                return! next ctx
            else
                let jsonOptions = getJsonOptions ctx
                ctx.Response.StatusCode <- 429
                return! Response.ofJsonOptions jsonOptions {| Error = "Too many requests." |} ctx
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

/// Strict formatting — see Formatting.odtFormatPattern.
let private odtFormatPattern = Formatting.odtFormatPattern

/// Lenient parsing — accepts full and shortened offsets (e.g. "-08" and
/// "-08:00") and fractional seconds. Fractional seconds are accepted on
/// input but dropped on output (the format pattern has second precision).
let private odtParsePattern = OffsetDateTimePattern.ExtendedIso

let isValidDurationMinutes (minutes: int) = minutes >= 5 && minutes <= 480

/// Maximum number of availability windows per /api/slots request.
let private maxAvailabilityWindows = 50

/// Maximum number of previous messages per /api/parse request.
let private maxPreviousMessages = 20

/// Maximum character length for the current message in /api/parse.
let private maxMessageLength = 2000

/// Maximum character length for each previous message in /api/parse.
let private maxPreviousMessageLength = 2000

/// Maximum total character length for all messages combined (previous + current)
/// in /api/parse. Limits LLM token consumption.
let private maxTotalParseInputLength = 20_000

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

/// Maximum length for user-supplied values echoed in error messages.
/// Prevents oversized error responses from very long invalid inputs.
let private maxEchoLength = 80

/// Truncate a value for safe inclusion in error messages.
let private echoValue (s: string) : string =
    if isNull s then ""
    elif s.Length <= maxEchoLength then s
    else s.Substring(0, maxEchoLength) + "…"

let tryParseOdt (field: string) (value: string) : Result<OffsetDateTime, string> =
    let parseResult = odtParsePattern.Parse(value)

    if parseResult.Success then
        Ok parseResult.Value
    else
        Error $"Invalid datetime format for {field}: '{echoValue value}'. Expected ISO-8601 with offset."

let tryResolveTimezone (tzId: string) : Result<DateTimeZone, string> =
    match DateTimeZoneProviders.Tzdb.GetZoneOrNull(tzId) with
    | null -> Error $"Unrecognized timezone: '{echoValue tzId}'."
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

/// Sanitize LLM-generated text fields. The model output is untrusted and
/// may contain control characters or excessive length that should not be
/// forwarded to clients.
let private sanitizeParseResult (result: ParseResult) : ParseResult =
    { result with
        Title = result.Title |> Option.map Sanitize.sanitizeTitle
        Description = result.Description |> Option.map Sanitize.sanitizeDescription
        Name = result.Name |> Option.map Sanitize.sanitizeName
        Email = result.Email |> Option.map Sanitize.sanitizeEmail
        Phone = result.Phone |> Option.map Sanitize.sanitizePhone }

let handleParse (httpClient: HttpClient) (geminiConfig: GeminiConfig) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let! bodyResult = tryReadJsonBody<ParseRequest> jsonOptions ctx

            match bodyResult with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok raw ->

                let body =
                    { raw with
                        Message = Sanitize.stripControlChars raw.Message |> fun s -> s.Trim()
                        Timezone = Sanitize.stripControlChars raw.Timezone |> fun s -> s.Trim() }

                if String.IsNullOrWhiteSpace(body.Message) then
                    return! badRequest jsonOptions "Message is required." ctx
                elif body.Message.Length > maxMessageLength then
                    return! badRequest jsonOptions $"Message is too long (max {maxMessageLength} characters)." ctx
                elif String.IsNullOrWhiteSpace(body.Timezone) then
                    return! badRequest jsonOptions "Timezone is required." ctx
                elif
                    body.PreviousMessages <> null
                    && body.PreviousMessages.Length > maxPreviousMessages
                then
                    return! badRequest jsonOptions $"Too many previous messages (max {maxPreviousMessages})." ctx
                elif
                    body.PreviousMessages <> null
                    && body.PreviousMessages
                       |> Array.exists (fun m -> m <> null && m.Length > maxPreviousMessageLength)
                then
                    return!
                        badRequest
                            jsonOptions
                            $"Individual previous message is too long (max {maxPreviousMessageLength} characters)."
                            ctx
                else

                    match tryResolveTimezone body.Timezone with
                    | Error msg -> return! badRequest jsonOptions msg ctx
                    | Ok tz ->

                        let now = clock.GetCurrentInstant().InZone(tz)

                        // Sanitize previous messages and concatenate with current
                        let cleanPrevious =
                            if body.PreviousMessages <> null then
                                body.PreviousMessages
                                |> Array.map (fun m ->
                                    if isNull m then
                                        ""
                                    else
                                        Sanitize.stripControlChars m |> fun s -> s.Trim())
                                |> Array.filter (fun m -> m.Length > 0)
                            else
                                [||]

                        let allMessages =
                            if cleanPrevious.Length > 0 then
                                let prev = String.Join("\n", cleanPrevious)
                                $"{prev}\n{body.Message}"
                            else
                                body.Message

                        if allMessages.Length > maxTotalParseInputLength then
                            return!
                                badRequest
                                    jsonOptions
                                    $"Combined message input is too long (max {maxTotalParseInputLength} characters)."
                                    ctx
                        else

                            let! result = parseInput httpClient geminiConfig allMessages now

                            match result with
                            | Ok parseResult ->
                                // Sanitize LLM output — model-generated text is untrusted
                                let sanitized = sanitizeParseResult parseResult

                                let response =
                                    { ParseResult = sanitized
                                      SystemMessage = buildSystemMessage sanitized }

                                return! Response.ofJsonOptions jsonOptions response ctx
                            | Error err ->
                                log().Error("Parse request failed: {Error}", err)
                                ctx.Response.StatusCode <- 500

                                return!
                                    Response.ofJsonOptions jsonOptions {| Error = "An internal error occurred." |} ctx
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
                elif body.AvailabilityWindows.Length > maxAvailabilityWindows then
                    return! badRequest jsonOptions $"Too many availability windows (max {maxAvailabilityWindows})." ctx
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
                                | Ok s, Ok e when e.ToInstant() <= s.ToInstant() ->
                                    Error $"AvailabilityWindows[{i}].End must be after AvailabilityWindows[{i}].Start."
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
                                        { TimeSlotDto.Start = odtFormatPattern.Format(s.SlotStart)
                                          End = odtFormatPattern.Format(s.SlotEnd) }) }

                            return! Response.ofJsonOptions jsonOptions response ctx
        }

let handleBook (createConn: unit -> SqliteConnection) (hostTz: DateTimeZone) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            let! bodyResult = tryReadJsonBody<BookRequest> jsonOptions ctx

            match bodyResult with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok raw ->

                // Sanitize all participant-supplied strings before validation
                let body =
                    { raw with
                        Name = Sanitize.sanitizeName raw.Name
                        Email = Sanitize.sanitizeEmail raw.Email
                        Phone = Sanitize.sanitizePhone raw.Phone
                        Title = Sanitize.sanitizeTitle raw.Title
                        Description =
                            if String.IsNullOrEmpty(raw.Description) then
                                raw.Description
                            else
                                Sanitize.sanitizeDescription raw.Description }

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
