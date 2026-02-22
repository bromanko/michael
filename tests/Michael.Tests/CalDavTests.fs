module Michael.Tests.CalDavTests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Threading
open System.Threading.Tasks
open Expecto
open NodaTime
open NodaTime.Text
open Ical.Net
open Michael.Domain
open Michael.CalDav
open Michael.Availability
open Michael.Tests.TestHelpers

let private instant y m d h min = Instant.FromUtc(y, m, d, h, min)

let private hostTz = DateTimeZoneProviders.Tzdb.["America/New_York"]

[<Tests>]
let createHttpClientTests =
    testList
        "createHttpClient"
        [ test "sets Basic auth header from username and password" {
              let factory = FakeHttpClientFactory()
              let client = createHttpClient factory "alice" "s3cret"

              Expect.isSome
                  (client.DefaultRequestHeaders.Authorization |> Option.ofObj)
                  "Authorization header should be set"

              let auth = client.DefaultRequestHeaders.Authorization
              Expect.equal auth.Scheme "Basic" "scheme should be Basic"

              let decoded =
                  System.Convert.FromBase64String(auth.Parameter)
                  |> System.Text.Encoding.UTF8.GetString

              Expect.equal decoded "alice:s3cret" "decoded credentials should match"
          }

          test "requests the caldav named client from factory" {
              let factory = FakeHttpClientFactory()
              let _client = createHttpClient factory "user" "pass"

              Expect.equal factory.RequestedNames [ "caldav" ] "should request the 'caldav' named client"
          }

          test "different sources get independent clients" {
              let factory = FakeHttpClientFactory()
              let client1 = createHttpClient factory "user1" "pass1"
              let client2 = createHttpClient factory "user2" "pass2"

              let decoded1 =
                  System.Convert.FromBase64String(client1.DefaultRequestHeaders.Authorization.Parameter)
                  |> System.Text.Encoding.UTF8.GetString

              let decoded2 =
                  System.Convert.FromBase64String(client2.DefaultRequestHeaders.Authorization.Parameter)
                  |> System.Text.Encoding.UTF8.GetString

              Expect.equal decoded1 "user1:pass1" "first client has correct credentials"
              Expect.equal decoded2 "user2:pass2" "second client has correct credentials"
              Expect.notEqual client1 client2 "clients should be distinct instances"
          }

          test "handles special characters in credentials" {
              let factory = FakeHttpClientFactory()
              let client = createHttpClient factory "user@domain.com" "p@ss:w0rd!"

              let decoded =
                  System.Convert.FromBase64String(client.DefaultRequestHeaders.Authorization.Parameter)
                  |> System.Text.Encoding.UTF8.GetString

              Expect.equal decoded "user@domain.com:p@ss:w0rd!" "special characters preserved"
          } ]

[<Tests>]
let parseAndExpandEventsTests =
    testList
        "parseAndExpandEvents"
        [ test "parses a simple timed event" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"

              let ics =
                  """BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:test-event-1@example.com
SUMMARY:Team standup
DTSTART:20260203T150000Z
DTEND:20260203T153000Z
END:VEVENT
END:VCALENDAR"""

              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 2 28 0 0

              let events =
                  parseAndExpandEvents sourceId calendarUrl [ ics ] hostTz rangeStart rangeEnd

              Expect.hasLength events 1 "should parse one event"
              Expect.equal events.[0].Summary "Team standup" "summary matches"
              Expect.equal events.[0].Uid "test-event-1@example.com" "uid matches"
              Expect.equal events.[0].SourceId sourceId "sourceId matches"
              Expect.equal events.[0].CalendarUrl calendarUrl "calendarUrl matches"
              Expect.isFalse events.[0].IsAllDay "should not be all-day"
              Expect.isTrue (events.[0].StartInstant.Equals(instant 2026 2 3 15 0)) "start instant"
              Expect.isTrue (events.[0].EndInstant.Equals(instant 2026 2 3 15 30)) "end instant"
          }

          test "parses an all-day event and converts to host timezone" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"

              let ics =
                  """BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:allday-1@example.com
SUMMARY:Holiday
DTSTART;VALUE=DATE:20260204
DTEND;VALUE=DATE:20260205
END:VEVENT
END:VCALENDAR"""

              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 2 28 0 0

              let events =
                  parseAndExpandEvents sourceId calendarUrl [ ics ] hostTz rangeStart rangeEnd

              Expect.hasLength events 1 "should parse one all-day event"
              Expect.isTrue events.[0].IsAllDay "should be all-day"
              Expect.equal events.[0].Summary "Holiday" "summary matches"
              // Feb 4 midnight ET = Feb 4 05:00 UTC (EST = UTC-5)
              Expect.isTrue (events.[0].StartInstant.Equals(instant 2026 2 4 5 0)) "start at midnight ET in UTC"
              // Feb 5 midnight ET = Feb 5 05:00 UTC
              Expect.isTrue (events.[0].EndInstant.Equals(instant 2026 2 5 5 0)) "end at next midnight ET in UTC"
          }

          test "expands recurring event within range" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"

              let ics =
                  """BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:recurring-1@example.com
SUMMARY:Weekly sync
DTSTART:20260202T140000Z
DTEND:20260202T150000Z
RRULE:FREQ=WEEKLY;COUNT=4
END:VEVENT
END:VCALENDAR"""

              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 3 1 0 0

              let events =
                  parseAndExpandEvents sourceId calendarUrl [ ics ] hostTz rangeStart rangeEnd

              Expect.hasLength events 4 "should expand to 4 weekly occurrences"

              for evt in events do
                  Expect.equal evt.Summary "Weekly sync" "all occurrences have same summary"
                  Expect.equal evt.Uid "recurring-1@example.com" "all occurrences have same uid"
          }

          test "filters events outside range" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"

              let ics =
                  """BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:outside-range@example.com
SUMMARY:Past event
DTSTART:20260101T100000Z
DTEND:20260101T110000Z
END:VEVENT
END:VCALENDAR"""

              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 2 28 0 0

              let events =
                  parseAndExpandEvents sourceId calendarUrl [ ics ] hostTz rangeStart rangeEnd

              Expect.hasLength events 0 "event outside range should not be returned"
          }

          test "handles empty ICS list" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"
              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 2 28 0 0

              let events = parseAndExpandEvents sourceId calendarUrl [] hostTz rangeStart rangeEnd

              Expect.hasLength events 0 "empty ICS list should return no events"
          }

          test "parses a multi-day all-day event" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"

              // 3-day event: Feb 4-6
              let ics =
                  """BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:multiday-1@example.com
SUMMARY:Conference
DTSTART;VALUE=DATE:20260204
DTEND;VALUE=DATE:20260207
END:VEVENT
END:VCALENDAR"""

              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 2 28 0 0

              let events =
                  parseAndExpandEvents sourceId calendarUrl [ ics ] hostTz rangeStart rangeEnd

              Expect.hasLength events 1 "should parse one multi-day all-day event"
              Expect.isTrue events.[0].IsAllDay "should be all-day"
              Expect.equal events.[0].Summary "Conference" "summary matches"
              // Feb 4 midnight ET = Feb 4 05:00 UTC (EST = UTC-5)
              Expect.isTrue (events.[0].StartInstant.Equals(instant 2026 2 4 5 0)) "start at midnight ET Feb 4 in UTC"
              // Feb 7 midnight ET = Feb 7 05:00 UTC (3 days later)
              Expect.isTrue (events.[0].EndInstant.Equals(instant 2026 2 7 5 0)) "end at midnight ET Feb 7 in UTC"
          }

          test "handles malformed ICS gracefully" {
              let sourceId = Guid.NewGuid()
              let calendarUrl = "https://example.com/cal/1"
              let rangeStart = instant 2026 2 1 0 0
              let rangeEnd = instant 2026 2 28 0 0

              let events =
                  parseAndExpandEvents sourceId calendarUrl [ "not valid ics" ] hostTz rangeStart rangeEnd

              Expect.hasLength events 0 "malformed ICS should be skipped"
          } ]

[<Tests>]
let computeSlotsWithBlockersTests =
    testList
        "computeSlots with calendar blockers"
        [ test "calendar blockers reduce available slots" {
              let hostSlots =
                  [ { Id = Guid.NewGuid()
                      DayOfWeek = IsoDayOfWeek.Monday
                      StartTime = LocalTime(9, 0)
                      EndTime = LocalTime(17, 0) } ]

              // Participant available Mon Feb 2 2026 9:00-12:00 ET (UTC-5)
              let participantWindows =
                  [ { Start = OffsetDateTime(LocalDateTime(2026, 2, 2, 9, 0), Offset.FromHours(-5))
                      End = OffsetDateTime(LocalDateTime(2026, 2, 2, 12, 0), Offset.FromHours(-5))
                      Timezone = Some "America/New_York" } ]

              // Calendar blocker: 10:00-11:00 ET = 15:00-16:00 UTC
              let calendarBlockers = [ Interval(instant 2026 2 2 15 0, instant 2026 2 2 16 0) ]

              let slots =
                  computeSlots participantWindows hostSlots hostTz [] calendarBlockers 30 "America/New_York"

              // 9:00-12:00 = 6 slots, minus 10:00-11:00 = 4 slots
              Expect.hasLength slots 4 "should have 4 slots with calendar blocker"
          }

          test "overlapping booking and calendar blocker both subtracted" {
              let hostSlots =
                  [ { Id = Guid.NewGuid()
                      DayOfWeek = IsoDayOfWeek.Monday
                      StartTime = LocalTime(9, 0)
                      EndTime = LocalTime(17, 0) } ]

              let participantWindows =
                  [ { Start = OffsetDateTime(LocalDateTime(2026, 2, 2, 9, 0), Offset.FromHours(-5))
                      End = OffsetDateTime(LocalDateTime(2026, 2, 2, 12, 0), Offset.FromHours(-5))
                      Timezone = Some "America/New_York" } ]

              // Booking 9:00-9:30 ET
              let existingBookings =
                  [ { Id = Guid.NewGuid()
                      ParticipantName = "Someone"
                      ParticipantEmail = "someone@example.com"
                      ParticipantPhone = None
                      Title = "Meeting"
                      Description = None
                      StartTime = OffsetDateTime(LocalDateTime(2026, 2, 2, 9, 0), Offset.FromHours(-5))
                      EndTime = OffsetDateTime(LocalDateTime(2026, 2, 2, 9, 30), Offset.FromHours(-5))
                      DurationMinutes = 30
                      Timezone = "America/New_York"
                      Status = Confirmed
                      CreatedAt = SystemClock.Instance.GetCurrentInstant()
                      CancellationToken = Some fixedCancellationToken
                      CalDavEventHref = None } ]

              // Calendar blocker 10:00-10:30 ET = 15:00-15:30 UTC
              let calendarBlockers = [ Interval(instant 2026 2 2 15 0, instant 2026 2 2 15 30) ]

              let slots =
                  computeSlots
                      participantWindows
                      hostSlots
                      hostTz
                      existingBookings
                      calendarBlockers
                      30
                      "America/New_York"

              // 9:00-12:00 = 6 slots, minus 9:00-9:30 and 10:00-10:30 = 4 slots
              Expect.hasLength slots 4 "should have 4 slots with both blockers"
          } ]

// ---------------------------------------------------------------------------
// Mock HTTP handler for PUT/DELETE tests
// ---------------------------------------------------------------------------

type private MockHttpHandler(handler: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()

    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult(handler request)

let private makeClient (handler: HttpRequestMessage -> HttpResponseMessage) =
    new HttpClient(new MockHttpHandler(handler))

let private makeBookingForIcs () : Booking =
    { Id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
      ParticipantName = "Alice Smith"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = Some "+1-555-0100"
      Title = "Product Review"
      Description = Some "Discuss Q1 metrics"
      StartTime = OffsetDateTime(LocalDateTime(2026, 3, 5, 14, 0), Offset.FromHours(-5))
      EndTime = OffsetDateTime(LocalDateTime(2026, 3, 5, 14, 30), Offset.FromHours(-5))
      DurationMinutes = 30
      Timezone = "America/New_York"
      Status = Confirmed
      CreatedAt = Instant.FromUtc(2026, 3, 1, 10, 0)
      CancellationToken = Some fixedCancellationToken
      CalDavEventHref = None }

// ---------------------------------------------------------------------------
// putEvent tests
// ---------------------------------------------------------------------------

[<Tests>]
let putEventTests =
    testList
        "putEvent"
        [ test "returns Ok with resource URL on 201" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(HttpStatusCode.Created, Content = new StringContent("")))

              let result =
                  (putEvent client "https://cal.example.com/event.ics" "BEGIN:VCALENDAR")
                      .Result

              Expect.isOk result "should be Ok"
              Expect.equal (Result.defaultValue "" result) "https://cal.example.com/event.ics" "returns resource URL"
          }

          test "returns Ok with Location header when server provides one" {
              let client =
                  makeClient (fun _ ->
                      let resp = new HttpResponseMessage(HttpStatusCode.Created)
                      resp.Content <- new StringContent("")
                      resp.Headers.Location <- Uri("https://cal.example.com/relocated.ics")
                      resp)

              let result =
                  (putEvent client "https://cal.example.com/event.ics" "BEGIN:VCALENDAR")
                      .Result

              Expect.isOk result "should be Ok"
              Expect.equal (Result.defaultValue "" result) "https://cal.example.com/relocated.ics" "returns Location header"
          }

          test "returns Ok on 204 No Content" {
              let client =
                  makeClient (fun _ -> new HttpResponseMessage(HttpStatusCode.NoContent))

              let result =
                  (putEvent client "https://cal.example.com/event.ics" "BEGIN:VCALENDAR")
                      .Result

              Expect.isOk result "should be Ok on 204"
          }

          test "returns Error on 403" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(HttpStatusCode.Forbidden, Content = new StringContent("Access denied")))

              let result =
                  (putEvent client "https://cal.example.com/event.ics" "BEGIN:VCALENDAR")
                      .Result

              Expect.isError result "should be Error on 403"
          }

          test "returns Error on 500" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(
                          HttpStatusCode.InternalServerError,
                          Content = new StringContent("Server error")
                      ))

              let result =
                  (putEvent client "https://cal.example.com/event.ics" "BEGIN:VCALENDAR")
                      .Result

              Expect.isError result "should be Error on 500"
          }

          test "sets Content-Type to text/calendar" {
              let mutable capturedContentType = ""

              let client =
                  makeClient (fun req ->
                      capturedContentType <- req.Content.Headers.ContentType.MediaType
                      new HttpResponseMessage(HttpStatusCode.Created, Content = new StringContent("")))

              (putEvent client "https://cal.example.com/event.ics" "BEGIN:VCALENDAR")
                  .Result
              |> ignore

              Expect.equal capturedContentType "text/calendar" "Content-Type should be text/calendar"
          } ]

// ---------------------------------------------------------------------------
// deleteEvent tests
// ---------------------------------------------------------------------------

[<Tests>]
let deleteEventTests =
    testList
        "deleteEvent"
        [ test "returns Ok on 204" {
              let client =
                  makeClient (fun _ -> new HttpResponseMessage(HttpStatusCode.NoContent))

              let result =
                  (deleteEvent client "https://cal.example.com/event.ics").Result

              Expect.isOk result "should be Ok on 204"
          }

          test "returns Ok on 404 (idempotent)" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(HttpStatusCode.NotFound, Content = new StringContent("Not found")))

              let result =
                  (deleteEvent client "https://cal.example.com/event.ics").Result

              Expect.isOk result "should be Ok on 404 (already deleted)"
          }

          test "returns Error on 500" {
              let client =
                  makeClient (fun _ ->
                      new HttpResponseMessage(
                          HttpStatusCode.InternalServerError,
                          Content = new StringContent("Server error")
                      ))

              let result =
                  (deleteEvent client "https://cal.example.com/event.ics").Result

              Expect.isError result "should be Error on 500"
          } ]

// ---------------------------------------------------------------------------
// buildCalDavEventIcs tests
// ---------------------------------------------------------------------------

[<Tests>]
let buildCalDavEventIcsTests =
    testList
        "buildCalDavEventIcs"
        [ test "output parses back with Calendar.Load" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              Expect.hasLength (parsed.Events |> Seq.toList) 1 "should have one event"
          }

          test "output has no METHOD property" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              Expect.isFalse (ics.Contains("METHOD")) "should not contain METHOD"
          }

          test "has correct UID with @michael suffix" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.equal evt.Uid $"{booking.Id}@michael" "UID should be bookingId@michael"
          }

          test "SUMMARY includes participant name and title" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.stringContains evt.Summary "Alice Smith" "SUMMARY should contain participant name"
              Expect.stringContains evt.Summary "Product Review" "SUMMARY should contain title"
          }

          test "DESCRIPTION includes participant email" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.stringContains evt.Description "alice@example.com" "DESCRIPTION should contain email"
          }

          test "DESCRIPTION includes phone when present" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.stringContains evt.Description "+1-555-0100" "DESCRIPTION should contain phone"
          }

          test "DESCRIPTION omits phone when not present" {
              let booking = { makeBookingForIcs () with ParticipantPhone = None }
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.isFalse (evt.Description.Contains("Phone:")) "DESCRIPTION should not contain Phone line"
          }

          test "LOCATION set to video link when present" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" (Some "https://meet.example.com/abc")

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.equal evt.Location "https://meet.example.com/abc" "LOCATION should be video link"
          }

          test "LOCATION absent when no video link" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.isTrue (String.IsNullOrEmpty(evt.Location)) "LOCATION should be empty"
          }

          test "DTSTART and DTEND in UTC" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              // The raw ICS should have UTC timestamps ending with Z
              Expect.stringContains ics "DTSTART" "should have DTSTART"
              // Verify no TZID parameter on DTSTART/DTEND
              Expect.isFalse (ics.Contains("TZID=America")) "should not contain TZID parameter"
          }

          test "no ORGANIZER or ATTENDEE properties" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              Expect.isFalse (ics.Contains("ORGANIZER")) "should not contain ORGANIZER"
              Expect.isFalse (ics.Contains("ATTENDEE")) "should not contain ATTENDEE"
          }

          test "control characters in user input are stripped" {
              let booking =
                  { makeBookingForIcs () with
                      ParticipantName = "Alice\r\nSmith"
                      Title = "Meeting\twith\nnewlines" }

              let ics = buildCalDavEventIcs booking "host@example.com" None

              let parsed = Calendar.Load(ics)
              let evt = parsed.Events |> Seq.head
              Expect.isFalse (evt.Summary.Contains("\r")) "SUMMARY should not contain CR"
              Expect.isFalse (evt.Summary.Contains("\n")) "SUMMARY should not contain LF"
              Expect.isFalse (evt.Summary.Contains("\t")) "SUMMARY should not contain TAB"
          }

          test "PRODID is -//Michael//Michael//EN" {
              let booking = makeBookingForIcs ()
              let ics = buildCalDavEventIcs booking "host@example.com" None

              Expect.stringContains ics "-//Michael//Michael//EN" "should have correct PRODID"
          } ]
