module Michael.CalDav

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Xml.Linq
open Ical.Net
open Ical.Net.CalendarComponents
open Ical.Net.DataTypes
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

let createHttpClient (httpClientFactory: IHttpClientFactory) (username: string) (password: string) : HttpClient =
    let client = httpClientFactory.CreateClient("caldav")

    let credentials =
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"))

    client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Basic", credentials)
    client

let private sendWebDAV (client: HttpClient) (method: string) (url: string) (body: string) (depth: string) =
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
                |> Option.bind (fun el -> el.Elements(nsDAV + "href") |> Seq.tryHead |> Option.map (fun h -> h.Value))

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
                |> Option.bind (fun el -> el.Elements(nsDAV + "href") |> Seq.tryHead |> Option.map (fun h -> h.Value))

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
                        resp.Elements(nsDAV + "href") |> Seq.tryHead |> Option.map (fun h -> h.Value)

                    let propStat = resp.Elements(nsDAV + "propstat") |> Seq.tryHead

                    match href, propStat with
                    | Some h, Some ps ->
                        ps.Element(nsDAV + "prop")
                        |> Option.ofObj
                        |> Option.bind (fun prop ->
                            let isCalendar = prop.Descendants(nsCalDAV + "calendar") |> Seq.isEmpty |> not

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

let fetchRawEvents (client: HttpClient) (calendarUrl: string) (rangeStart: Instant) (rangeEnd: Instant) =
    task {
        let startStr = rangeStart.ToDateTimeUtc().ToString("yyyyMMdd'T'HHmmss'Z'")

        let endStr = rangeEnd.ToDateTimeUtc().ToString("yyyyMMdd'T'HHmmss'Z'")

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
// CalDAV PUT and DELETE
// ---------------------------------------------------------------------------

let putEvent (client: HttpClient) (resourceUrl: string) (icsContent: string) : System.Threading.Tasks.Task<Result<string, string>> =
    task {
        use request = new HttpRequestMessage(HttpMethod.Put, resourceUrl)
        request.Content <- new StringContent(icsContent, Encoding.UTF8, "text/calendar")

        let! response = client.SendAsync(request)

        if int response.StatusCode >= 200 && int response.StatusCode < 300 then
            match response.Headers.Location with
            | null -> return Ok resourceUrl
            | location -> return Ok(location.ToString())
        else
            let! body = response.Content.ReadAsStringAsync()
            return Error $"PUT {resourceUrl} returned {int response.StatusCode}: {body}"
    }

let deleteEvent (client: HttpClient) (resourceUrl: string) : System.Threading.Tasks.Task<Result<unit, string>> =
    task {
        use request = new HttpRequestMessage(HttpMethod.Delete, resourceUrl)

        let! response = client.SendAsync(request)
        let statusCode = int response.StatusCode

        if (statusCode >= 200 && statusCode < 300) || statusCode = 404 then
            return Ok()
        else
            let! body = response.Content.ReadAsStringAsync()
            return Error $"DELETE {resourceUrl} returned {statusCode}: {body}"
    }

// ---------------------------------------------------------------------------
// ICS Parsing and RRULE Expansion
// ---------------------------------------------------------------------------

/// Convert a CalDateTime to a DateTimeOffset using its UTC representation.
let private calDateTimeToOffset (cdt: CalDateTime) : DateTimeOffset =
    DateTimeOffset(cdt.AsUtc, TimeSpan.Zero)

let parseAndExpandEvents
    (sourceId: Guid)
    (calendarUrl: string)
    (icsStrings: string list)
    (hostTz: DateTimeZone)
    (rangeStart: Instant)
    (rangeEnd: Instant)
    : CachedEvent list =
    let searchStart = CalDateTime(rangeStart.ToDateTimeUtc(), true)
    let searchEnd = CalDateTime(rangeEnd.ToDateTimeUtc(), true)

    icsStrings
    |> List.collect (fun icsText ->
        try
            let calendar = Calendar.Load(icsText)

            calendar.Events
            |> Seq.toList
            |> List.collect (fun (evt: CalendarEvent) ->
                let occurrences =
                    evt.GetOccurrences(searchStart).TakeWhileBefore(searchEnd) |> Seq.toList

                occurrences
                |> List.map (fun (occ: Occurrence) ->
                    let isAllDay = evt.IsAllDay

                    let startInstant, endInstant =
                        if isAllDay then
                            let startDate = occ.Period.StartTime.Date
                            let effEnd = occ.Period.EffectiveEndTime

                            let endDate = if effEnd <> null then effEnd.Date else startDate.AddDays(1)

                            let localStart = LocalDate(startDate.Year, startDate.Month, startDate.Day)
                            let localEnd = LocalDate(endDate.Year, endDate.Month, endDate.Day)

                            let localEnd =
                                if localEnd = localStart then
                                    localStart.PlusDays(1)
                                else
                                    localEnd

                            let startZoned = hostTz.AtStartOfDay(localStart)
                            let endZoned = hostTz.AtStartOfDay(localEnd)
                            (startZoned.ToInstant(), endZoned.ToInstant())
                        else
                            let dtStart = calDateTimeToOffset occ.Period.StartTime
                            let effEnd = occ.Period.EffectiveEndTime

                            let dtEnd =
                                if effEnd <> null then
                                    calDateTimeToOffset effEnd
                                else
                                    dtStart.AddHours(1.0)

                            (Instant.FromDateTimeOffset(dtStart), Instant.FromDateTimeOffset(dtEnd))

                    let uid =
                        if String.IsNullOrEmpty(evt.Uid) then
                            Guid.NewGuid().ToString()
                        else
                            evt.Uid

                    let summary =
                        if String.IsNullOrEmpty(evt.Summary) then
                            "(no summary)"
                        else
                            evt.Summary

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
// ICS Generation for Write-Back
// ---------------------------------------------------------------------------

open Ical.Net.Serialization

let private toCalDateTime (odt: NodaTime.OffsetDateTime) =
    let utc = odt.ToInstant().ToDateTimeUtc()
    CalDateTime(utc, "UTC")

let private instantToCalDateTime (instant: NodaTime.Instant) =
    CalDateTime(instant.ToDateTimeUtc(), "UTC")

/// Generate a VCALENDAR for storing on the host's personal calendar.
/// No METHOD property (this is a stored resource, not an iTIP message).
/// SUMMARY includes participant name for the host's calendar view.
/// DESCRIPTION includes participant contact info.
let buildCalDavEventIcs (booking: Domain.Booking) (hostEmail: string) (videoLink: string option) : string =
    let cal = Calendar()
    cal.AddProperty("PRODID", "-//Michael//Michael//EN")
    // No cal.Method — intentionally omitted for CalDAV stored events

    let evt = CalendarEvent()
    evt.Uid <- $"{booking.Id}@michael"
    evt.DtStamp <- instantToCalDateTime booking.CreatedAt
    evt.DtStart <- toCalDateTime booking.StartTime
    evt.DtEnd <- toCalDateTime booking.EndTime
    evt.Summary <- Sanitize.stripControlChars $"Meeting with {booking.ParticipantName}: {booking.Title}"

    let descParts =
        [ Some $"Participant: {Sanitize.stripControlChars booking.ParticipantName}"
          Some $"Email: {Sanitize.stripControlChars booking.ParticipantEmail}"
          booking.ParticipantPhone |> Option.map (fun p -> $"Phone: {Sanitize.stripControlChars p}")
          booking.Description |> Option.map (fun d -> $"\n{Sanitize.stripControlChars d}") ]

    let desc = descParts |> List.choose id |> String.concat "\n"
    evt.Description <- desc

    match videoLink with
    | Some link when not (System.String.IsNullOrWhiteSpace(link)) -> evt.Location <- link
    | _ -> ()

    evt.Status <- "CONFIRMED"
    evt.Sequence <- 0

    cal.Events.Add(evt)
    let serializer = CalendarSerializer()
    serializer.SerializeToString(cal)

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
                        |> List.fold
                            (fun accTask cal ->
                                task {
                                    let! acc = accTask
                                    let! result = fetchRawEvents client cal.Url rangeStart rangeEnd

                                    match result with
                                    | Ok icsStrings ->
                                        let parsed =
                                            parseAndExpandEvents
                                                source.Id
                                                cal.Url
                                                icsStrings
                                                hostTz
                                                rangeStart
                                                rangeEnd

                                        return acc @ parsed
                                    | Error msg ->
                                        eprintfn "Failed to fetch events from %s: %s" cal.Url msg
                                        return acc
                                })
                            (task { return [] })

                    return Ok allEvents
    }
