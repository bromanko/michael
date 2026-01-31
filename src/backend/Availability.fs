module Michael.Availability

open NodaTime
open Michael.Domain

// ---------------------------------------------------------------------------
// Instant comparison helpers (NodaTime Instant doesn't implement
// non-generic IComparable, so F# comparison operators don't work directly)
// ---------------------------------------------------------------------------

let private instantMin (a: Instant) (b: Instant) =
    if Instant.op_LessThan(a, b) then a else b

let private instantMax (a: Instant) (b: Instant) =
    if Instant.op_GreaterThan(a, b) then a else b

let private instantLt (a: Instant) (b: Instant) =
    Instant.op_LessThan(a, b)

let private instantLte (a: Instant) (b: Instant) =
    Instant.op_LessThanOrEqual(a, b)

// ---------------------------------------------------------------------------
// Slot computation
// ---------------------------------------------------------------------------

/// Expand weekly host availability slots into concrete intervals for a date range.
let expandHostSlots
    (hostSlots: HostAvailabilitySlot list)
    (rangeStart: LocalDate)
    (rangeEnd: LocalDate)
    : Interval list =
    [ for slot in hostSlots do
          let tz = DateTimeZoneProviders.Tzdb.[slot.Timezone]
          let mutable date = rangeStart

          while date <= rangeEnd do
              if date.DayOfWeek = slot.DayOfWeek then
                  let startLocal = date + slot.StartTime
                  let endLocal = date + slot.EndTime
                  // AtLeniently maps ambiguous/skipped local times to the nearest
                  // valid instant. During spring-forward DST transitions, a skipped
                  // start time is pushed forward, effectively shortening the slot.
                  // This is the accepted trade-off: the slot is shortened rather than
                  // throwing an exception or producing an invalid interval.
                  let startZoned = tz.AtLeniently(startLocal)
                  let endZoned = tz.AtLeniently(endLocal)
                  yield Interval(startZoned.ToInstant(), endZoned.ToInstant())

              date <- date.PlusDays(1) ]

/// Convert an AvailabilityWindow to a NodaTime Interval.
let windowToInterval (w: AvailabilityWindow) : Interval =
    Interval(w.Start.ToInstant(), w.End.ToInstant())

/// Intersect two intervals. Returns None if they don't overlap.
let intersect (a: Interval) (b: Interval) : Interval option =
    let start = instantMax a.Start b.Start
    let end' = instantMin a.End b.End

    if instantLt start end' then
        Some(Interval(start, end'))
    else
        None

/// Subtract a list of intervals from a single interval.
let subtract (source: Interval) (removals: Interval list) : Interval list =
    let sourceStart = source.Start
    let sourceEnd = source.End

    let sorted =
        removals
        |> List.filter (fun (r: Interval) ->
            instantLt r.Start sourceEnd && instantLt sourceStart r.End)
        |> List.sortBy (fun (r: Interval) -> r.Start.ToUnixTimeTicks())

    let rec loop (current: Instant) (remaining: Interval list) acc =
        match remaining with
        | [] ->
            if instantLt current sourceEnd then
                List.rev (Interval(current, sourceEnd) :: acc)
            else
                List.rev acc
        | r :: rest ->
            let rStart = r.Start
            let rEnd = r.End
            let gapEnd = instantMin rStart sourceEnd

            let acc' =
                if instantLt current gapEnd then
                    Interval(current, gapEnd) :: acc
                else
                    acc

            let nextCurrent = instantMax current rEnd
            loop nextCurrent rest acc'

    loop sourceStart sorted []

/// Chunk an interval into fixed-duration slots.
let chunk (duration: Duration) (interval: Interval) : Interval list =
    let rec loop (start: Instant) acc =
        let end' = start + duration

        if instantLte end' interval.End then
            loop end' (Interval(start, end') :: acc)
        else
            List.rev acc

    loop interval.Start []

/// Compute available time slots given participant windows, host availability,
/// existing bookings, and requested duration.
let computeSlots
    (participantWindows: AvailabilityWindow list)
    (hostSlots: HostAvailabilitySlot list)
    (existingBookings: Booking list)
    (calendarBlockers: Interval list)
    (durationMinutes: int)
    (participantTz: string)
    : TimeSlot list =
    let tz = DateTimeZoneProviders.Tzdb.[participantTz]
    let duration = Duration.FromMinutes(int64 durationMinutes)

    let participantIntervals =
        participantWindows |> List.map windowToInterval

    if participantIntervals.IsEmpty then
        []
    else
        let allStarts =
            participantIntervals |> List.map (fun i -> i.Start)

        let allEnds =
            participantIntervals |> List.map (fun i -> i.End)

        let earliest = allStarts |> List.reduce instantMin
        let latest = allEnds |> List.reduce instantMax

        let rangeStart = earliest.InZone(tz).Date
        let rangeEnd = latest.InZone(tz).Date

        let hostIntervals = expandHostSlots hostSlots rangeStart rangeEnd

        let bookingIntervals =
            existingBookings
            |> List.map (fun b -> Interval(b.StartTime.ToInstant(), b.EndTime.ToInstant()))

        let intersected =
            [ for pw in participantIntervals do
                  for hw in hostIntervals do
                      match intersect pw hw with
                      | Some i -> yield i
                      | None -> () ]

        let allBlockers = bookingIntervals @ calendarBlockers

        let available =
            intersected
            |> List.collect (fun i -> subtract i allBlockers)

        available
        |> List.collect (chunk duration)
        |> List.map (fun (i: Interval) ->
            { SlotStart = i.Start.InZone(tz).ToOffsetDateTime()
              SlotEnd = i.End.InZone(tz).ToOffsetDateTime() })
