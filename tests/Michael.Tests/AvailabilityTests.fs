module Michael.Tests.AvailabilityTests

open Expecto
open NodaTime
open Michael.Domain
open Michael.Availability

let private nyTz = DateTimeZoneProviders.Tzdb.["America/New_York"]

let private instant y m d h min =
    Instant.FromUtc(y, m, d, h, min)

let private interval s e =
    Interval(s, e)

[<Tests>]
let intersectTests =
    testList "intersect" [
        test "overlapping intervals returns intersection" {
            let a = interval (instant 2026 2 3 9 0) (instant 2026 2 3 17 0)
            let b = interval (instant 2026 2 3 12 0) (instant 2026 2 3 20 0)
            let result = intersect a b
            Expect.isSome result "should have intersection"
            let i = result.Value
            Expect.isTrue (i.Start.Equals(instant 2026 2 3 12 0)) "start should be max of starts"
            Expect.isTrue (i.End.Equals(instant 2026 2 3 17 0)) "end should be min of ends"
        }

        test "non-overlapping intervals returns None" {
            let a = interval (instant 2026 2 3 9 0) (instant 2026 2 3 12 0)
            let b = interval (instant 2026 2 3 13 0) (instant 2026 2 3 17 0)
            let result = intersect a b
            Expect.isNone result "should not have intersection"
        }

        test "adjacent intervals returns None" {
            let a = interval (instant 2026 2 3 9 0) (instant 2026 2 3 12 0)
            let b = interval (instant 2026 2 3 12 0) (instant 2026 2 3 17 0)
            let result = intersect a b
            Expect.isNone result "adjacent intervals should not intersect"
        }

        test "contained interval returns inner" {
            let outer = interval (instant 2026 2 3 9 0) (instant 2026 2 3 17 0)
            let inner = interval (instant 2026 2 3 11 0) (instant 2026 2 3 14 0)
            let result = intersect outer inner
            Expect.isSome result "should have intersection"
            let i = result.Value
            Expect.isTrue (i.Start.Equals(instant 2026 2 3 11 0)) "start"
            Expect.isTrue (i.End.Equals(instant 2026 2 3 14 0)) "end"
        }
    ]

[<Tests>]
let subtractTests =
    testList "subtract" [
        test "removing middle portion splits interval" {
            let source = interval (instant 2026 2 3 9 0) (instant 2026 2 3 17 0)
            let removal = interval (instant 2026 2 3 12 0) (instant 2026 2 3 13 0)
            let result = subtract source [ removal ]
            Expect.hasLength result 2 "should produce two segments"
            Expect.isTrue (result.[0].Start.Equals(instant 2026 2 3 9 0)) "first start"
            Expect.isTrue (result.[0].End.Equals(instant 2026 2 3 12 0)) "first end"
            Expect.isTrue (result.[1].Start.Equals(instant 2026 2 3 13 0)) "second start"
            Expect.isTrue (result.[1].End.Equals(instant 2026 2 3 17 0)) "second end"
        }

        test "no removals returns original" {
            let source = interval (instant 2026 2 3 9 0) (instant 2026 2 3 17 0)
            let result = subtract source []
            Expect.hasLength result 1 "should produce one segment"
            Expect.isTrue (result.[0].Start.Equals(source.Start)) "start"
            Expect.isTrue (result.[0].End.Equals(source.End)) "end"
        }

        test "complete removal returns empty" {
            let source = interval (instant 2026 2 3 9 0) (instant 2026 2 3 17 0)
            let removal = interval (instant 2026 2 3 8 0) (instant 2026 2 3 18 0)
            let result = subtract source [ removal ]
            Expect.hasLength result 0 "should produce no segments"
        }
    ]

[<Tests>]
let chunkTests =
    testList "chunk" [
        test "divides evenly into 30-min slots" {
            let iv = interval (instant 2026 2 3 9 0) (instant 2026 2 3 11 0)
            let result = chunk (Duration.FromMinutes(30L)) iv
            Expect.hasLength result 4 "2 hours / 30 min = 4 slots"
            Expect.isTrue (result.[0].Start.Equals(instant 2026 2 3 9 0)) "first slot start"
            Expect.isTrue (result.[0].End.Equals(instant 2026 2 3 9 30)) "first slot end"
            Expect.isTrue (result.[3].Start.Equals(instant 2026 2 3 10 30)) "last slot start"
            Expect.isTrue (result.[3].End.Equals(instant 2026 2 3 11 0)) "last slot end"
        }

        test "remainder is dropped" {
            let iv = interval (instant 2026 2 3 9 0) (instant 2026 2 3 10 45)
            let result = chunk (Duration.FromMinutes(30L)) iv
            Expect.hasLength result 3 "105 min / 30 min = 3 full slots"
        }

        test "interval shorter than duration returns empty" {
            let iv = interval (instant 2026 2 3 9 0) (instant 2026 2 3 9 20)
            let result = chunk (Duration.FromMinutes(30L)) iv
            Expect.hasLength result 0 "20 min < 30 min duration"
        }
    ]

[<Tests>]
let computeSlotsTests =
    testList "computeSlots" [
        test "computes slots from participant windows and host availability" {
            // Host available Mon 9-17 ET
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Monday
                    StartTime = LocalTime(9, 0)
                    EndTime = LocalTime(17, 0) } ]

            // Participant available Mon Feb 2 2026 10:00-14:00 ET (UTC-5)
            let participantWindows =
                [ { Start = OffsetDateTime(LocalDateTime(2026, 2, 2, 10, 0), Offset.FromHours(-5))
                    End = OffsetDateTime(LocalDateTime(2026, 2, 2, 14, 0), Offset.FromHours(-5))
                    Timezone = Some "America/New_York" } ]

            let slots =
                computeSlots participantWindows hostSlots nyTz [] [] 30 "America/New_York"

            // Overlap is 10:00-14:00 ET = 8 x 30-min slots
            Expect.hasLength slots 8 "should have 8 half-hour slots"
        }

        test "empty participant windows returns no slots" {
            let slots = computeSlots [] [] nyTz [] [] 30 "America/New_York"
            Expect.hasLength slots 0 "no participant windows = no slots"
        }

        test "existing bookings are subtracted from available slots" {
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Monday
                    StartTime = LocalTime(9, 0)
                    EndTime = LocalTime(17, 0) } ]

            // Participant available Mon Feb 2 2026 9:00-12:00 ET
            let participantWindows =
                [ { Start = OffsetDateTime(LocalDateTime(2026, 2, 2, 9, 0), Offset.FromHours(-5))
                    End = OffsetDateTime(LocalDateTime(2026, 2, 2, 12, 0), Offset.FromHours(-5))
                    Timezone = Some "America/New_York" } ]

            // Existing booking 10:00-10:30 ET
            let existingBookings =
                [ { Id = System.Guid.NewGuid()
                    ParticipantName = "Someone"
                    ParticipantEmail = "someone@example.com"
                    ParticipantPhone = None
                    Title = "Existing meeting"
                    Description = None
                    StartTime = OffsetDateTime(LocalDateTime(2026, 2, 2, 10, 0), Offset.FromHours(-5))
                    EndTime = OffsetDateTime(LocalDateTime(2026, 2, 2, 10, 30), Offset.FromHours(-5))
                    DurationMinutes = 30
                    Timezone = "America/New_York"
                    Status = Confirmed
                    CreatedAt = SystemClock.Instance.GetCurrentInstant() } ]

            let slots =
                computeSlots participantWindows hostSlots nyTz existingBookings [] 30 "America/New_York"

            // 9:00-12:00 = 6 slots, minus 10:00-10:30 = 5 slots
            Expect.hasLength slots 5 "should have 5 slots with one booking subtracted"
        }

        test "no host availability on participant day returns no slots" {
            // Host only available Tuesday
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Tuesday
                    StartTime = LocalTime(9, 0)
                    EndTime = LocalTime(17, 0) } ]

            // Participant available Monday
            let participantWindows =
                [ { Start = OffsetDateTime(LocalDateTime(2026, 2, 2, 9, 0), Offset.FromHours(-5))
                    End = OffsetDateTime(LocalDateTime(2026, 2, 2, 17, 0), Offset.FromHours(-5))
                    Timezone = Some "America/New_York" } ]

            let slots =
                computeSlots participantWindows hostSlots nyTz [] [] 30 "America/New_York"

            Expect.hasLength slots 0 "no overlap between Monday participant and Tuesday host"
        }
    ]

[<Tests>]
let expandHostSlotsTests =
    testList "expandHostSlots" [
        test "expands single weekday slot into correct interval" {
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Monday
                    StartTime = LocalTime(9, 0)
                    EndTime = LocalTime(17, 0) } ]

            // Mon Feb 2 2026 is a Monday
            let rangeStart = LocalDate(2026, 2, 2)
            let rangeEnd = LocalDate(2026, 2, 2)
            let result = expandHostSlots hostSlots nyTz rangeStart rangeEnd

            Expect.hasLength result 1 "one Monday in range"
            // 9:00 ET = 14:00 UTC, 17:00 ET = 22:00 UTC (EST = UTC-5)
            Expect.isTrue (result.[0].Start.Equals(instant 2026 2 2 14 0)) "start at 14:00 UTC"
            Expect.isTrue (result.[0].End.Equals(instant 2026 2 2 22 0)) "end at 22:00 UTC"
        }

        test "skips days that don't match slot day of week" {
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Wednesday
                    StartTime = LocalTime(9, 0)
                    EndTime = LocalTime(17, 0) } ]

            // Mon Feb 2 to Tue Feb 3 — no Wednesday
            let rangeStart = LocalDate(2026, 2, 2)
            let rangeEnd = LocalDate(2026, 2, 3)
            let result = expandHostSlots hostSlots nyTz rangeStart rangeEnd

            Expect.hasLength result 0 "no Wednesdays in Mon-Tue range"
        }

        test "expands across a full week" {
            let hostSlots =
                [ for day in 1..5 do
                      { Id = System.Guid.NewGuid()
                        DayOfWeek = enum<IsoDayOfWeek> day
                        StartTime = LocalTime(9, 0)
                        EndTime = LocalTime(17, 0) } ]

            // Mon Feb 2 to Sun Feb 8 2026
            let rangeStart = LocalDate(2026, 2, 2)
            let rangeEnd = LocalDate(2026, 2, 8)
            let result = expandHostSlots hostSlots nyTz rangeStart rangeEnd

            Expect.hasLength result 5 "5 weekdays Mon-Fri"
        }

        test "handles DST spring forward without crashing" {
            // US DST 2026: clocks spring forward on March 8 at 2:00 AM
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Sunday
                    StartTime = LocalTime(2, 30)
                    EndTime = LocalTime(5, 0) } ]

            // Sun March 8 2026 — 2:30 AM doesn't exist (clocks jump 2:00 → 3:00)
            let rangeStart = LocalDate(2026, 3, 8)
            let rangeEnd = LocalDate(2026, 3, 8)

            // AtLeniently should handle this without throwing
            let result = expandHostSlots hostSlots nyTz rangeStart rangeEnd
            Expect.hasLength result 1 "should produce one interval even during DST transition"
        }

        test "DST spring forward shortens the slot via AtLeniently" {
            // US DST 2026: clocks spring forward on March 8 at 2:00 AM
            // 2:30 AM doesn't exist. AtLeniently maps it to 3:30 AM EDT (UTC-4)
            // by adding the gap amount to the skipped time.
            // So the slot becomes 3:30 AM EDT to 5:00 AM EDT = 1.5 hours instead of 2.5
            let hostSlots =
                [ { Id = System.Guid.NewGuid()
                    DayOfWeek = IsoDayOfWeek.Sunday
                    StartTime = LocalTime(2, 30)
                    EndTime = LocalTime(5, 0) } ]

            let rangeStart = LocalDate(2026, 3, 8)
            let rangeEnd = LocalDate(2026, 3, 8)

            let result = expandHostSlots hostSlots nyTz rangeStart rangeEnd
            Expect.hasLength result 1 "should produce one interval"
            let iv = result.[0]
            // 3:30 AM EDT = 07:30 UTC, 5:00 AM EDT = 09:00 UTC
            Expect.isTrue (iv.Start.Equals(instant 2026 3 8 7 30)) "start pushed to 3:30 AM EDT = 07:30 UTC"
            Expect.isTrue (iv.End.Equals(instant 2026 3 8 9 0)) "end at 5:00 AM EDT = 09:00 UTC"
            // Duration is 1.5 hours, not 2.5 — the slot was shortened by DST
            let duration = iv.End - iv.Start
            Expect.equal duration (Duration.FromMinutes(90L)) "slot shortened to 90 min due to spring-forward"
        }

        test "empty host slots returns empty" {
            let rangeStart = LocalDate(2026, 2, 2)
            let rangeEnd = LocalDate(2026, 2, 8)
            let result = expandHostSlots [] nyTz rangeStart rangeEnd
            Expect.hasLength result 0 "no host slots = no intervals"
        }
    ]
