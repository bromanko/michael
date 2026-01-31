module Michael.CalDav

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Xml.Linq
open Ical.Net
open Ical.Net.CalendarComponents
open NodaTime
open Michael.Domain

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type CalendarInfo =
    { DisplayName: string
      Url: string
      SupportedComponents: string list }

// ---------------------------------------------------------------------------
// XML Namespaces
// ---------------------------------------------------------------------------

let private nsDAV = XNamespace.Get "DAV:"
let private nsCalDAV = XNamespace.Get "urn:ietf:params:xml:ns:caldav"
let private nsApple = XNamespace.Get "http://apple.com/ns/ical/"

// ---------------------------------------------------------------------------
// HTTP Helpers
// ---------------------------------------------------------------------------

let createHttpClient (username: string) (password: string) : HttpClient =
    let handler = new HttpClientHandler()
    handler.AllowAutoRedirect <- true
    handler.MaxAutomaticRedirections <- 10

    let client = new HttpClient(handler)
    client.Timeout <- TimeSpan.FromSeconds(30.0)

    let credentials =
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"))

    client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Basic", credentials)
    client

let private sendWebDAV
    (client: HttpClient)
    (method: string)
    (url: string)
    (body: string)
    (depth: string)
    =
    task {
        use request = new HttpRequestMessage(HttpMethod(method), url)

        if not (String.IsNullOrEmpty(body)) then
            request.Content <- new StringContent(body, Encoding.UTF8, "application/xml")

        request.Headers.Add("Depth", depth)

        let! response = client.SendAsync(request)
        let! content = response.Content.ReadAsStringAsync()

        return (int response.StatusCode, content)
    }

// ---------------------------------------------------------------------------
// CalDAV Discovery
// ---------------------------------------------------------------------------

let discoverPrincipal (client: HttpClient) (baseUrl: string) =
    task {
        let body =
            """<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:">
  <d:prop>
    <d:current-user-principal/>
  </d:prop>
</d:propfind>"""

        let! (status, content) = sendWebDAV client "PROPFIND" baseUrl body "0"

        if status >= 400 then
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
                return Ok(uri.ToString())
            | None -> return Error "No current-user-principal found in response"
    }

let discoverCalendarHome (client: HttpClient) (principalUrl: string) =
    task {
        let body =
            """<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
  <d:prop>
    <c:calendar-home-set/>
  </d:prop>
</d:propfind>"""

        let! (status, content) = sendWebDAV client "PROPFIND" principalUrl body "0"

        if status >= 400 then
            return Error $"PROPFIND on principal returned {status}"
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
                return Ok(uri.ToString())
            | None -> return Error "No calendar-home-set found in response"
    }

let listCalendars (client: HttpClient) (homeUrl: string) =
    task {
        let body =
            """<?xml version="1.0" encoding="utf-8"?>
<d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:ic="http://apple.com/ns/ical/" xmlns:cs="http://calendarserver.org/ns/">
  <d:prop>
    <d:resourcetype/>
    <d:displayname/>
    <c:supported-calendar-component-set/>
  </d:prop>
</d:propfind>"""

        let! (status, content) = sendWebDAV client "PROPFIND" homeUrl body "1"

        if status >= 400 then
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
                        ps.Element(nsDAV + "prop")
                        |> Option.ofObj
                        |> Option.bind (fun prop ->
                            let isCalendar =
                                prop.Descendants(nsCalDAV + "calendar") |> Seq.isEmpty |> not

                            if not isCalendar then
                                None
                            else
                                let displayName =
                                    prop.Element(nsDAV + "displayname")
                                    |> Option.ofObj
                                    |> Option.map (fun el -> el.Value)
                                    |> Option.filter (String.IsNullOrEmpty >> not)
                                    |> Option.defaultValue "(unnamed)"

                                let components =
                                    prop.Element(nsCalDAV + "supported-calendar-component-set")
                                    |> Option.ofObj
                                    |> Option.map (fun el ->
                                        el.Elements(nsCalDAV + "comp")
                                        |> Seq.choose (fun c ->
                                            c.Attribute(XName.Get "name")
                                            |> Option.ofObj
                                            |> Option.map (fun a -> a.Value))
                                        |> Seq.toList)
                                    |> Option.defaultValue []

                                let url = Uri(Uri(homeUrl), h).ToString()

                                Some
                                    { DisplayName = displayName
                                      Url = url
                                      SupportedComponents = components })
                    | _ -> None)
                |> Seq.toList

            return Ok calendars
    }

// ---------------------------------------------------------------------------
// Event Fetching
// ---------------------------------------------------------------------------

let fetchRawEvents
    (client: HttpClient)
    (calendarUrl: string)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    =
    task {
        let startStr =
            rangeStart
                .ToDateTimeUtc()
                .ToString("yyyyMMdd'T'HHmmss'Z'")

        let endStr =
            rangeEnd
                .ToDateTimeUtc()
                .ToString("yyyyMMdd'T'HHmmss'Z'")

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

        use request = new HttpRequestMessage(HttpMethod("REPORT"), calendarUrl)
        request.Content <- new StringContent(body, Encoding.UTF8, "application/xml")
        request.Headers.Add("Depth", "1")

        let! response = client.SendAsync(request)
        let! content = response.Content.ReadAsStringAsync()

        if int response.StatusCode >= 400 then
            return Error $"REPORT on {calendarUrl} returned {response.StatusCode}"
        else
            let doc = XDocument.Parse(content)

            let icsStrings =
                doc.Descendants(nsCalDAV + "calendar-data")
                |> Seq.map (fun el -> el.Value)
                |> Seq.filter (fun s -> not (String.IsNullOrWhiteSpace(s)))
                |> Seq.toList

            return Ok icsStrings
    }

// ---------------------------------------------------------------------------
// ICS Parsing and RRULE Expansion
// ---------------------------------------------------------------------------

let parseAndExpandEvents
    (sourceId: Guid)
    (calendarUrl: string)
    (icsStrings: string list)
    (hostTz: DateTimeZone)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    : CachedEvent list =
    let searchStart = rangeStart.ToDateTimeUtc()
    let searchEnd = rangeEnd.ToDateTimeUtc()

    icsStrings
    |> List.collect (fun icsText ->
        try
            let calendar = Calendar.Load(icsText)

            calendar.Events
            |> Seq.toList
            |> List.collect (fun (evt: CalendarEvent) ->
                let occurrences =
                    evt.GetOccurrences(searchStart, searchEnd)
                    |> Seq.toList

                occurrences
                |> List.map (fun (occ: Ical.Net.DataTypes.Occurrence) ->
                    let isAllDay = evt.IsAllDay

                    let startInstant, endInstant =
                        if isAllDay then
                            let startDate = occ.Period.StartTime.Date
                            let endDate =
                                if occ.Period.EndTime <> null then
                                    occ.Period.EndTime.Date
                                else
                                    startDate.AddDays(1)
                            let localStart = LocalDate(startDate.Year, startDate.Month, startDate.Day)
                            let localEnd = LocalDate(endDate.Year, endDate.Month, endDate.Day)
                            // If start == end (single day), use PlusDays(1)
                            let localEnd = if localEnd = localStart then localStart.PlusDays(1) else localEnd
                            let startZoned = hostTz.AtStartOfDay(localStart)
                            let endZoned = hostTz.AtStartOfDay(localEnd)
                            (startZoned.ToInstant(), endZoned.ToInstant())
                        else
                            let dtStart : DateTimeOffset = occ.Period.StartTime.AsDateTimeOffset
                            let duration : TimeSpan = evt.Duration
                            let dtEnd : DateTimeOffset =
                                if duration.Ticks > 0L then
                                    dtStart.Add(duration)
                                elif evt.DtEnd <> null then
                                    let origDuration : TimeSpan = evt.DtEnd.AsDateTimeOffset - evt.DtStart.AsDateTimeOffset
                                    dtStart.Add(origDuration)
                                else
                                    dtStart.AddHours(1.0)

                            (Instant.FromDateTimeOffset(dtStart),
                             Instant.FromDateTimeOffset(dtEnd))

                    let uid =
                        if String.IsNullOrEmpty(evt.Uid) then Guid.NewGuid().ToString()
                        else evt.Uid

                    let summary =
                        if String.IsNullOrEmpty(evt.Summary) then "(no summary)"
                        else evt.Summary

                    { Id = Guid.NewGuid()
                      SourceId = sourceId
                      CalendarUrl = calendarUrl
                      Uid = uid
                      Summary = summary
                      StartInstant = startInstant
                      EndInstant = endInstant
                      IsAllDay = isAllDay }))
        with
        | :? FormatException as ex ->
            eprintfn "Failed to parse ICS data from %s: %s" calendarUrl ex.Message
            []
        | :? ArgumentException as ex ->
            eprintfn "Invalid argument parsing ICS data from %s: %s" calendarUrl ex.Message
            []
        | :? System.Runtime.Serialization.SerializationException as ex ->
            eprintfn "Failed to deserialize ICS data from %s: %s" calendarUrl ex.Message
            [])

// ---------------------------------------------------------------------------
// High-level sync
// ---------------------------------------------------------------------------

let syncSource
    (client: HttpClient)
    (source: CalendarSource)
    (hostTz: DateTimeZone)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    =
    task {
        // Step 1: Discover principal
        let! principalResult = discoverPrincipal client source.BaseUrl

        match principalResult with
        | Error msg -> return Error $"Principal discovery failed: {msg}"
        | Ok principalUrl ->

        // Step 2: Discover calendar home
        let! homeResult = discoverCalendarHome client principalUrl

        match homeResult with
        | Error msg -> return Error $"Calendar home discovery failed: {msg}"
        | Ok homeUrl ->

        // Step 3: List calendars
        let! calendarsResult = listCalendars client homeUrl

        match calendarsResult with
        | Error msg -> return Error $"Calendar listing failed: {msg}"
        | Ok calendars ->

        let eventCalendars =
            calendars
            |> List.filter (fun c ->
                c.SupportedComponents.IsEmpty || c.SupportedComponents |> List.contains "VEVENT")

        // Step 4: Fetch and parse events from all calendars
        let! allEvents =
            eventCalendars
            |> List.fold (fun accTask cal -> task {
                let! acc = accTask
                let! result = fetchRawEvents client cal.Url rangeStart rangeEnd
                match result with
                | Ok icsStrings ->
                    let parsed =
                        parseAndExpandEvents source.Id cal.Url icsStrings hostTz rangeStart rangeEnd
                    return acc @ parsed
                | Error msg ->
                    eprintfn "Failed to fetch events from %s: %s" cal.Url msg
                    return acc
            }) (task { return [] })

        return Ok allEvents
    }
