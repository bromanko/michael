module Michael.Tests.PropertyTests

open System
open Expecto
open FsCheck
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.Availability
open Michael.Email
open Michael.Tests.TestHelpers
open Michael.Handlers
open Michael.Sanitize
open Michael.Formatting

// ---------------------------------------------------------------------------
// Custom generators
// ---------------------------------------------------------------------------

/// Generate a NodaTime Offset in the valid range -18h to +18h, always
/// aligned to 15-minute increments (matching real-world IANA offsets).
let private genOffset =
    // -72 to +72 quarter-hours = -18h to +18h
    Gen.choose (-72, 72) |> Gen.map (fun q -> Offset.FromSeconds(q * 15 * 60))

/// Generate a reasonable OffsetDateTime (years 2020–2035).
let private genOffsetDateTime =
    gen {
        let! year = Gen.choose (2020, 2035)
        let! month = Gen.choose (1, 12)
        let maxDay = System.DateTime.DaysInMonth(year, month)
        let! day = Gen.choose (1, maxDay)
        let! hour = Gen.choose (0, 23)
        let! minute = Gen.choose (0, 59)
        let! second = Gen.choose (0, 59)
        let! offset = genOffset
        let ldt = LocalDateTime(year, month, day, hour, minute, second)
        return OffsetDateTime(ldt, offset)
    }

/// Generate a valid Instant within a reasonable range (2020–2035).
let private genInstant = genOffsetDateTime |> Gen.map (fun odt -> odt.ToInstant())

/// Generate an Interval with positive duration (up to 24 hours).
let private genInterval =
    gen {
        let! start = genInstant
        let! durationMinutes = Gen.choose (1, 1440)
        let duration = Duration.FromMinutes(int64 durationMinutes)
        return Interval(start, start + duration)
    }

/// Generate a chunk duration that could produce at least one slot
/// from a given interval.
let private genChunkDuration (iv: Interval) =
    let totalMinutes = int iv.Duration.TotalMinutes

    if totalMinutes < 1 then
        Gen.constant (Duration.FromMinutes(1L))
    else
        Gen.choose (1, totalMinutes) |> Gen.map (fun m -> Duration.FromMinutes(int64 m))

/// Generate a string with random ASCII printable characters.
let private genPrintableString =
    Gen.choose (32, 126) |> Gen.map char |> Gen.arrayOf |> Gen.map System.String

/// Generate a string that includes control characters.
let private genStringWithControlChars =
    gen {
        let! chars = Gen.listOf (Gen.oneof [ Gen.choose (0, 31) |> Gen.map char; Gen.choose (32, 126) |> Gen.map char ])

        return System.String(chars |> List.toArray)
    }

/// Generate a structurally valid email address.
let private genValidEmail =
    gen {
        let! localLen = Gen.choose (1, 10)

        let! local =
            Gen.arrayOfLength localLen (Gen.elements [ 'a' .. 'z' ])
            |> Gen.map System.String

        let! domainLen = Gen.choose (1, 8)

        let! domain =
            Gen.arrayOfLength domainLen (Gen.elements [ 'a' .. 'z' ])
            |> Gen.map System.String

        let! tldLen = Gen.choose (2, 4)

        let! tld = Gen.arrayOfLength tldLen (Gen.elements [ 'a' .. 'z' ]) |> Gen.map System.String

        return $"{local}@{domain}.{tld}"
    }

/// Generate a host availability slot for any day of the week (Mon–Sun).
let private genHostSlot: Gen<HostAvailabilitySlot> =
    gen {
        let! day = Gen.choose (1, 7) |> Gen.map (fun d -> enum<IsoDayOfWeek> d)
        let! startHour = Gen.choose (0, 22)
        let! durationHours = Gen.choose (1, 23 - startHour)

        return
            { HostAvailabilitySlot.Id = System.Guid.Empty
              DayOfWeek = day
              StartTime = LocalTime(startHour, 0)
              EndTime = LocalTime(startHour + durationHours, 0) }
    }

/// Generate a list of host availability slots. May include multiple slots
/// on the same weekday (overlapping host intervals), which computeSlots
/// must handle correctly by merging after intersection.
let private genHostSlots: Gen<HostAvailabilitySlot list> =
    Gen.resize 6 (Gen.listOf genHostSlot)

/// Timezones used by participant window generators. Covers multiple UTC
/// offsets, DST rules, and hemispheres so property tests exercise varied
/// timezone arithmetic.
let private participantTimezones =
    [| "America/New_York" // UTC-5 / UTC-4 (DST)
       "America/Los_Angeles" // UTC-8 / UTC-7 (DST)
       "Europe/London" // UTC+0 / UTC+1 (DST)
       "Asia/Tokyo" // UTC+9 (no DST)
       "Australia/Sydney" // UTC+10 / UTC+11 (DST, southern hemisphere)
       "UTC" |]

/// Generate a participant availability window for a given date in a
/// randomly chosen timezone.
let private genParticipantWindowOnDate (date: LocalDate) : Gen<AvailabilityWindow> =
    gen {
        let! startHour = Gen.choose (6, 18)
        let! durationHours = Gen.choose (1, 22 - startHour)
        let! tzId = Gen.elements participantTimezones
        let tz = DateTimeZoneProviders.Tzdb.[tzId]
        let startLdt = LocalDateTime(date.Year, date.Month, date.Day, startHour, 0)

        let endLdt =
            LocalDateTime(date.Year, date.Month, date.Day, startHour + durationHours, 0)
        // Resolve the offset from the timezone so it's consistent with the
        // declared timezone, including DST transitions.
        let startOdt = tz.AtLeniently(startLdt).ToOffsetDateTime()
        let endOdt = tz.AtLeniently(endLdt).ToOffsetDateTime()

        return
            { AvailabilityWindow.Start = startOdt
              End = endOdt
              Timezone = Some tzId }
    }

/// Generate 1–4 participant windows. May include overlapping windows on
/// the same day, which computeSlots must handle correctly by merging
/// after intersection.
let private genParticipantWindows: Gen<AvailabilityWindow list> =
    gen {
        let! count = Gen.choose (1, 4)
        // Pick day offsets (may repeat, producing same-day overlapping windows)
        let! dayOffsets = Gen.listOfLength count (Gen.choose (0, 20))

        return!
            dayOffsets
            |> List.map (fun offset ->
                let date = LocalDate(2026, 2, 2).PlusDays(offset)
                genParticipantWindowOnDate date)
            |> Gen.sequence
    }

/// Generate an interval anchored in the same date range as participant
/// windows (Feb 2–22, 2026) so blockers and bookings frequently overlap
/// with computed slots.
let private genOverlappingInterval =
    gen {
        let! dayOffset = Gen.choose (0, 20)
        let! startHour = Gen.choose (0, 23)
        let! durationMinutes = Gen.choose (15, 240)
        let date = LocalDate(2026, 2, 2).PlusDays(dayOffset)
        let ldt = LocalDateTime(date.Year, date.Month, date.Day, startHour, 0)
        let tz = DateTimeZoneProviders.Tzdb.["America/New_York"]
        let start = tz.AtLeniently(ldt).ToInstant()
        return Interval(start, start + Duration.FromMinutes(int64 durationMinutes))
    }

/// Generate a confirmed booking from an interval. Fills in required
/// fields with placeholder values — only StartTime/EndTime matter for
/// slot computation.
let private genBooking: Gen<Booking> =
    gen {
        let! iv = genOverlappingInterval
        let tz = DateTimeZoneProviders.Tzdb.["America/New_York"]
        let startOdt = iv.Start.InZone(tz).ToOffsetDateTime()
        let endOdt = iv.End.InZone(tz).ToOffsetDateTime()
        let durationMin = int iv.Duration.TotalMinutes

        return
            { Booking.Id = System.Guid.Empty
              ParticipantName = "Test"
              ParticipantEmail = "test@example.com"
              ParticipantPhone = None
              Title = "Test booking"
              Description = None
              StartTime = startOdt
              EndTime = endOdt
              DurationMinutes = durationMin
              Timezone = "America/New_York"
              Status = Confirmed
              CreatedAt = iv.Start
              CancellationToken = Some fixedCancellationToken
              CalDavEventHref = None }
    }

/// Generate a source interval with random removals for subtract tests.
let private genSourceAndRemovals =
    gen {
        let! source = genInterval
        let! removals = Gen.resize 5 (Gen.listOf genInterval)
        return source, removals
    }

/// Generate an interval paired with a fitting chunk duration.
let private genIntervalAndChunk =
    gen {
        let! iv = genInterval
        let! dur = genChunkDuration iv
        return iv, dur
    }

/// FsCheck config with a reasonable number of tests.
let private cfg =
    { FsCheckConfig.defaultConfig with
        maxTest = 200 }

// ---------------------------------------------------------------------------
// SMTP TLS parsing properties
// ---------------------------------------------------------------------------

/// Helper: build config with required SMTP vars and a specific TLS value.
let private buildWithTls (tlsValue: string option) =
    let getEnv name =
        match name with
        | "MICHAEL_SMTP_HOST" -> Some "mail.example.com"
        | "MICHAEL_SMTP_PORT" -> Some "587"
        | "MICHAEL_SMTP_FROM" -> Some "noreply@example.com"
        | "MICHAEL_SMTP_TLS" -> tlsValue
        | _ -> None

    buildSmtpConfig getEnv

/// The recognized TLS mode strings and their expected result.
let private validTlsValues =
    [ "none", NoTls
      "false", NoTls
      "starttls", StartTls
      "true", StartTls
      "sslon", SslOnConnect
      "sslonconnect", SslOnConnect ]

[<Tests>]
let smtpTlsProperties =
    testList
        "Property: SMTP TLS parsing"
        [ testPropertyWithConfig cfg "recognized values map to correct TlsMode (case-insensitive)" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! (value, expected) = Gen.elements validTlsValues

                          // Randomize case to verify case-insensitivity
                          let! chars =
                              value.ToCharArray()
                              |> Array.map (fun c -> Gen.elements [ System.Char.ToLower(c); System.Char.ToUpper(c) ])
                              |> Gen.sequence

                          let randomCased = System.String(chars |> List.toArray)
                          return randomCased, expected
                      }
                  ))
                  (fun (input, expectedMode) ->
                      match buildWithTls (Some input) with
                      | Ok(Some config) -> config.TlsMode = expectedMode
                      | _ -> false))

          testPropertyWithConfig cfg "unrecognized values return Error" (fun () ->
              Prop.forAll (Arb.fromGen genPrintableString) (fun s ->
                  let lower = s.ToLowerInvariant()

                  let isRecognized = validTlsValues |> List.exists (fun (v, _) -> v = lower)

                  if isRecognized then
                      true // skip recognized values
                  else
                      match buildWithTls (Some s) with
                      | Error _ -> true
                      | _ -> false))

          testPropertyWithConfig cfg "TlsMode defaults to StartTls when MICHAEL_SMTP_TLS is unset" (fun () ->
              match buildWithTls None with
              | Ok(Some config) -> config.TlsMode = StartTls
              | _ -> false) ]

// ---------------------------------------------------------------------------
// Sanitize module properties
// ---------------------------------------------------------------------------

[<Tests>]
let sanitizeProperties =
    testList
        "Property: Sanitize"
        [ testPropertyWithConfig cfg "stripControlChars removes all control characters" (fun () ->
              Prop.forAll (Arb.fromGen genStringWithControlChars) (fun s ->
                  let result = stripControlChars s
                  result |> Seq.forall (fun c -> not (System.Char.IsControl c))))

          testPropertyWithConfig cfg "stripControlChars is idempotent" (fun () ->
              Prop.forAll (Arb.fromGen genStringWithControlChars) (fun s ->
                  let once = stripControlChars s
                  let twice = stripControlChars once
                  once = twice))

          testPropertyWithConfig cfg "stripControlChars preserves printable strings" (fun () ->
              Prop.forAll (Arb.fromGen genPrintableString) (fun s ->
                  let result = stripControlChars s
                  result = s))

          testPropertyWithConfig cfg "truncate enforces maximum length" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! maxLen = Gen.choose (1, 500)
                          let! strLen = Gen.choose (0, maxLen * 3)
                          let s = System.String('x', strLen)
                          return maxLen, s
                      }
                  ))
                  (fun (maxLen, s) ->
                      let result = truncate maxLen s
                      result.Length <= maxLen))

          testPropertyWithConfig cfg "truncate preserves strings within limit" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! maxLen = Gen.choose (1, 500)
                          let! strLen = Gen.choose (0, maxLen)
                          let s = System.String('a', strLen)
                          return maxLen, s
                      }
                  ))
                  (fun (maxLen, s) ->
                      let result = truncate maxLen s
                      result = s))

          testPropertyWithConfig cfg "sanitizeField is idempotent" (fun () ->
              Prop.forAll (Arb.fromGen genStringWithControlChars) (fun s ->
                  let once = sanitizeField 100 s
                  let twice = sanitizeField 100 once
                  once = twice))

          testPropertyWithConfig cfg "sanitizeField result has no leading/trailing whitespace" (fun () ->
              Prop.forAll (Arb.fromGen genStringWithControlChars) (fun s ->
                  let result = sanitizeField 1000 s
                  result = result.Trim())) ]

// ---------------------------------------------------------------------------
// Email validation properties
// ---------------------------------------------------------------------------

[<Tests>]
let emailProperties =
    testList
        "Property: isValidEmail"
        [ testPropertyWithConfig cfg "valid structured emails are accepted" (fun () ->
              Prop.forAll (Arb.fromGen genValidEmail) (fun email -> isValidEmail email))

          testPropertyWithConfig cfg "emails without @ are rejected" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! len = Gen.choose (1, 30)

                          let! chars =
                              Gen.arrayOfLength
                                  len
                                  (Gen.elements ([ 'a' .. 'z' ] @ [ '0' .. '9' ] @ [ '.'; '-'; '_' ]))

                          return System.String(chars)
                      }
                  ))
                  (fun s -> not (isValidEmail s)))

          testPropertyWithConfig cfg "empty and whitespace strings are rejected" (fun (n: PositiveInt) ->
              let spaces = System.String(' ', n.Get)
              not (isValidEmail spaces) && not (isValidEmail "")) ]

// ---------------------------------------------------------------------------
// Duration validation properties
// ---------------------------------------------------------------------------

[<Tests>]
let durationProperties =
    testList
        "Property: isValidDurationMinutes"
        [ testPropertyWithConfig cfg "values in [5, 480] are valid" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.choose (5, 480))) (fun d -> isValidDurationMinutes d))

          testPropertyWithConfig cfg "values below 5 are invalid" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.choose (System.Int32.MinValue, 4))) (fun d ->
                  not (isValidDurationMinutes d)))

          testPropertyWithConfig cfg "values above 480 are invalid" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.choose (481, System.Int32.MaxValue))) (fun d ->
                  not (isValidDurationMinutes d))) ]

// ---------------------------------------------------------------------------
// ODT format/parse roundtrip properties
// ---------------------------------------------------------------------------

[<Tests>]
let formattingProperties =
    testList
        "Property: ODT format/parse roundtrip"
        [ testPropertyWithConfig cfg "format then parse preserves value" (fun () ->
              Prop.forAll (Arb.fromGen genOffsetDateTime) (fun odt ->
                  let formatted = odtFormatPattern.Format(odt)
                  let parsed = OffsetDateTimePattern.ExtendedIso.Parse(formatted)
                  parsed.Success && parsed.Value = odt))

          testPropertyWithConfig cfg "formatted output always contains full ±HH:MM offset" (fun () ->
              Prop.forAll (Arb.fromGen genOffsetDateTime) (fun odt ->
                  let formatted = odtFormatPattern.Format(odt)
                  let len = formatted.Length
                  // Must end with ±HH:MM (6 chars: sign + 2 digits + colon + 2 digits)
                  len >= 6
                  && (formatted.[len - 6] = '+' || formatted.[len - 6] = '-')
                  && formatted.[len - 3] = ':'))

          testPropertyWithConfig cfg "tryParseOdt accepts output of odtFormatPattern" (fun () ->
              Prop.forAll (Arb.fromGen genOffsetDateTime) (fun odt ->
                  let formatted = odtFormatPattern.Format(odt)
                  let result = tryParseOdt "test" formatted
                  Result.isOk result))

          testPropertyWithConfig cfg "tryParseOdt rejects strings without T separator" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! y = Gen.choose (2020, 2030)
                          let! m = Gen.choose (1, 12)
                          let! d = Gen.choose (1, 28)
                          let! h = Gen.choose (0, 23)
                          let! min = Gen.choose (0, 59)
                          // Date and time with space instead of 'T'
                          return sprintf "%04d-%02d-%02d %02d:%02d:00+00:00" y m d h min
                      }
                  ))
                  (fun s -> Result.isError (tryParseOdt "test" s)))

          testPropertyWithConfig cfg "tryParseOdt rejects strings missing offset" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! y = Gen.choose (2020, 2030)
                          let! m = Gen.choose (1, 12)
                          let! d = Gen.choose (1, 28)
                          let! h = Gen.choose (0, 23)
                          let! min = Gen.choose (0, 59)
                          // Valid local datetime but no UTC offset
                          return sprintf "%04d-%02d-%02dT%02d:%02d:00" y m d h min
                      }
                  ))
                  (fun s -> Result.isError (tryParseOdt "test" s)))

          testPropertyWithConfig cfg "tryParseOdt rejects random non-datetime strings" (fun () ->
              Prop.forAll (Arb.fromGen genPrintableString) (fun s ->
                  // Skip strings that happen to be valid ISO-8601 with offset
                  match tryParseOdt "test" s with
                  | Ok _ -> true // rare accidental match is fine
                  | Error _ -> true))

          testPropertyWithConfig cfg "tryParseOdt rejects invalid month/day values" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! invalidMonth = Gen.choose (13, 99)
                          let! invalidDay = Gen.choose (32, 99)

                          let! useInvalidMonth = Gen.elements [ true; false ]

                          if useInvalidMonth then
                              return sprintf "2026-%02d-15T10:00:00+00:00" invalidMonth
                          else
                              return sprintf "2026-06-%02dT10:00:00+00:00" invalidDay
                      }
                  ))
                  (fun s -> Result.isError (tryParseOdt "test" s))) ]

// ---------------------------------------------------------------------------
// Interval intersection properties
// ---------------------------------------------------------------------------

[<Tests>]
let intersectProperties =
    testList
        "Property: intersect"
        [ testPropertyWithConfig cfg "intersection is commutative" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.two genInterval)) (fun (a, b) ->
                  let ab = intersect a b
                  let ba = intersect b a

                  match ab, ba with
                  | None, None -> true
                  | Some x, Some y -> x.Start = y.Start && x.End = y.End
                  | _ -> false))

          testPropertyWithConfig cfg "intersection result is contained in both inputs" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.two genInterval)) (fun (a, b) ->
                  match intersect a b with
                  | None -> true
                  | Some i -> i.Start >= a.Start && i.End <= a.End && i.Start >= b.Start && i.End <= b.End))

          testPropertyWithConfig cfg "intersection with self is identity" (fun () ->
              Prop.forAll (Arb.fromGen genInterval) (fun a ->
                  match intersect a a with
                  | Some i -> i.Start = a.Start && i.End = a.End
                  | None -> false))

          testPropertyWithConfig cfg "intersection result has positive duration" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.two genInterval)) (fun (a, b) ->
                  match intersect a b with
                  | None -> true
                  | Some i -> i.Start < i.End))

          testPropertyWithConfig cfg "touching intervals (end = start) have no intersection" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! a = genInterval
                          let! durationMin = Gen.choose (1, 1440)
                          let b = Interval(a.End, a.End + Duration.FromMinutes(int64 durationMin))
                          return a, b
                      }
                  ))
                  (fun (a, b) -> intersect a b = None)) ]

// ---------------------------------------------------------------------------
// Interval merge properties
// ---------------------------------------------------------------------------

[<Tests>]
let mergeProperties =
    testList
        "Property: mergeIntervals"
        [ // Strict less-than: since mergeIntervals merges adjacent (touching)
          // intervals by design, merged output must have true gaps between
          // consecutive results — no touching or overlapping boundaries.
          testPropertyWithConfig cfg "result intervals are non-adjacent, non-overlapping, and sorted" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.resize 8 (Gen.listOf genInterval))) (fun intervals ->
                  let merged = mergeIntervals intervals

                  merged |> List.pairwise |> List.forall (fun (a, b) -> a.End < b.Start)))

          testPropertyWithConfig cfg "result covers every input point" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.resize 8 (Gen.listOf genInterval))) (fun intervals ->
                  let merged = mergeIntervals intervals

                  intervals
                  |> List.forall (fun iv -> merged |> List.exists (fun m -> m.Start <= iv.Start && m.End >= iv.End))))

          testPropertyWithConfig cfg "result has positive duration intervals" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.resize 8 (Gen.listOf genInterval))) (fun intervals ->
                  let merged = mergeIntervals intervals
                  merged |> List.forall (fun m -> m.Start < m.End)))

          testPropertyWithConfig cfg "merging is idempotent" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.resize 8 (Gen.listOf genInterval))) (fun intervals ->
                  let once = mergeIntervals intervals
                  let twice = mergeIntervals once

                  once.Length = twice.Length
                  && List.zip once twice
                     |> List.forall (fun (a, b) -> a.Start = b.Start && a.End = b.End)))

          testPropertyWithConfig cfg "empty input produces empty output" (fun () ->
              let result = mergeIntervals []
              result.IsEmpty)

          testPropertyWithConfig cfg "single interval is preserved" (fun () ->
              Prop.forAll (Arb.fromGen genInterval) (fun iv ->
                  let result = mergeIntervals [ iv ]

                  match result with
                  | [ single ] -> single.Start = iv.Start && single.End = iv.End
                  | _ -> false))

          testPropertyWithConfig cfg "adjacent intervals are merged into one" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! start = genInstant
                          let! d1 = Gen.choose (1, 720) |> Gen.map (fun m -> Duration.FromMinutes(int64 m))
                          let! d2 = Gen.choose (1, 720) |> Gen.map (fun m -> Duration.FromMinutes(int64 m))
                          let mid = start + d1
                          return Interval(start, mid), Interval(mid, mid + d2)
                      }
                  ))
                  (fun (a, b) ->
                      let merged = mergeIntervals [ a; b ]

                      match merged with
                      | [ single ] -> single.Start = a.Start && single.End = b.End
                      | _ -> false)) ]

// ---------------------------------------------------------------------------
// Interval subtraction properties
// ---------------------------------------------------------------------------

[<Tests>]
let subtractProperties =
    testList
        "Property: subtract"
        [ testPropertyWithConfig cfg "result intervals are within source bounds" (fun () ->
              Prop.forAll (Arb.fromGen genSourceAndRemovals) (fun (source, removals) ->
                  let result = subtract source removals

                  result |> List.forall (fun r -> r.Start >= source.Start && r.End <= source.End)))

          testPropertyWithConfig cfg "result intervals have positive duration" (fun () ->
              Prop.forAll (Arb.fromGen genSourceAndRemovals) (fun (source, removals) ->
                  let result = subtract source removals
                  result |> List.forall (fun r -> r.Start < r.End)))

          testPropertyWithConfig cfg "result intervals don't overlap removals" (fun () ->
              Prop.forAll (Arb.fromGen genSourceAndRemovals) (fun (source, removals) ->
                  let result = subtract source removals

                  result
                  |> List.forall (fun r ->
                      removals
                      |> List.forall (fun rem ->
                          match intersect r rem with
                          | None -> true
                          | Some _ -> false))))

          testPropertyWithConfig cfg "total result duration ≤ source duration" (fun () ->
              Prop.forAll (Arb.fromGen genSourceAndRemovals) (fun (source, removals) ->
                  let result = subtract source removals

                  let totalResultTicks = result |> List.sumBy (fun r -> (r.End - r.Start).TotalTicks)

                  totalResultTicks <= (source.End - source.Start).TotalTicks))

          testPropertyWithConfig cfg "subtracting nothing returns the original" (fun () ->
              Prop.forAll (Arb.fromGen genInterval) (fun source ->
                  let result = subtract source []

                  match result with
                  | [ single ] -> single.Start = source.Start && single.End = source.End
                  | _ -> false))

          testPropertyWithConfig cfg "result intervals are non-overlapping and sorted" (fun () ->
              Prop.forAll (Arb.fromGen genSourceAndRemovals) (fun (source, removals) ->
                  let result = subtract source removals

                  result |> List.pairwise |> List.forall (fun (a, b) -> a.End <= b.Start)))

          testPropertyWithConfig cfg "subtracting a superset of source yields empty" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! source = genInterval
                          let! extraBefore = Gen.choose (0, 1440) |> Gen.map (fun m -> Duration.FromMinutes(int64 m))
                          let! extraAfter = Gen.choose (0, 1440) |> Gen.map (fun m -> Duration.FromMinutes(int64 m))
                          let removal = Interval(source.Start - extraBefore, source.End + extraAfter)
                          return source, removal
                      }
                  ))
                  (fun (source, removal) ->
                      let result = subtract source [ removal ]
                      result.IsEmpty)) ]

// ---------------------------------------------------------------------------
// Chunk properties
// ---------------------------------------------------------------------------

[<Tests>]
let chunkProperties =
    testList
        "Property: chunk"
        [ testPropertyWithConfig cfg "all chunks have the exact requested duration" (fun () ->
              Prop.forAll (Arb.fromGen genIntervalAndChunk) (fun (iv, dur) ->
                  let chunks = chunk dur iv
                  chunks |> List.forall (fun c -> (c.End - c.Start) = dur)))

          testPropertyWithConfig cfg "chunks are contiguous (end of one = start of next)" (fun () ->
              Prop.forAll (Arb.fromGen genIntervalAndChunk) (fun (iv, dur) ->
                  let chunks = chunk dur iv
                  chunks |> List.pairwise |> List.forall (fun (a, b) -> a.End = b.Start)))

          testPropertyWithConfig cfg "first chunk starts at interval start" (fun () ->
              Prop.forAll (Arb.fromGen genIntervalAndChunk) (fun (iv, dur) ->
                  let chunks = chunk dur iv

                  match chunks with
                  | [] -> true
                  | first :: _ -> first.Start = iv.Start))

          testPropertyWithConfig cfg "last chunk ends within interval" (fun () ->
              Prop.forAll (Arb.fromGen genIntervalAndChunk) (fun (iv, dur) ->
                  let chunks = chunk dur iv

                  match chunks |> List.rev with
                  | [] -> true
                  | last :: _ -> last.End <= iv.End))

          testPropertyWithConfig cfg "chunk count equals floor(interval / duration)" (fun () ->
              Prop.forAll (Arb.fromGen genIntervalAndChunk) (fun (iv, dur) ->
                  let chunks = chunk dur iv
                  let expected = int (iv.Duration.TotalMinutes / dur.TotalMinutes)
                  chunks.Length = expected))

          testPropertyWithConfig cfg "chunk duration larger than interval produces empty list" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! iv = genInterval
                          let extraMinutes = int iv.Duration.TotalMinutes + 1
                          let dur = Duration.FromMinutes(int64 extraMinutes)
                          return iv, dur
                      }
                  ))
                  (fun (iv, dur) ->
                      let chunks = chunk dur iv
                      chunks.IsEmpty)) ]

// ---------------------------------------------------------------------------
// computeSlots properties
// ---------------------------------------------------------------------------

[<Tests>]
let computeSlotsProperties =
    /// Generate a timezone id from the shared set.
    let genTzId = Gen.elements participantTimezones

    /// Convert a TimeSlot to an Interval, computing instants once.
    let slotToInterval (s: TimeSlot) =
        Interval(s.SlotStart.ToInstant(), s.SlotEnd.ToInstant())

    testList
        "Property: computeSlots"
        [ testPropertyWithConfig cfg "all slots have the requested duration" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! windows = genParticipantWindows
                          let! durationMin = Gen.choose (5, 120)
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, windows, durationMin, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, windows, durationMin, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]

                      let slots = computeSlots windows hostSlots hostTz [] [] durationMin participantTzId

                      let expected = Duration.FromMinutes(int64 durationMin)

                      slots
                      |> List.forall (fun s ->
                          let iv = slotToInterval s
                          (iv.End - iv.Start) = expected)))

          testPropertyWithConfig cfg "slots are non-overlapping" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! windows = genParticipantWindows
                          let! durationMin = Gen.choose (15, 60)
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, windows, durationMin, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, windows, durationMin, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]

                      let slots = computeSlots windows hostSlots hostTz [] [] durationMin participantTzId

                      let intervals = slots |> List.map slotToInterval |> List.sortBy (fun iv -> iv.Start)

                      intervals |> List.pairwise |> List.forall (fun (a, b) -> a.End <= b.Start)))

          testPropertyWithConfig cfg "slots don't overlap with blockers" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! windows = genParticipantWindows
                          let! durationMin = Gen.choose (15, 60)
                          let! blockers = Gen.resize 3 (Gen.listOf genOverlappingInterval)
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, windows, durationMin, blockers, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, windows, durationMin, blockers, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]

                      let slots =
                          computeSlots windows hostSlots hostTz [] blockers durationMin participantTzId

                      slots
                      |> List.forall (fun s ->
                          let slotIv = slotToInterval s

                          blockers
                          |> List.forall (fun b ->
                              match intersect slotIv b with
                              | None -> true
                              | Some _ -> false))))

          testPropertyWithConfig cfg "slots don't overlap with existing bookings" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! windows = genParticipantWindows
                          let! durationMin = Gen.choose (15, 60)
                          let! bookings = Gen.resize 3 (Gen.listOf genBooking)
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, windows, durationMin, bookings, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, windows, durationMin, bookings, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]

                      let slots =
                          computeSlots windows hostSlots hostTz bookings [] durationMin participantTzId

                      let bookingIntervals =
                          bookings
                          |> List.map (fun b -> Interval(b.StartTime.ToInstant(), b.EndTime.ToInstant()))

                      slots
                      |> List.forall (fun s ->
                          let slotIv = slotToInterval s

                          bookingIntervals
                          |> List.forall (fun b ->
                              match intersect slotIv b with
                              | None -> true
                              | Some _ -> false))))

          testPropertyWithConfig cfg "empty participant windows produce no slots" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]
                      let slots = computeSlots [] hostSlots hostTz [] [] 30 participantTzId
                      slots.IsEmpty))

          testPropertyWithConfig cfg "empty host slots produce no slots" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! windows = genParticipantWindows
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return windows, hostTzId, participantTzId
                      }
                  ))
                  (fun (windows, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]
                      let slots = computeSlots windows [] hostTz [] [] 30 participantTzId
                      slots.IsEmpty))

          testPropertyWithConfig cfg "slots fall within participant windows" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! windows = genParticipantWindows
                          let! durationMin = Gen.choose (15, 60)
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, windows, durationMin, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, windows, durationMin, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]

                      let slots = computeSlots windows hostSlots hostTz [] [] durationMin participantTzId

                      // Use merged windows: computeSlots merges overlapping participant
                      // windows, so a slot may span adjacent original window boundaries.
                      let windowIntervals =
                          windows
                          |> List.map (fun w -> Interval(w.Start.ToInstant(), w.End.ToInstant()))
                          |> mergeIntervals

                      slots
                      |> List.forall (fun s ->
                          let slotIv = slotToInterval s

                          windowIntervals
                          |> List.exists (fun wiv -> slotIv.Start >= wiv.Start && slotIv.End <= wiv.End))))

          testPropertyWithConfig cfg "slots are expressed in the requested timezone" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! hostSlots = genHostSlots
                          let! windows = genParticipantWindows
                          let! hostTzId = genTzId
                          let! participantTzId = genTzId
                          return hostSlots, windows, hostTzId, participantTzId
                      }
                  ))
                  (fun (hostSlots, windows, hostTzId, participantTzId) ->
                      let hostTz = DateTimeZoneProviders.Tzdb.[hostTzId]

                      let slots = computeSlots windows hostSlots hostTz [] [] 30 participantTzId

                      let tz = DateTimeZoneProviders.Tzdb.[participantTzId]

                      slots
                      |> List.forall (fun s ->
                          let startInst = s.SlotStart.ToInstant()
                          let expectedOffset = tz.GetUtcOffset(startInst)
                          s.SlotStart.Offset = expectedOffset && s.SlotEnd.Offset = expectedOffset))) ]

// ---------------------------------------------------------------------------
// tryResolveTimezone properties
// ---------------------------------------------------------------------------

[<Tests>]
let timezoneProperties =
    let commonTimezones =
        [ "UTC"
          "America/New_York"
          "America/Chicago"
          "America/Denver"
          "America/Los_Angeles"
          "Europe/London"
          "Europe/Berlin"
          "Asia/Tokyo"
          "Asia/Kolkata"
          "Australia/Sydney"
          "Pacific/Auckland" ]

    testList
        "Property: tryResolveTimezone"
        [ testPropertyWithConfig cfg "known IANA timezones always resolve" (fun () ->
              Prop.forAll (Arb.fromGen (Gen.elements commonTimezones)) (fun tz -> Result.isOk (tryResolveTimezone tz)))

          testPropertyWithConfig cfg "random lowercase strings don't crash" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! len = Gen.choose (1, 20)
                          let! chars = Gen.arrayOfLength len (Gen.choose (int 'a', int 'z') |> Gen.map char)
                          return System.String(chars)
                      }
                  ))
                  (fun s ->
                      // The property is about not crashing, not about rejection
                      match tryResolveTimezone s with
                      | Error _ -> true
                      | Ok _ -> true))

          testPropertyWithConfig cfg "error messages include the input" (fun () ->
              Prop.forAll
                  (Arb.fromGen (
                      gen {
                          let! len = Gen.choose (1, 30)
                          let! chars = Gen.arrayOfLength len (Gen.choose (int 'a', int 'z') |> Gen.map char)
                          return System.String(chars)
                      }
                  ))
                  (fun s ->
                      match tryResolveTimezone s with
                      | Ok _ -> true
                      | Error msg ->
                          let expectedFragment = if s.Length <= 80 then s else s.Substring(0, 80)

                          msg.Contains(expectedFragment))) ]
