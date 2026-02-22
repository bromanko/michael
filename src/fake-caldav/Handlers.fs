module FakeCalDav.Handlers

open System
open System.IO
open System.Text
open System.Xml.Linq
open Microsoft.AspNetCore.Http
open FakeCalDav.Scenario
open FakeCalDav.IcsGenerator

// ---------------------------------------------------------------------------
// XML Namespaces
// ---------------------------------------------------------------------------

let private nsDAV = XNamespace.Get "DAV:"
let private nsCalDAV = XNamespace.Get "urn:ietf:params:xml:ns:caldav"

// ---------------------------------------------------------------------------
// URL helpers
// ---------------------------------------------------------------------------

let private userPath = "/dav/calendars/user/fake@example.com"
let private principalPath = "/dav/principals/user/fake@example.com/"
let private calendarHomePath = $"{userPath}/"

let calendarPath (slug: string) = $"{userPath}/{slug}/"

// ---------------------------------------------------------------------------
// XML response builders
// ---------------------------------------------------------------------------

let private multistatusDoc (responses: XElement list) =
    let doc =
        XDocument(
            XElement(
                nsDAV + "multistatus",
                XAttribute(XNamespace.Xmlns + "d", nsDAV.NamespaceName),
                XAttribute(XNamespace.Xmlns + "c", nsCalDAV.NamespaceName),
                responses |> List.toArray |> Array.map box
            )
        )

    doc.ToString()

let private propstatOk (props: XElement list) =
    XElement(
        nsDAV + "propstat",
        XElement(nsDAV + "prop", props |> List.toArray |> Array.map box),
        XElement(nsDAV + "status", "HTTP/1.1 200 OK")
    )

let private responseEl (href: string) (propstat: XElement) =
    XElement(nsDAV + "response", XElement(nsDAV + "href", href), propstat)

// ---------------------------------------------------------------------------
// PROPFIND: principal discovery
// ---------------------------------------------------------------------------

let handlePrincipalDiscovery () =
    let props =
        [ XElement(nsDAV + "current-user-principal", XElement(nsDAV + "href", principalPath)) ]

    multistatusDoc [ responseEl "/" (propstatOk props) ]

// ---------------------------------------------------------------------------
// PROPFIND: calendar home set
// ---------------------------------------------------------------------------

let handleCalendarHome () =
    let props =
        [ XElement(nsCalDAV + "calendar-home-set", XElement(nsDAV + "href", calendarHomePath)) ]

    multistatusDoc [ responseEl principalPath (propstatOk props) ]

// ---------------------------------------------------------------------------
// PROPFIND: list calendars
// ---------------------------------------------------------------------------

let handleListCalendars (scenario: ResolvedScenario) =
    let calResponses =
        scenario.Calendars
        |> List.map (fun cal ->
            let props =
                [ XElement(nsDAV + "resourcetype", XElement(nsDAV + "collection"), XElement(nsCalDAV + "calendar"))
                  XElement(nsDAV + "displayname", cal.Name)
                  XElement(
                      nsCalDAV + "supported-calendar-component-set",
                      XElement(nsCalDAV + "comp", XAttribute(XName.Get "name", "VEVENT"))
                  ) ]

            responseEl (calendarPath cal.Slug) (propstatOk props))

    // Include the home collection itself (not a calendar)
    let homeProps = [ XElement(nsDAV + "resourcetype", XElement(nsDAV + "collection")) ]

    let homeResponse = responseEl calendarHomePath (propstatOk homeProps)

    multistatusDoc (homeResponse :: calResponses)

// ---------------------------------------------------------------------------
// REPORT: calendar-query (event fetch)
// ---------------------------------------------------------------------------

let private parseTimeRange (body: string) =
    try
        let doc = XDocument.Parse(body)

        let timeRange = doc.Descendants(nsCalDAV + "time-range") |> Seq.tryHead

        match timeRange with
        | Some el ->
            let startAttr = el.Attribute(XName.Get "start")
            let endAttr = el.Attribute(XName.Get "end")

            let parseIcsTime (s: string) =
                DateTime.ParseExact(
                    s,
                    [| "yyyyMMdd'T'HHmmss'Z'"; "yyyyMMdd'T'HHmmss" |],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal
                    ||| System.Globalization.DateTimeStyles.AdjustToUniversal
                )

            let startDt =
                if startAttr <> null then
                    Some(parseIcsTime startAttr.Value)
                else
                    None

            let endDt =
                if endAttr <> null then
                    Some(parseIcsTime endAttr.Value)
                else
                    None

            (startDt, endDt)
        | None -> (None, None)
    with _ ->
        (None, None)

let handleCalendarQuery (calendar: ResolvedCalendar) (body: string) =
    let (rangeStart, rangeEnd) = parseTimeRange body

    let filtered =
        calendar.Events
        |> List.filter (fun evt ->
            let evtStartUtc = evt.Start.ToInstant().ToDateTimeUtc()
            let evtEndUtc = evt.End.ToInstant().ToDateTimeUtc()

            let afterStart =
                match rangeStart with
                | Some s -> evtEndUtc > s
                | None -> true

            let beforeEnd =
                match rangeEnd with
                | Some e -> evtStartUtc < e
                | None -> true

            afterStart && beforeEnd)

    let responses =
        filtered
        |> List.map (fun evt ->
            let ics = generateIcs evt

            let props =
                [ XElement(nsDAV + "getetag", $"\"{evt.Uid}\"")
                  XElement(nsCalDAV + "calendar-data", ics) ]

            responseEl $"{calendarPath calendar.Slug}{evt.Uid}.ics" (propstatOk props))

    multistatusDoc responses

// ---------------------------------------------------------------------------
// Request router
// ---------------------------------------------------------------------------

let handleRequest (scenario: ResolvedScenario) (ctx: HttpContext) =
    task {
        let method = ctx.Request.Method.ToUpperInvariant()
        let path = ctx.Request.Path.Value

        // Read request body
        use reader = new StreamReader(ctx.Request.Body, Encoding.UTF8)
        let! body = reader.ReadToEndAsync()

        let depth =
            match ctx.Request.Headers.TryGetValue("Depth") with
            | true, values -> values.ToString()
            | _ -> "0"

        let respond (status: int) (xml: string) =
            task {
                ctx.Response.StatusCode <- status
                ctx.Response.ContentType <- "application/xml; charset=utf-8"
                do! ctx.Response.WriteAsync(xml)
            }

        match method, path, depth with
        // Step 1: Principal discovery — PROPFIND on base URL or root with Depth:0
        | "PROPFIND", _, "0" when
            path = "/"
            || path.StartsWith("/dav/calendars")
               && not (path.Contains("/principals/"))
               && body.Contains("current-user-principal")
            ->
            do! respond 207 (handlePrincipalDiscovery ())

        // Step 2: Calendar home — PROPFIND on principal URL with Depth:0
        | "PROPFIND", p, "0" when p.Contains("/principals/") && body.Contains("calendar-home-set") ->
            do! respond 207 (handleCalendarHome ())

        // Step 3: List calendars — PROPFIND on calendar home with Depth:1
        | "PROPFIND", p, "1" when p.TrimEnd('/') = userPath.TrimEnd('/') || p = calendarHomePath ->
            do! respond 207 (handleListCalendars scenario)

        // Step 4: Calendar query (REPORT) — fetch events for a specific calendar
        | "REPORT", p, _ ->
            let slug = p.TrimEnd('/').Split('/') |> Array.last

            let calendar = scenario.Calendars |> List.tryFind (fun c -> c.Slug = slug)

            match calendar with
            | Some cal -> do! respond 207 (handleCalendarQuery cal body)
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync("Calendar not found")

        // Step 5: PUT — create/update a CalDAV resource (write-back)
        | "PUT", p, _ when p.EndsWith(".ics") ->
            ctx.Response.StatusCode <- 201
            do! ctx.Response.WriteAsync("")

        // Step 6: DELETE — remove a CalDAV resource (cancellation write-back)
        | "DELETE", p, _ when p.EndsWith(".ics") -> ctx.Response.StatusCode <- 204

        | _ ->
            ctx.Response.StatusCode <- 405
            do! ctx.Response.WriteAsync($"Method {method} not supported at {path}")
    }
