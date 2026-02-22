// CalDAV Write-Back Spike
// =======================
// Validates PUT and DELETE behavior on Fastmail (and optionally iCloud).
// Creates a test VEVENT via PUT, verifies it appears via REPORT, then
// deletes it via DELETE and confirms removal.
//
// Usage:
//   export FASTMAIL_CALDAV_USER="user@domain.com"
//   export FASTMAIL_CALDAV_PASSWORD="app-password"
//   export FASTMAIL_CALDAV_CALENDAR_URL="https://caldav.fastmail.com/dav/calendars/user/user@domain.com/Default/"
//   dotnet fsi spike/caldav_writeback_spike.fsx
//
// The FASTMAIL_CALDAV_CALENDAR_URL should be the full collection URL for
// the calendar you want to write to. You can find it by running the
// original caldav_spike.fsx and looking at the calendar URLs listed.
//
// Optionally set ICLOUD_CALDAV_* equivalents to test iCloud too.

#r "nuget: Ical.Net, 4.3.1"

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Threading.Tasks
open Ical.Net
open Ical.Net.CalendarComponents
open Ical.Net.DataTypes
open Ical.Net.Serialization

// ─── Types ───────────────────────────────────────────────────────────

type Provider =
    | Fastmail
    | ICloud

type ProviderConfig =
    { Provider: Provider
      Username: string
      Password: string
      CalendarUrl: string }

let providerName =
    function
    | Fastmail -> "Fastmail"
    | ICloud -> "iCloud"

// ─── Output Helpers ──────────────────────────────────────────────────

let printSeparator () =
    printfn "%s" (String.replicate 60 "─")

let printHeader (text: string) =
    printfn ""
    printSeparator ()
    printfn "  %s" text
    printSeparator ()

let printStep (step: int) (total: int) (desc: string) =
    printfn ""
    printfn "  [%d/%d] %s" step total desc

let printResult (label: string) (value: string) =
    printfn "    %s: %s" label value

let printOk (msg: string) =
    printfn "    ✅ %s" msg

let printFail (msg: string) =
    printfn "    ❌ %s" msg

let printInfo (msg: string) =
    printfn "    ℹ️  %s" msg

// ─── HTTP Client ─────────────────────────────────────────────────────

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

// ─── ICS Generation ──────────────────────────────────────────────────

let generateTestIcs (uid: string) (summary: string) =
    let cal = Calendar()
    cal.AddProperty("PRODID", "-//Michael//Spike//EN")
    // No METHOD — this is a stored resource, not an iTIP message

    let evt = CalendarEvent()
    evt.Uid <- uid
    evt.DtStamp <- CalDateTime(DateTime.UtcNow, "UTC")
    // Event 2 hours from now, lasting 30 minutes
    let startUtc = DateTime.UtcNow.AddHours(2.0)
    let endUtc = startUtc.AddMinutes(30.0)
    evt.DtStart <- CalDateTime(startUtc, "UTC")
    evt.DtEnd <- CalDateTime(endUtc, "UTC")
    evt.Summary <- summary
    evt.Description <- "Test event created by CalDAV write-back spike.\nThis event should be deleted automatically."
    evt.Status <- "CONFIRMED"
    evt.Sequence <- 0

    cal.Events.Add(evt)
    let serializer = CalendarSerializer()
    serializer.SerializeToString(cal)

// ─── CalDAV Operations ───────────────────────────────────────────────

let putEvent (client: HttpClient) (resourceUrl: string) (icsContent: string) =
    async {
        let request = new HttpRequestMessage(HttpMethod.Put, resourceUrl)
        request.Content <- new StringContent(icsContent, Encoding.UTF8, "text/calendar")

        let! response = client.SendAsync(request) |> Async.AwaitTask
        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        return (response.StatusCode, response.Headers, body)
    }

let deleteEvent (client: HttpClient) (resourceUrl: string) =
    async {
        let request = new HttpRequestMessage(HttpMethod.Delete, resourceUrl)

        let! response = client.SendAsync(request) |> Async.AwaitTask
        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        return (response.StatusCode, response.Headers, body)
    }

let getEvent (client: HttpClient) (resourceUrl: string) =
    async {
        let request = new HttpRequestMessage(HttpMethod.Get, resourceUrl)

        let! response = client.SendAsync(request) |> Async.AwaitTask
        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        return (response.StatusCode, body)
    }

/// Fetch events via REPORT calendar-query to check if our event appears.
let reportEvents (client: HttpClient) (calendarUrl: string) (uid: string) =
    async {
        // Use a wide time range to catch the event
        let startStr = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")
        let endStr = DateTime.UtcNow.AddDays(1.0).ToString("yyyyMMdd'T'HHmmss'Z'")

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
            return Error $"REPORT returned {response.StatusCode}"
        else
            // Check if our UID appears in the response
            let found = content.Contains(uid)
            return Ok found
    }

// ─── Spike Runner ────────────────────────────────────────────────────

let runSpike (config: ProviderConfig) =
    async {
        let name = providerName config.Provider
        printHeader $"{name} CalDAV Write-Back Spike"
        printfn "  User: %s" config.Username
        printfn "  Calendar URL: %s" config.CalendarUrl

        use client = createClient config

        let testUid = $"michael-spike-{Guid.NewGuid()}@michael"
        let resourceUrl = $"{config.CalendarUrl.TrimEnd('/')}/{Guid.NewGuid()}.ics"
        let summary = "Michael Spike Test Event"

        printfn ""
        printfn "  Test UID: %s" testUid
        printfn "  Resource URL: %s" resourceUrl

        // ── Step 1: PUT a VCALENDAR ──────────────────────────────────

        printStep 1 5 "PUT a VCALENDAR (no METHOD property)"

        let icsContent = generateTestIcs testUid summary
        printInfo "Generated ICS content (no METHOD, no ORGANIZER/ATTENDEE)"

        let! (putStatus, putHeaders, putBody) = putEvent client resourceUrl icsContent

        printResult "Status" $"{int putStatus} {putStatus}"

        if int putStatus >= 200 && int putStatus < 300 then
            printOk $"Server accepted the PUT ({putStatus})"
        else
            printFail $"Server rejected the PUT: {putBody}"
            printfn ""
            printfn "  ⚠️  Aborting remaining steps for %s" name
            return ()

        // Check Location header
        match putHeaders.Location with
        | null ->
            printInfo "No Location header in response — resource lives at request URL"
        | location ->
            printResult "Location header" (location.ToString())
            printInfo "Server returned a different URL for the resource"

        // Print ETag if present
        match putHeaders.ETag with
        | null -> printInfo "No ETag in response"
        | etag -> printResult "ETag" (etag.ToString())

        // ── Step 2: GET the resource back ────────────────────────────

        printStep 2 5 "GET the resource back"

        let! (getStatus, getBody) = getEvent client resourceUrl

        printResult "Status" $"{int getStatus} {getStatus}"

        if int getStatus >= 200 && int getStatus < 300 then
            printOk "Resource exists at the request URL"

            if getBody.Contains(testUid) then
                printOk "Response body contains our UID"
            else
                printFail "Response body does NOT contain our UID"

            if getBody.Contains("METHOD") then
                printInfo "⚠️  Server added a METHOD property (unexpected)"
            else
                printOk "No METHOD property in stored resource"
        else
            printFail $"GET failed: {getBody}"

        // ── Step 3: REPORT to verify event in calendar ───────────────

        printStep 3 5 "REPORT calendar-query to verify event appears"

        let! reportResult = reportEvents client config.CalendarUrl testUid

        match reportResult with
        | Ok true ->
            printOk "Event found in REPORT results (UID matched)"
        | Ok false ->
            printFail "Event NOT found in REPORT results"
            printInfo "The event may not appear until the next sync cycle"
        | Error msg ->
            printFail $"REPORT failed: {msg}"

        // ── Step 4: DELETE the resource ──────────────────────────────

        printStep 4 5 "DELETE the resource"

        let! (deleteStatus, deleteHeaders, deleteBody) = deleteEvent client resourceUrl

        printResult "Status" $"{int deleteStatus} {deleteStatus}"

        if int deleteStatus >= 200 && int deleteStatus < 300 then
            printOk $"Server accepted the DELETE ({deleteStatus})"
        else
            printFail $"DELETE failed: {deleteBody}"

        // ── Step 5: DELETE again (idempotency check) ─────────────────

        printStep 5 5 "DELETE again (idempotency check)"

        let! (delete2Status, _, delete2Body) = deleteEvent client resourceUrl

        printResult "Status" $"{int delete2Status} {delete2Status}"

        match int delete2Status with
        | 404 ->
            printOk "Second DELETE returned 404 (resource already gone)"
        | s when s >= 200 && s < 300 ->
            printOk $"Second DELETE returned {delete2Status} (server treats as idempotent)"
        | _ ->
            printInfo $"Second DELETE returned {delete2Status}: {delete2Body}"

        // ── Summary ──────────────────────────────────────────────────

        printfn ""
        printSeparator ()
        printfn "  %s Summary" name
        printSeparator ()
        printfn "  PUT accepted:      %s" (if int putStatus >= 200 && int putStatus < 300 then "YES" else "NO")
        printfn "  PUT status:        %d" (int putStatus)
        printfn "  Location header:   %s" (if putHeaders.Location <> null then putHeaders.Location.ToString() else "(none)")
        printfn "  GET after PUT:     %d" (int getStatus)
        printfn "  REPORT found UID:  %s" (match reportResult with Ok true -> "YES" | Ok false -> "NO" | Error _ -> "ERROR")
        printfn "  DELETE status:     %d" (int deleteStatus)
        printfn "  2nd DELETE status: %d" (int delete2Status)
    }

// ─── Credential Loading ──────────────────────────────────────────────

type CredentialResult =
    | Ok of ProviderConfig
    | Missing of provider: string * missingVars: string list

let loadProvider (provider: Provider) (userVar: string) (passVar: string) (calUrlVar: string) : CredentialResult =
    let user = Environment.GetEnvironmentVariable(userVar)
    let pass = Environment.GetEnvironmentVariable(passVar)
    let calUrl = Environment.GetEnvironmentVariable(calUrlVar)

    let missing =
        [ if String.IsNullOrEmpty(user) then userVar
          if String.IsNullOrEmpty(pass) then passVar
          if String.IsNullOrEmpty(calUrl) then calUrlVar ]

    if missing.IsEmpty then
        Ok
            { Provider = provider
              Username = user
              Password = pass
              CalendarUrl = calUrl }
    else
        Missing(providerName provider, missing)

// ─── Entry Point ─────────────────────────────────────────────────────

printHeader "CalDAV Write-Back Spike"
printfn "  Date: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
printfn ""
printfn "  This spike validates CalDAV PUT and DELETE behavior."
printfn "  It will:"
printfn "    1. PUT a test VEVENT (no METHOD property)"
printfn "    2. GET it back to verify storage"
printfn "    3. REPORT to check it appears in calendar queries"
printfn "    4. DELETE it"
printfn "    5. DELETE again to check idempotency"

let providers =
    [ loadProvider
          Fastmail
          "FASTMAIL_CALDAV_USER"
          "FASTMAIL_CALDAV_PASSWORD"
          "FASTMAIL_CALDAV_CALENDAR_URL"
      loadProvider
          ICloud
          "ICLOUD_CALDAV_USER"
          "ICLOUD_CALDAV_PASSWORD"
          "ICLOUD_CALDAV_CALENDAR_URL" ]

let configs =
    providers
    |> List.choose (fun r ->
        match r with
        | Ok config -> Some config
        | Missing(name, vars) ->
            printfn ""
            printfn "  SKIPPING %s: missing env var(s): %s" name (String.Join(", ", vars))
            None)

if configs.IsEmpty then
    printfn ""
    printfn "  ERROR: No providers configured."
    printfn ""
    printfn "  Required (at least one provider):"
    printfn ""
    printfn "    Fastmail:"
    printfn "      FASTMAIL_CALDAV_USER=user@domain.com"
    printfn "      FASTMAIL_CALDAV_PASSWORD=app-password"
    printfn "      FASTMAIL_CALDAV_CALENDAR_URL=https://caldav.fastmail.com/dav/calendars/user/user@domain.com/Default/"
    printfn ""
    printfn "    iCloud:"
    printfn "      ICLOUD_CALDAV_USER=user@icloud.com"
    printfn "      ICLOUD_CALDAV_PASSWORD=app-specific-password"
    printfn "      ICLOUD_CALDAV_CALENDAR_URL=https://caldav.icloud.com/12345678/calendars/home/"
    printfn ""
    printfn "  Tip: Run caldav_spike.fsx first to find your calendar URLs."
else
    for config in configs do
        try
            runSpike config |> Async.RunSynchronously
        with ex ->
            printfn ""
            printfn "  UNEXPECTED ERROR with %s: %s" (providerName config.Provider) ex.Message
            printfn "  %s" ex.StackTrace

    printHeader "Done"
    printfn ""
    printfn "  Record these results in docs/spike-caldav-writeback.md"
