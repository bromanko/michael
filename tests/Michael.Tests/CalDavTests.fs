module Michael.Tests.CalDavTests

open System
open System.Net.Http.Headers
open Expecto
open NodaTime
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
