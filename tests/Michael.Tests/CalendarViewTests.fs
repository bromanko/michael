module Michael.Tests.CalendarViewTests

open System
open Expecto
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.AdminHandlers
open Michael.Tests.TestHelpers

let private nyTz = DateTimeZoneProviders.Tzdb.["America/New_York"]
let private odtPattern = OffsetDateTimePattern.ExtendedIso

let private makeCachedEvent
    (id: Guid)
    (summary: string)
    (startInstant: Instant)
    (endInstant: Instant)
    (isAllDay: bool)
    : CachedEvent =
    { Id = id
      SourceId = Guid.NewGuid()
      CalendarUrl = "https://caldav.example.com/cal"
      Uid = $"event-{id}"
      Summary = summary
      StartInstant = startInstant
      EndInstant = endInstant
      IsAllDay = isAllDay }

let private makeBooking
    (id: Guid)
    (title: string)
    (participantName: string)
    (startOdt: OffsetDateTime)
    (endOdt: OffsetDateTime)
    : Booking =
    { Id = id
      ParticipantName = participantName
      ParticipantEmail = "test@example.com"
      ParticipantPhone = None
      Title = title
      Description = None
      StartTime = startOdt
      EndTime = endOdt
      DurationMinutes = 30
      Timezone = "America/New_York"
      Status = Confirmed
      CreatedAt = SystemClock.Instance.GetCurrentInstant()
      CancellationToken = Some fixedCancellationToken
      CalDavEventHref = None }

let private makeAvailabilitySlot
    (id: Guid)
    (dayOfWeek: IsoDayOfWeek)
    (startHour: int)
    (endHour: int)
    : HostAvailabilitySlot =
    { Id = id
      DayOfWeek = dayOfWeek
      StartTime = LocalTime(startHour, 0)
      EndTime = LocalTime(endHour, 0) }

[<Tests>]
let calendarViewTests =
    testList
        "CalendarView"
        [ testList
              "cachedEventToDto"
              [ test "converts cached event to DTO" {
                    let id = Guid.NewGuid()
                    let start = Instant.FromUtc(2026, 2, 15, 14, 0)
                    let end' = Instant.FromUtc(2026, 2, 15, 15, 0)
                    let evt = makeCachedEvent id "Team Meeting" start end' false
                    let formatTime = formatInstantInZone nyTz

                    let dto = cachedEventToDto formatTime evt

                    Expect.equal dto.Id (id.ToString()) "id matches"
                    Expect.equal dto.Title "Team Meeting" "title matches"
                    Expect.equal dto.EventType "calendar" "event type is calendar"
                    Expect.equal dto.IsAllDay false "not all day"
                }

                test "handles all-day events" {
                    let id = Guid.NewGuid()
                    let start = Instant.FromUtc(2026, 2, 15, 0, 0)
                    let end' = Instant.FromUtc(2026, 2, 16, 0, 0)
                    let evt = makeCachedEvent id "Holiday" start end' true
                    let formatTime = formatInstantInZone nyTz

                    let dto = cachedEventToDto formatTime evt

                    Expect.equal dto.IsAllDay true "is all day"
                } ]

          testList
              "bookingToCalendarDto"
              [ test "converts booking to DTO with formatted title" {
                    let id = Guid.NewGuid()
                    let startOdt = odtPattern.Parse("2026-02-15T10:00:00-05:00").Value
                    let endOdt = odtPattern.Parse("2026-02-15T10:30:00-05:00").Value
                    let booking = makeBooking id "Project Review" "Alice" startOdt endOdt
                    let formatTime = formatInstantInZone nyTz

                    let dto = bookingToCalendarDto formatTime booking

                    Expect.equal dto.Id (id.ToString()) "id matches"
                    Expect.stringContains dto.Title "Project Review" "title contains booking title"
                    Expect.stringContains dto.Title "Alice" "title contains participant name"
                    Expect.equal dto.EventType "booking" "event type is booking"
                    Expect.equal dto.IsAllDay false "bookings are not all day"
                } ]

          testList
              "expandAvailabilitySlots"
              [ test "expands single slot for matching day" {
                    let slotId = Guid.NewGuid()
                    // Monday Feb 16, 2026
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0) // midnight EST
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)
                    let slots = [ makeAvailabilitySlot slotId IsoDayOfWeek.Monday 9 17 ]
                    let formatTime = formatInstantInZone nyTz
                    let noBlockedDates = Set.empty

                    let dtos =
                        expandAvailabilitySlots nyTz formatTime rangeStart rangeEnd noBlockedDates slots

                    Expect.hasLength dtos 1 "one availability event"
                    Expect.equal dtos.[0].Title "Available" "title is Available"
                    Expect.equal dtos.[0].EventType "availability" "event type is availability"
                    Expect.stringContains dtos.[0].Id (slotId.ToString()) "id contains slot id"
                }

                test "returns empty for non-matching day" {
                    let slotId = Guid.NewGuid()
                    // Monday Feb 16, 2026 — range covers only Monday in local time
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0) // midnight EST Monday
                    let rangeEnd = Instant.FromUtc(2026, 2, 16, 23, 0) // 6pm EST Monday (before midnight)
                    // Slot is for Wednesday, but range is only Monday
                    let slots = [ makeAvailabilitySlot slotId IsoDayOfWeek.Wednesday 9 17 ]
                    let formatTime = formatInstantInZone nyTz
                    let noBlockedDates = Set.empty

                    let dtos =
                        expandAvailabilitySlots nyTz formatTime rangeStart rangeEnd noBlockedDates slots

                    Expect.hasLength dtos 0 "no availability events for non-matching day"
                }

                test "expands across multiple days" {
                    let slotId = Guid.NewGuid()
                    // Monday Feb 16 through Wednesday Feb 18, 2026
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 19, 5, 0)
                    let slots = [ makeAvailabilitySlot slotId IsoDayOfWeek.Monday 9 17 ]
                    let formatTime = formatInstantInZone nyTz
                    let noBlockedDates = Set.empty

                    let dtos =
                        expandAvailabilitySlots nyTz formatTime rangeStart rangeEnd noBlockedDates slots

                    // Only Monday matches
                    Expect.hasLength dtos 1 "one Monday in range"
                }

                test "handles multiple slots per day" {
                    let slot1 = Guid.NewGuid()
                    let slot2 = Guid.NewGuid()
                    // Monday Feb 16, 2026
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)

                    let slots =
                        [ makeAvailabilitySlot slot1 IsoDayOfWeek.Monday 9 12
                          makeAvailabilitySlot slot2 IsoDayOfWeek.Monday 13 17 ]

                    let formatTime = formatInstantInZone nyTz
                    let noBlockedDates = Set.empty

                    let dtos =
                        expandAvailabilitySlots nyTz formatTime rangeStart rangeEnd noBlockedDates slots

                    Expect.hasLength dtos 2 "two availability events for two slots"
                }

                test "empty slots returns empty list" {
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)
                    let formatTime = formatInstantInZone nyTz
                    let noBlockedDates = Set.empty

                    let dtos =
                        expandAvailabilitySlots nyTz formatTime rangeStart rangeEnd noBlockedDates []

                    Expect.hasLength dtos 0 "no events from empty slots"
                }

                test "suppresses availability on blocked dates" {
                    let slotId = Guid.NewGuid()
                    // Monday Feb 16, 2026
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)
                    let slots = [ makeAvailabilitySlot slotId IsoDayOfWeek.Monday 9 17 ]
                    let formatTime = formatInstantInZone nyTz
                    let blockedDates = Set.ofList [ LocalDate(2026, 2, 16) ]

                    let dtos =
                        expandAvailabilitySlots nyTz formatTime rangeStart rangeEnd blockedDates slots

                    Expect.hasLength dtos 0 "blocked date suppresses availability"
                } ]

          testList
              "buildCalendarViewEvents"
              [ test "merges all event types" {
                    let cachedId = Guid.NewGuid()
                    let bookingId = Guid.NewGuid()
                    let slotId = Guid.NewGuid()

                    // Monday Feb 16, 2026
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)

                    let cachedEvents =
                        [ makeCachedEvent
                              cachedId
                              "External Meeting"
                              (Instant.FromUtc(2026, 2, 16, 15, 0))
                              (Instant.FromUtc(2026, 2, 16, 16, 0))
                              false ]

                    let bookings =
                        let startOdt = odtPattern.Parse("2026-02-16T10:00:00-05:00").Value
                        let endOdt = odtPattern.Parse("2026-02-16T10:30:00-05:00").Value
                        [ makeBooking bookingId "Consultation" "Bob" startOdt endOdt ]

                    let slots = [ makeAvailabilitySlot slotId IsoDayOfWeek.Monday 9 17 ]

                    let events =
                        buildCalendarViewEvents nyTz rangeStart rangeEnd cachedEvents bookings slots

                    Expect.hasLength events 3 "three events total"

                    let calendarEvents = events |> List.filter (fun e -> e.EventType = "calendar")
                    let bookingEvents = events |> List.filter (fun e -> e.EventType = "booking")

                    let availabilityEvents =
                        events |> List.filter (fun e -> e.EventType = "availability")

                    Expect.hasLength calendarEvents 1 "one calendar event"
                    Expect.hasLength bookingEvents 1 "one booking event"
                    Expect.hasLength availabilityEvents 1 "one availability event"

                    // Availability first, then calendar, then booking (for z-order layering)
                    Expect.equal events.[0].EventType "availability" "availability renders first"
                    Expect.equal events.[1].EventType "calendar" "calendar renders second"
                    Expect.equal events.[2].EventType "booking" "booking renders last"
                }

                test "returns empty when all sources empty" {
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)

                    let events = buildCalendarViewEvents nyTz rangeStart rangeEnd [] [] []

                    Expect.hasLength events 0 "no events from empty sources"
                }

                test "handles only cached events" {
                    let id = Guid.NewGuid()
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)

                    let cachedEvents =
                        [ makeCachedEvent
                              id
                              "Meeting"
                              (Instant.FromUtc(2026, 2, 16, 15, 0))
                              (Instant.FromUtc(2026, 2, 16, 16, 0))
                              false ]

                    let events = buildCalendarViewEvents nyTz rangeStart rangeEnd cachedEvents [] []

                    Expect.hasLength events 1 "one event"
                    Expect.equal events.[0].EventType "calendar" "is calendar event"
                }

                test "handles only bookings" {
                    let id = Guid.NewGuid()
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)
                    let startOdt = odtPattern.Parse("2026-02-16T10:00:00-05:00").Value
                    let endOdt = odtPattern.Parse("2026-02-16T10:30:00-05:00").Value
                    let bookings = [ makeBooking id "Call" "Charlie" startOdt endOdt ]

                    let events = buildCalendarViewEvents nyTz rangeStart rangeEnd [] bookings []

                    Expect.hasLength events 1 "one event"
                    Expect.equal events.[0].EventType "booking" "is booking event"
                }

                test "handles only availability" {
                    let id = Guid.NewGuid()
                    // Monday
                    let rangeStart = Instant.FromUtc(2026, 2, 16, 5, 0)
                    let rangeEnd = Instant.FromUtc(2026, 2, 17, 5, 0)
                    let slots = [ makeAvailabilitySlot id IsoDayOfWeek.Monday 9 17 ]

                    let events = buildCalendarViewEvents nyTz rangeStart rangeEnd [] [] slots

                    Expect.hasLength events 1 "one event"
                    Expect.equal events.[0].EventType "availability" "is availability event"
                } ] ]
