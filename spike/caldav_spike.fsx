// CalDAV Calendar Fetch Spike
// ===========================
// Connects to Fastmail and iCloud CalDAV servers, discovers calendars,
// fetches events for a 14-day window, and prints formatted results.
//
// Usage:
//   export FASTMAIL_CALDAV_USER="user@domain.com"
//   export FASTMAIL_CALDAV_PASSWORD="app-password"
//   export ICLOUD_CALDAV_USER="user@icloud.com"
//   export ICLOUD_CALDAV_PASSWORD="app-specific-password"
//   dotnet fsi spike/caldav_spike.fsx

#r "nuget: Ical.Net, 4.3.1"

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Xml.Linq
open Ical.Net
open Ical.Net.CalendarComponents
open Ical.Net.DataTypes

// ─── Provider Configuration ───────────────────────────────────────────

type Provider =
    | Fastmail
    | ICloud

type ProviderConfig =
    { Provider: Provider
      BaseUrl: string
      Username: string
      Password: string }

let providerName =
    function
    | Fastmail -> "Fastmail"
    | ICloud -> "iCloud"

// ─── Credential Loading ──────────────────────────────────────────────

type CredentialResult =
    | Ok of ProviderConfig
    | Missing of provider: string * missingVars: string list

let loadProvider (provider: Provider) (userVar: string) (passVar: string) (baseUrl: string) : CredentialResult =
    let user = Environment.GetEnvironmentVariable(userVar)
    let pass = Environment.GetEnvironmentVariable(passVar)

    let missing =
        [ if String.IsNullOrEmpty(user) then userVar
          if String.IsNullOrEmpty(pass) then passVar ]

    if missing.IsEmpty then
        Ok
            { Provider = provider
              BaseUrl = baseUrl
              Username = user
              Password = pass }
    else
        Missing(providerName provider, missing)

let loadAllProviders () =
    [ loadProvider Fastmail "FASTMAIL_CALDAV_USER" "FASTMAIL_CALDAV_PASSWORD" "https://caldav.fastmail.com/dav/calendars"
      loadProvider ICloud "ICLOUD_CALDAV_USER" "ICLOUD_CALDAV_PASSWORD" "https://caldav.icloud.com/" ]

// ─── XML Namespaces ──────────────────────────────────────────────────

let nsDAV = XNamespace.Get "DAV:"
let nsCalDAV = XNamespace.Get "urn:ietf:params:xml:ns:caldav"
let nsApple = XNamespace.Get "http://apple.com/ns/ical/"
let nsCS = XNamespace.Get "http://calendarserver.org/ns/"

// ─── HTTP Helpers ────────────────────────────────────────────────────

let createClient (config: ProviderConfig) =
    let handler = new HttpClientHandler()
    handler.AllowAutoRedirect <- true
    handler.MaxAutomaticRedirections <- 10

    let client = new HttpClient(handler)
    client.Timeout <- TimeSpan.FromSeconds(30.0)

    let credentials =
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"))

    client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Basic", credentials)
    client

let sendWebDAV (client: HttpClient) (method: string) (url: string) (body: string) (depth: string) =
    async {
        let request = new HttpRequestMessage(HttpMethod(method), url)

        if not (String.IsNullOrEmpty(body)) then
            request.Content <- new StringContent(body, Encoding.UTF8, "application/xml")

        request.Headers.Add("Depth", depth)

        let! response = client.SendAsync(request) |> Async.AwaitTask

        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        return (response.StatusCode, response.Headers, content)
    }

// ─── CalDAV Discovery ────────────────────────────────────────────────

type CalendarInfo =
    { DisplayName: string
      Url: string
      Color: string option
      SupportedComponents: string list }

/// Discover the principal URL via PROPFIND on the base URL.
let discoverPrincipal (client: HttpClient) (baseUrl: string) =
    async {
        let body =
            """<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:">
  <d:prop>
    <d:current-user-principal/>
  </d:prop>
</d:propfind>"""

        let! (status, _, content) = sendWebDAV client "PROPFIND" baseUrl body "0"

        if int status >= 400 then
            return Error $"PROPFIND on {baseUrl} returned {status}"
        else
            let doc = XDocument.Parse(content)

            let principalHref =
                doc.Descendants(nsDAV + "current-user-principal")
                |> Seq.tryHead
                |> Option.bind (fun el ->
                    el.Elements(nsDAV + "href")
                    |> Seq.tryHead
                    |> Option.map (fun h -> h.Value))

            match principalHref with
            | Some href ->
                let uri = Uri(Uri(baseUrl), href)
                return Result.Ok(uri.ToString())
            | None -> return Error "No current-user-principal found in response"
    }

/// Discover the calendar home set from the principal URL.
let discoverCalendarHome (client: HttpClient) (principalUrl: string) =
    async {
        let body =
            """<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
  <d:prop>
    <c:calendar-home-set/>
  </d:prop>
</d:propfind>"""

        let request = new HttpRequestMessage(HttpMethod("PROPFIND"), principalUrl)
        request.Content <- new StringContent(body, Encoding.UTF8, "application/xml")
        request.Headers.Add("Depth", "0")

        let! response = client.SendAsync(request) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        if int response.StatusCode >= 400 then
            return Error $"PROPFIND on principal returned {response.StatusCode}"
        else
            let doc = XDocument.Parse(content)

            let homeHref =
                doc.Descendants(nsCalDAV + "calendar-home-set")
                |> Seq.tryHead
                |> Option.bind (fun el ->
                    el.Elements(nsDAV + "href")
                    |> Seq.tryHead
                    |> Option.map (fun h -> h.Value))

            match homeHref with
            | Some href ->
                let uri = Uri(Uri(principalUrl), href)
                return Result.Ok(uri.ToString())
            | None -> return Error "No calendar-home-set found in response"
    }

/// List calendars from the calendar home set.
let listCalendars (client: HttpClient) (homeUrl: string) =
    async {
        let body =
            """<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:ic="http://apple.com/ns/ical/" xmlns:cs="http://calendarserver.org/ns/">
  <d:prop>
    <d:resourcetype/>
    <d:displayname/>
    <ic:calendar-color/>
    <c:supported-calendar-component-set/>
  </d:prop>
</d:propfind>"""

        let! (status, _, content) = sendWebDAV client "PROPFIND" homeUrl body "1"

        if int status >= 400 then
            return Error $"PROPFIND on calendar home returned {status}"
        else
            let doc = XDocument.Parse(content)

            let calendars =
                doc.Descendants(nsDAV + "response")
                |> Seq.choose (fun resp ->
                    let href =
                        resp.Elements(nsDAV + "href")
                        |> Seq.tryHead
                        |> Option.map (fun h -> h.Value)

                    let propStat =
                        resp.Elements(nsDAV + "propstat")
                        |> Seq.tryHead

                    match href, propStat with
                    | Some h, Some ps ->
                        let prop = ps.Element(nsDAV + "prop")

                        if prop = null then
                            None
                        else
                            // Check if this is actually a calendar (has calendar resourcetype)
                            let isCalendar =
                                prop.Descendants(nsCalDAV + "calendar") |> Seq.isEmpty |> not

                            if not isCalendar then
                                None
                            else
                                let displayName =
                                    match prop.Element(nsDAV + "displayname") with
                                    | null -> "(unnamed)"
                                    | el -> if String.IsNullOrEmpty(el.Value) then "(unnamed)" else el.Value

                                let color =
                                    match prop.Element(nsApple + "calendar-color") with
                                    | null -> None
                                    | el ->
                                        if String.IsNullOrEmpty(el.Value) then
                                            None
                                        else
                                            Some el.Value

                                let components =
                                    match prop.Element(nsCalDAV + "supported-calendar-component-set") with
                                    | null -> []
                                    | el ->
                                        el.Elements(nsCalDAV + "comp")
                                        |> Seq.choose (fun c ->
                                            let nameAttr = c.Attribute(XName.Get "name")
                                            if nameAttr <> null then Some nameAttr.Value else None)
                                        |> Seq.toList

                                let url = Uri(Uri(homeUrl), h).ToString()

                                Some
                                    { DisplayName = displayName
                                      Url = url
                                      Color = color
                                      SupportedComponents = components }
                    | _ -> None)
                |> Seq.toList

            return Result.Ok calendars
    }

// ─── Event Fetching ──────────────────────────────────────────────────

type EventInfo =
    { Summary: string
      Start: string
      End: string
      Timezone: string
      IsAllDay: bool
      IsRecurring: bool
      RecurrenceRule: string option
      Location: string option }

/// Fetch events from a calendar using a REPORT calendar-query with time-range.
let fetchEvents (client: HttpClient) (calendarUrl: string) (startDate: DateTime) (endDate: DateTime) =
    async {
        let startStr = startDate.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
        let endStr = endDate.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")

        let body =
            $"""<?xml version="1.0" encoding="utf-8"?>
<c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
  <d:prop>
    <d:getetag/>
    <c:calendar-data/>
  </d:prop>
  <c:filter>
    <c:comp-filter name="VCALENDAR">
      <c:comp-filter name="VEVENT">
        <c:time-range start="{startStr}" end="{endStr}"/>
      </c:comp-filter>
    </c:comp-filter>
  </c:filter>
</c:calendar-query>"""

        let request = new HttpRequestMessage(HttpMethod("REPORT"), calendarUrl)
        request.Content <- new StringContent(body, Encoding.UTF8, "application/xml")
        request.Headers.Add("Depth", "1")

        let! response = client.SendAsync(request) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        if int response.StatusCode >= 400 then
            return Error $"REPORT on {calendarUrl} returned {response.StatusCode}"
        else
            let doc = XDocument.Parse(content)

            let events =
                doc.Descendants(nsCalDAV + "calendar-data")
                |> Seq.collect (fun calData ->
                    let icsText = calData.Value

                    if String.IsNullOrWhiteSpace(icsText) then
                        Seq.empty
                    else
                        try
                            let calendar = Calendar.Load(icsText)

                            calendar.Events
                            |> Seq.map (fun evt ->
                                let isAllDay = evt.IsAllDay

                                let startStr =
                                    if isAllDay then
                                        evt.DtStart.Date.ToString("yyyy-MM-dd")
                                    else
                                        evt.DtStart.ToString()

                                let endStr =
                                    if evt.DtEnd <> null then
                                        if isAllDay then
                                            evt.DtEnd.Date.ToString("yyyy-MM-dd")
                                        else
                                            evt.DtEnd.ToString()
                                    else
                                        "(none)"

                                let tz =
                                    if evt.DtStart <> null && evt.DtStart.TzId <> null then
                                        evt.DtStart.TzId
                                    elif evt.DtStart <> null && evt.DtStart.IsUtc then
                                        "UTC"
                                    elif isAllDay then
                                        "(floating/all-day)"
                                    else
                                        "(local/floating)"

                                let isRecurring =
                                    evt.RecurrenceRules <> null && evt.RecurrenceRules.Count > 0

                                let rrule =
                                    if isRecurring then
                                        evt.RecurrenceRules
                                        |> Seq.tryHead
                                        |> Option.map (fun r -> r.ToString())
                                    else
                                        None

                                { Summary =
                                    if String.IsNullOrEmpty(evt.Summary) then
                                        "(no summary)"
                                    else
                                        evt.Summary
                                  Start = startStr
                                  End = endStr
                                  Timezone = tz
                                  IsAllDay = isAllDay
                                  IsRecurring = isRecurring
                                  RecurrenceRule = rrule
                                  Location =
                                    if String.IsNullOrEmpty(evt.Location) then
                                        None
                                    else
                                        Some evt.Location })
                        with ex ->
                            eprintfn "  Warning: failed to parse iCalendar data: %s" ex.Message
                            Seq.empty)
                |> Seq.toList

            return Result.Ok events
    }

// ─── Output Formatting ──────────────────────────────────────────────

let printSeparator () =
    printfn "%s" (String.replicate 60 "─")

let printHeader (text: string) =
    printfn ""
    printSeparator ()
    printfn "  %s" text
    printSeparator ()

let printCalendar (cal: CalendarInfo) =
    printfn "  📅 %s" cal.DisplayName
    printfn "     URL: %s" cal.Url

    match cal.Color with
    | Some c -> printfn "     Color: %s" c
    | None -> ()

    if not cal.SupportedComponents.IsEmpty then
        printfn "     Components: %s" (String.Join(", ", cal.SupportedComponents))

let printEvent (evt: EventInfo) =
    printfn "    - %s" evt.Summary
    printfn "      Start: %s" evt.Start
    printfn "      End:   %s" evt.End
    printfn "      TZ:    %s" evt.Timezone

    if evt.IsAllDay then
        printfn "      (all-day event)"

    if evt.IsRecurring then
        printfn "      RECURRING: %s" (evt.RecurrenceRule |> Option.defaultValue "yes")

    match evt.Location with
    | Some loc -> printfn "      Location: %s" loc
    | None -> ()

// ─── Main ────────────────────────────────────────────────────────────

let runProvider (config: ProviderConfig) =
    async {
        let name = providerName config.Provider
        printHeader $"{name} CalDAV"
        printfn "  Connecting as: %s" config.Username
        printfn "  Base URL: %s" config.BaseUrl

        use client = createClient config

        // Step 1: Discover principal
        printfn ""
        printfn "  [1/3] Discovering principal..."
        let! principalResult = discoverPrincipal client config.BaseUrl

        match principalResult with
        | Error msg ->
            printfn "  ERROR: %s" msg
            return ()
        | Result.Ok principalUrl ->
            printfn "  Principal URL: %s" principalUrl

            // Step 2: Discover calendar home
            printfn "  [2/3] Discovering calendar home..."
            let! homeResult = discoverCalendarHome client principalUrl

            match homeResult with
            | Error msg ->
                printfn "  ERROR: %s" msg
                return ()
            | Result.Ok homeUrl ->
                printfn "  Calendar home: %s" homeUrl

                // Step 3: List calendars
                printfn "  [3/3] Listing calendars..."
                let! calendarsResult = listCalendars client homeUrl

                match calendarsResult with
                | Error msg ->
                    printfn "  ERROR: %s" msg
                    return ()
                | Result.Ok calendars ->
                    printfn ""
                    printfn "  Found %d calendar(s):" calendars.Length

                    for cal in calendars do
                        printCalendar cal

                    // Fetch events for each calendar with VEVENT support
                    let eventCalendars =
                        calendars
                        |> List.filter (fun c ->
                            c.SupportedComponents.IsEmpty || c.SupportedComponents |> List.contains "VEVENT")

                    let startDate = DateTime(2026, 1, 26, 0, 0, 0, DateTimeKind.Utc)
                    let endDate = DateTime(2026, 2, 12, 23, 59, 59, DateTimeKind.Utc)

                    printfn ""
                    printfn "  Fetching events from %s to %s..." (startDate.ToString("yyyy-MM-dd")) (endDate.ToString("yyyy-MM-dd"))

                    for cal in eventCalendars do
                        printfn ""
                        printfn "  Calendar: %s" cal.DisplayName
                        let! eventsResult = fetchEvents client cal.Url startDate endDate

                        match eventsResult with
                        | Error msg -> printfn "    ERROR: %s" msg
                        | Result.Ok events ->
                            if events.IsEmpty then
                                printfn "    (no events in date range)"
                            else
                                printfn "    %d event(s):" events.Length

                                let allDay = events |> List.filter (fun e -> e.IsAllDay)
                                let recurring = events |> List.filter (fun e -> e.IsRecurring)

                                printfn "    [%d all-day, %d recurring]" allDay.Length recurring.Length

                                for evt in events do
                                    printEvent evt

                    // Summary
                    printfn ""
                    printfn "  --- %s Summary ---" name
                    printfn "  Calendars found: %d" calendars.Length
                    printfn "  Calendars with VEVENT support: %d" eventCalendars.Length
    }

// ─── Entry Point ─────────────────────────────────────────────────────

printHeader "CalDAV Calendar Fetch Spike"
printfn "  Date: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
printfn "  Window: 2026-01-26 to 2026-02-12"

let providers = loadAllProviders ()

let configs =
    providers
    |> List.choose (fun r ->
        match r with
        | Ok config -> Some config
        | Missing(name, vars) ->
            printfn ""
            printfn "  SKIPPING %s: missing environment variable(s): %s" name (String.Join(", ", vars))
            None)

if configs.IsEmpty then
    printfn ""
    printfn "  ERROR: No providers configured. Set environment variables and try again."
    printfn ""
    printfn "  Required:"
    printfn "    FASTMAIL_CALDAV_USER / FASTMAIL_CALDAV_PASSWORD"
    printfn "    ICLOUD_CALDAV_USER / ICLOUD_CALDAV_PASSWORD"
    printfn ""
    printfn "  At least one provider must be configured."
else
    for config in configs do
        try
            runProvider config |> Async.RunSynchronously
        with ex ->
            printfn ""
            printfn "  UNEXPECTED ERROR with %s: %s" (providerName config.Provider) ex.Message
            printfn "  %s" ex.StackTrace

    printHeader "Done"
