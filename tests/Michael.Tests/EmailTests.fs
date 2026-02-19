module Michael.Tests.EmailTests

open System
open System.IO
open System.Text
open Expecto
open Ical.Net
open Ical.Net.CalendarComponents
open MimeKit
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.Email

let private makeBooking () =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
      ParticipantName = "Alice Smith"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = Some "555-1234"
      Title = "Project Review"
      Description = Some "Quarterly review meeting"
      StartTime = pattern.Parse("2026-02-15T14:00:00-05:00").Value
      EndTime = pattern.Parse("2026-02-15T15:00:00-05:00").Value
      DurationMinutes = 60
      Timezone = "America/New_York"
      Status = Confirmed
      CreatedAt = Instant.FromUtc(2026, 2, 14, 12, 0, 0)
      CancellationToken = Some "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890" }

let private testSmtpConfig: SmtpConfig =
    { Host = "mail.example.com"
      Port = 587
      Username = None
      Password = None
      TlsMode = StartTls
      FromAddress = "cal@example.com"
      FromName = "Michael" }

let private testNotificationConfig: NotificationConfig =
    { Smtp = testSmtpConfig
      HostEmail = "host@example.com"
      HostName = "Brian"
      PublicUrl = "https://cal.example.com" }

[<Tests>]
let emailTests =
    testList
        "Email"
        [ testList
              "formatBookingDate"
              [ test "formats date as YYYY-MM-DD" {
                    let pattern = OffsetDateTimePattern.ExtendedIso
                    let odt = pattern.Parse("2026-02-15T14:00:00-05:00").Value
                    let result = formatBookingDate odt
                    Expect.equal result "2026-02-15" "date formatted correctly"
                }

                test "pads single-digit month and day" {
                    let pattern = OffsetDateTimePattern.ExtendedIso
                    let odt = pattern.Parse("2026-01-05T09:00:00+00:00").Value
                    let result = formatBookingDate odt
                    Expect.equal result "2026-01-05" "single digits padded"
                } ]

          testList
              "formatBookingTime"
              [ test "formats time as HH:MM" {
                    let pattern = OffsetDateTimePattern.ExtendedIso
                    let odt = pattern.Parse("2026-02-15T14:30:00-05:00").Value
                    let result = formatBookingTime odt
                    Expect.equal result "14:30" "time formatted correctly"
                }

                test "pads single-digit hour" {
                    let pattern = OffsetDateTimePattern.ExtendedIso
                    let odt = pattern.Parse("2026-02-15T09:05:00-05:00").Value
                    let result = formatBookingTime odt
                    Expect.equal result "09:05" "single digit hour padded"
                }

                test "handles midnight" {
                    let pattern = OffsetDateTimePattern.ExtendedIso
                    let odt = pattern.Parse("2026-02-15T00:00:00+00:00").Value
                    let result = formatBookingTime odt
                    Expect.equal result "00:00" "midnight formatted correctly"
                } ]

          testList
              "buildCancellationEmailContent"
              [ test "includes booking title in subject" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true
                    Expect.stringContains content.Subject "Project Review" "subject contains title"
                    Expect.stringContains content.Subject "Cancelled" "subject indicates cancellation"
                }

                test "body includes all booking details" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true
                    Expect.stringContains content.Body "Project Review" "body contains title"
                    Expect.stringContains content.Body "2026-02-15" "body contains date"
                    Expect.stringContains content.Body "14:00" "body contains start time"
                    Expect.stringContains content.Body "15:00" "body contains end time"
                    Expect.stringContains content.Body "America/New_York" "body contains timezone"
                    Expect.stringContains content.Body "60 minutes" "body contains duration"
                }

                test "host cancellation shows correct message" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true
                    Expect.stringContains content.Body "The host has cancelled" "host cancellation message"
                }

                test "participant cancellation shows correct message" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking false
                    Expect.stringContains content.Body "You have cancelled" "participant cancellation message"
                }

                test "self-cancellation body differs from host-cancellation body" {
                    // Cross-path regression guard: both branches must produce
                    // distinct output and must not bleed each other's copy.
                    let booking = makeBooking ()
                    let hostContent = buildCancellationEmailContent booking true
                    let selfContent = buildCancellationEmailContent booking false

                    Expect.notEqual hostContent.Body selfContent.Body "bodies differ between paths"

                    Expect.isFalse
                        (selfContent.Body.Contains("The host has cancelled"))
                        "host-cancel phrase must not appear in self-cancel body"

                    Expect.isFalse
                        (hostContent.Body.Contains("You have cancelled"))
                        "self-cancel phrase must not appear in host-cancel body"
                }

                test "never includes a video link" {
                    // A cancelled meeting has no video call; including the link
                    // would be misleading. The video link is intentionally omitted
                    // regardless of the booking's videoLink field.
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true
                    Expect.isFalse (content.Body.Contains("Video link:")) "no video link in cancellation email"
                } ]

          testList
              "buildConfirmationEmailContent"
              [ test "includes booking title in subject" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None None
                    Expect.stringContains content.Subject "Project Review" "subject contains title"
                    Expect.stringContains content.Subject "Confirmed" "subject indicates confirmation"
                }

                test "body includes all booking details" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None None
                    Expect.stringContains content.Body "Project Review" "body contains title"
                    Expect.stringContains content.Body "2026-02-15" "body contains date"
                    Expect.stringContains content.Body "14:00" "body contains start time"
                    Expect.stringContains content.Body "15:00" "body contains end time"
                    Expect.stringContains content.Body "America/New_York" "body contains timezone"
                    Expect.stringContains content.Body "60 minutes" "body contains duration"
                }

                test "includes description when present" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None None
                    Expect.stringContains content.Body "Quarterly review meeting" "body contains description"
                }

                test "omits description line when None" {
                    let booking =
                        { makeBooking () with
                            Description = None }

                    let content = buildConfirmationEmailContent booking None None
                    Expect.isFalse (content.Body.Contains("Description:")) "no description line"
                }

                test "includes video link when provided" {
                    let booking = makeBooking ()

                    let content =
                        buildConfirmationEmailContent booking (Some "https://meet.google.com/abc-def") None

                    Expect.stringContains content.Body "https://meet.google.com/abc-def" "body contains video link"
                }

                test "omits video link when None" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None None
                    Expect.isFalse (content.Body.Contains("Video link:")) "no video link line"
                }

                test "omits video link when whitespace" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking (Some "  ") None
                    Expect.isFalse (content.Body.Contains("Video link:")) "no video link for whitespace"
                }

                test "includes cancellation URL when provided" {
                    let booking = makeBooking ()

                    let content =
                        buildConfirmationEmailContent booking None (Some "https://cal.example.com/cancel/abc/token123")

                    Expect.stringContains
                        content.Body
                        "https://cal.example.com/cancel/abc/token123"
                        "body contains cancellation link"

                    Expect.stringContains content.Body "To cancel this meeting" "body contains cancellation text"
                }

                test "omits cancellation line when None" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None None
                    Expect.isFalse (content.Body.Contains("To cancel")) "no cancellation line"
                }

                test "omits cancellation text when booking has no cancellation token" {
                    let booking =
                        { makeBooking () with
                            CancellationToken = None }

                    // Derive cancellationUrl the same way sendBookingConfirmationEmail does
                    let cancellationUrl =
                        booking.CancellationToken
                        |> Option.map (fun token -> $"https://cal.example.com/cancel/{booking.Id}/{token}")

                    let content = buildConfirmationEmailContent booking None cancellationUrl
                    Expect.isNone cancellationUrl "cancellationUrl should be None for tokenless booking"
                    Expect.isFalse (content.Body.Contains("To cancel")) "no cancellation text"
                    Expect.isFalse (content.Body.Contains("/cancel/")) "no cancellation URL"
                } ]

          testList
              "buildCancellationUrl"
              [ test "constructs correct URL from publicUrl, booking ID, and token" {
                    let booking =
                        { makeBooking () with
                            Id = Guid.Parse("11111111-2222-3333-4444-555555555555")
                            CancellationToken = Some "AABBCCDD" }

                    let url = buildCancellationUrl "https://cal.example.com" booking

                    Expect.equal
                        url
                        (Some "https://cal.example.com/cancel/11111111-2222-3333-4444-555555555555/AABBCCDD")
                        "URL format"
                }

                test "returns None when booking has no cancellation token" {
                    let booking =
                        { makeBooking () with
                            CancellationToken = None }

                    let url = buildCancellationUrl "https://cal.example.com" booking
                    Expect.isNone url "no URL without token"
                }

                test "preserves trailing-slash-free publicUrl" {
                    let booking =
                        { makeBooking () with
                            CancellationToken = Some "TOKEN123" }

                    let url = buildCancellationUrl "https://example.com" booking

                    match url with
                    | Some u -> Expect.stringStarts u "https://example.com/cancel/" "no double slash"
                    | None -> failtest "expected Some"
                } ]

          testList
              "buildConfirmationIcs"
              [ test "parses back as valid iCalendar with METHOD:REQUEST" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let cal = Calendar.Load(ics)
                    Expect.equal cal.Method "REQUEST" "method is REQUEST"
                    Expect.equal cal.Events.Count 1 "one event"
                }

                test "has correct UID" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.equal evt.Uid $"{booking.Id}@michael" "UID matches"
                }

                test "has correct SUMMARY" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.equal evt.Summary "Project Review" "SUMMARY matches title"
                }

                test "DTSTART and DTEND are in UTC" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    // 14:00 EST = 19:00 UTC; 15:00 EST = 20:00 UTC
                    Expect.stringContains ics "19000" "start contains 1900"
                    Expect.stringContains ics "20000" "end contains 2000"
                    // Should NOT contain TZID references
                    Expect.isFalse (ics.Contains("TZID=")) "no TZID references"
                }

                test "has ORGANIZER with host email and name" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    Expect.stringContains ics "host@example.com" "contains host email"
                    Expect.stringContains ics "Brian" "contains host name"
                }

                test "has ATTENDEE with participant email and name" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    Expect.stringContains ics "alice@example.com" "contains participant email"
                    Expect.stringContains ics "Alice Smith" "contains participant name"
                }

                test "includes LOCATION when video link provided" {
                    let booking = makeBooking ()

                    let ics =
                        buildConfirmationIcs booking "host@example.com" "Brian" (Some "https://zoom.us/j/123456") None

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.equal evt.Location "https://zoom.us/j/123456" "LOCATION matches video link"
                }

                test "omits LOCATION when no video link" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.isTrue (isNull evt.Location || evt.Location = "") "no LOCATION"
                }

                test "includes cancellation URL in DESCRIPTION" {
                    let booking = makeBooking ()

                    let ics =
                        buildConfirmationIcs
                            booking
                            "host@example.com"
                            "Brian"
                            None
                            (Some "https://cal.example.com/cancel/abc/token123")

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]

                    Expect.stringContains
                        evt.Description
                        "https://cal.example.com/cancel/abc/token123"
                        "DESCRIPTION contains cancellation URL"
                }

                test "DESCRIPTION includes both description and cancellation URL" {
                    let booking = makeBooking () // has Description = Some "Quarterly review meeting"

                    let ics =
                        buildConfirmationIcs
                            booking
                            "host@example.com"
                            "Brian"
                            None
                            (Some "https://example.com/cancel/x/y")

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]

                    Expect.stringContains evt.Description "Quarterly review meeting" "contains description"

                    Expect.stringContains evt.Description "https://example.com/cancel/x/y" "contains cancellation URL"
                }

                test "DESCRIPTION newline is RFC 5545 compliant — Ical.Net escapes \\n correctly" {
                    // RFC 5545 §3.3.11 (TEXT): embedded newlines in property values
                    // MUST be serialised as the literal two-character sequence \n
                    // (backslash + n), NOT as a bare LF (0x0A). Ical.Net handles this
                    // automatically when a raw '\n' character is assigned to Description.
                    //
                    // This test pins that behaviour so a future Ical.Net version that
                    // stops escaping would be caught immediately, and documents why the
                    // current F# code uses a raw '\n' rather than a literal '\\n'.
                    let booking = makeBooking () // Description = Some "Quarterly review meeting"

                    let ics =
                        buildConfirmationIcs
                            booking
                            "host@example.com"
                            "Brian"
                            None
                            (Some "https://example.com/cancel/x/y")

                    // 1. Wire format: the raw ICS text must contain the literal \n escape
                    //    sequence (backslash + n), not a bare LF inside the DESCRIPTION line.
                    let descriptionLines =
                        ics.Split([| "\r\n" |], StringSplitOptions.None)
                        |> Array.filter (fun l -> l.StartsWith("DESCRIPTION"))

                    Expect.isTrue (descriptionLines.Length > 0) "DESCRIPTION line present"

                    // The literal \n escape appears in the serialised value.
                    Expect.isTrue
                        (descriptionLines |> Array.exists (fun l -> l.Contains("\\n")))
                        "DESCRIPTION wire format uses literal \\n escape, not a bare LF"

                    // 2. Round-trip: Calendar.Load must recover a real newline (0x0A)
                    //    in the parsed Description string.
                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]

                    Expect.isTrue
                        (evt.Description.Contains("\n"))
                        "round-tripped DESCRIPTION contains a real newline character"
                }

                test "has STATUS CONFIRMED and SEQUENCE 0" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.equal evt.Status "CONFIRMED" "STATUS is CONFIRMED"
                    Expect.equal evt.Sequence 0 "SEQUENCE is 0"
                }

                test "contains PRODID" {
                    let booking = makeBooking ()

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    Expect.stringContains ics "PRODID" "contains PRODID"
                    Expect.stringContains ics "Michael" "PRODID mentions Michael"
                }

                test "omits cancellation URL in DESCRIPTION when booking has no token" {
                    let booking =
                        { makeBooking () with
                            CancellationToken = None }

                    let cancellationUrl =
                        booking.CancellationToken
                        |> Option.map (fun token -> $"https://cal.example.com/cancel/{booking.Id}/{token}")

                    let ics =
                        buildConfirmationIcs booking "host@example.com" "Brian" None cancellationUrl

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]

                    let hasCancel = not (isNull evt.Description) && evt.Description.Contains("/cancel/")

                    Expect.isFalse hasCancel "DESCRIPTION should not contain cancellation URL"
                }

                test "no DESCRIPTION when booking has no description and no cancellation URL" {
                    let booking =
                        { makeBooking () with
                            Description = None }

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.isTrue (isNull evt.Description || evt.Description = "") "DESCRIPTION is empty or null"
                }

                test "CRLF in title produces a parseable single-event ICS" {
                    // Ical.Net escapes TEXT property values (SUMMARY) per RFC 5545 §3.3.11;
                    // stripControlChars adds defence in depth by removing control chars
                    // before assignment. Either way the result must parse to exactly one event.
                    let booking =
                        { makeBooking () with
                            Title = "Meeting\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Injected" }

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None
                    let cal = Calendar.Load(ics)

                    Expect.equal cal.Events.Count 1 "only one VEVENT after title injection attempt"
                }

                test "CRLF in participant name produces a parseable ICS" {
                    // Ical.Net does NOT escape control characters in PARAM values (CN).
                    // Without stripControlChars an embedded CRLF breaks the ATTENDEE
                    // property line and Calendar.Load throws a parse error.
                    let booking =
                        { makeBooking () with
                            ParticipantName = "Alice\r\nEND:VEVENT" }

                    let ics = buildConfirmationIcs booking "host@example.com" "Brian" None None
                    // This would throw "Could not parse line" without the fix.
                    let cal = Calendar.Load(ics)

                    Expect.equal cal.Events.Count 1 "only one VEVENT after name injection attempt"

                    let cn = cal.Events.[0].Attendees.[0].CommonName
                    let hasControl = cn |> Seq.exists Char.IsControl
                    Expect.isFalse hasControl "CN contains no control characters after stripping"
                } ]

          testList
              "buildCancellationIcs"
              [ test "parses back as valid iCalendar with METHOD:CANCEL" {
                    let booking = makeBooking ()
                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                    let cal = Calendar.Load(ics)
                    Expect.equal cal.Method "CANCEL" "method is CANCEL"
                    Expect.equal cal.Events.Count 1 "one event"
                }

                test "has same UID as confirmation" {
                    let booking = makeBooking ()
                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.equal evt.Uid $"{booking.Id}@michael" "UID matches booking"
                }

                test "has STATUS CANCELLED and SEQUENCE 1" {
                    let booking = makeBooking ()
                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.equal evt.Status "CANCELLED" "STATUS is CANCELLED"
                    Expect.equal evt.Sequence 1 "SEQUENCE is 1"
                }

                test "SUMMARY is prefixed with Cancelled:" {
                    let booking = makeBooking ()
                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    Expect.stringStarts evt.Summary "Cancelled:" "SUMMARY starts with Cancelled:"
                    Expect.stringContains evt.Summary "Project Review" "SUMMARY contains title"
                }

                test "has ORGANIZER and ATTENDEE" {
                    let booking = makeBooking ()
                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                    Expect.stringContains ics "host@example.com" "contains host email"
                    Expect.stringContains ics "alice@example.com" "contains participant email"
                }

                test "DTSTAMP matches cancelledAt parameter" {
                    let booking = makeBooking ()
                    let cancelledAt = Instant.FromUtc(2026, 3, 1, 8, 30, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt

                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]
                    let expected = cancelledAt.ToDateTimeUtc()
                    Expect.equal evt.DtStamp.Value expected "DTSTAMP matches cancelledAt"
                }

                test "CRLF in title produces a parseable single-event cancellation ICS" {
                    let booking =
                        { makeBooking () with
                            Title = "Meeting\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Injected" }

                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)
                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt
                    let cal = Calendar.Load(ics)

                    Expect.equal cal.Events.Count 1 "only one VEVENT after title injection attempt"
                }

                test "CRLF in participant name produces a parseable cancellation ICS" {
                    let booking =
                        { makeBooking () with
                            ParticipantName = "Alice\r\nEND:VEVENT" }

                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)
                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt
                    // Would throw "Could not parse line" without stripControlChars on CN.
                    let cal = Calendar.Load(ics)

                    Expect.equal cal.Events.Count 1 "only one VEVENT after name injection attempt"

                    let cn = cal.Events.[0].Attendees.[0].CommonName
                    let hasControl = cn |> Seq.exists Char.IsControl
                    Expect.isFalse hasControl "CN contains no control characters after stripping"
                }

                test "DESCRIPTION is absent from cancellation ICS" {
                    // buildCancellationIcs intentionally omits DESCRIPTION —
                    // a cancelled meeting has no actionable body text to convey.
                    // This assertion is a regression guard: a future change that
                    // accidentally injects user-supplied description text here
                    // would otherwise go undetected.
                    let booking = makeBooking () // has Description = Some "Quarterly review meeting"
                    let cancelledAt = Instant.FromUtc(2026, 2, 16, 10, 0, 0)

                    let ics = buildCancellationIcs booking "host@example.com" "Brian" cancelledAt
                    let cal = Calendar.Load(ics)
                    let evt = cal.Events.[0]

                    Expect.isTrue
                        (isNull evt.Description || evt.Description = "")
                        "DESCRIPTION must be absent from cancellation ICS"
                } ]

          testList
              "buildMimeMessage"
              [ test "plain text message without attachment" {
                    let msg =
                        buildMimeMessage testSmtpConfig "to@example.com" "Alice" "Subject" "Body text" None None

                    Expect.equal msg.Subject "Subject" "subject set"
                    Expect.equal (msg.To.ToString()) "\"Alice\" <to@example.com>" "to address"

                    // Body should be plain text, not multipart
                    Expect.isTrue (msg.Body :? TextPart) "body is TextPart"
                    let tp = msg.Body :?> TextPart
                    Expect.equal tp.Text "Body text" "body text matches"
                }

                test "multipart message with ics attachment" {
                    let msg =
                        buildMimeMessage
                            testSmtpConfig
                            "to@example.com"
                            "Alice"
                            "Subject"
                            "Body text"
                            None
                            (Some
                                { Content = "BEGIN:VCALENDAR\nEND:VCALENDAR"
                                  Method = "REQUEST" })

                    Expect.isTrue (msg.Body :? Multipart) "body is Multipart"
                    let mp = msg.Body :?> Multipart
                    Expect.equal mp.Count 2 "two parts"

                    // First part is text/plain
                    let textPart = mp.[0] :?> TextPart
                    Expect.equal textPart.ContentType.MimeType "text/plain" "first part is text/plain"
                    Expect.equal textPart.Text "Body text" "text body matches"

                    // Second part is text/calendar
                    let calPart = mp.[1] :?> MimePart
                    Expect.equal calPart.ContentType.MimeType "text/calendar" "second part is text/calendar"

                    Expect.equal (calPart.ContentType.Parameters.["charset"]) "utf-8" "charset is utf-8"

                    Expect.equal (calPart.ContentType.Parameters.["method"]) "REQUEST" "method is REQUEST"

                    Expect.equal
                        calPart.ContentDisposition.Disposition
                        ContentDisposition.Inline
                        "disposition is inline"

                    Expect.equal calPart.ContentDisposition.FileName "invite.ics" "filename is invite.ics"

                    // Read calendar content
                    use ms = new MemoryStream()
                    calPart.Content.DecodeTo(ms)
                    let content = Encoding.UTF8.GetString(ms.ToArray())
                    Expect.stringContains content "BEGIN:VCALENDAR" "contains iCal content"
                }

                test "ics attachment with CANCEL method" {
                    let msg =
                        buildMimeMessage
                            testSmtpConfig
                            "to@example.com"
                            "Alice"
                            "Subject"
                            "Body"
                            None
                            (Some
                                { Content = "BEGIN:VCALENDAR\nMETHOD:CANCEL\nEND:VCALENDAR"
                                  Method = "CANCEL" })

                    let mp = msg.Body :?> Multipart
                    let calPart = mp.[1] :?> MimePart

                    Expect.equal (calPart.ContentType.Parameters.["method"]) "CANCEL" "method is CANCEL"
                }

                test "BCC header present when bcc provided" {
                    let msg =
                        buildMimeMessage
                            testSmtpConfig
                            "to@example.com"
                            "Alice"
                            "Subject"
                            "Body"
                            (Some "bcc@example.com")
                            None

                    Expect.equal msg.Bcc.Count 1 "one BCC recipient"
                    let bccAddr = msg.Bcc.[0] :?> MailboxAddress
                    Expect.equal bccAddr.Address "bcc@example.com" "BCC address matches"
                }

                test "no BCC header when bcc is None" {
                    let msg =
                        buildMimeMessage testSmtpConfig "to@example.com" "Alice" "Subject" "Body" None None

                    Expect.equal msg.Bcc.Count 0 "no BCC recipients"
                }

                test "method parameter matches ics content method" {
                    let booking = makeBooking ()

                    let icsContent = buildConfirmationIcs booking "host@example.com" "Brian" None None

                    let msg =
                        buildMimeMessage
                            testSmtpConfig
                            "to@example.com"
                            "Alice"
                            "Subject"
                            "Body"
                            None
                            (Some
                                { Content = icsContent
                                  Method = "REQUEST" })

                    let mp = msg.Body :?> Multipart
                    let calPart = mp.[1] :?> MimePart
                    let mimeMethod = calPart.ContentType.Parameters.["method"]

                    use ms = new MemoryStream()
                    calPart.Content.DecodeTo(ms)
                    let content = Encoding.UTF8.GetString(ms.ToArray())

                    Expect.stringContains content $"METHOD:{mimeMethod}" "ics content METHOD matches MIME method"
                } ]

          testList
              "buildConfirmationMimeMessage"
              [ test "To is participant email with display name" {
                    let booking = makeBooking ()

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    Expect.equal msg.To.Count 1 "one To recipient"
                    let toAddr = msg.To.[0] :?> MailboxAddress
                    Expect.equal toAddr.Address booking.ParticipantEmail "To address is participant email"
                    Expect.equal toAddr.Name booking.ParticipantName "To name is participant name"
                }

                test "BCC is host email" {
                    let booking = makeBooking ()

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    Expect.equal msg.Bcc.Count 1 "one BCC recipient"
                    let bccAddr = msg.Bcc.[0] :?> MailboxAddress
                    Expect.equal bccAddr.Address testNotificationConfig.HostEmail "BCC is host email"
                }

                test "Subject contains booking title and Confirmed" {
                    let booking = makeBooking ()

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    Expect.stringContains msg.Subject booking.Title "subject contains booking title"
                    Expect.stringContains msg.Subject "Confirmed" "subject contains Confirmed"
                }

                test "cancellationUrl from token reaches email body" {
                    // The plumbing: buildCancellationUrl → buildConfirmationEmailContent
                    // must pass the URL into the body so the participant can cancel.
                    let booking = makeBooking () // CancellationToken = Some "ABCDEF..."

                    let expectedUrl =
                        $"{testNotificationConfig.PublicUrl}/cancel/{booking.Id}/{Option.get booking.CancellationToken}"

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    let mp = msg.Body :?> Multipart
                    let textPart = mp.[0] :?> TextPart
                    Expect.stringContains textPart.Text expectedUrl "body contains cancellation URL"
                }

                test "cancellationUrl from token reaches ICS DESCRIPTION" {
                    // The plumbing: buildCancellationUrl → buildConfirmationIcs
                    // must also pass the URL into the ICS attachment's DESCRIPTION
                    // field so calendar clients display the cancel link.
                    let booking = makeBooking ()

                    let expectedUrl =
                        $"{testNotificationConfig.PublicUrl}/cancel/{booking.Id}/{Option.get booking.CancellationToken}"

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    let mp = msg.Body :?> Multipart
                    let calPart = mp.[1] :?> MimePart

                    use ms = new MemoryStream()
                    calPart.Content.DecodeTo(ms)
                    let icsText = Encoding.UTF8.GetString(ms.ToArray())
                    let cal = Calendar.Load(icsText)
                    let evt = cal.Events.[0]

                    Expect.stringContains evt.Description expectedUrl "ICS DESCRIPTION contains cancellation URL"
                }

                test "no cancellationUrl in body or ICS DESCRIPTION when booking has no token" {
                    // Composition guard: buildCancellationUrl returns None → both
                    // buildConfirmationEmailContent and buildConfirmationIcs must
                    // receive None and produce no /cancel/ text. A bug that
                    // fabricated a Some "" or passed a stale URL would be caught here.
                    let booking =
                        { makeBooking () with
                            CancellationToken = None }

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    let mp = msg.Body :?> Multipart
                    let textPart = mp.[0] :?> TextPart

                    Expect.isFalse
                        (textPart.Text.Contains("/cancel/"))
                        "email body must not contain a cancellation URL when booking has no token"

                    let calPart = mp.[1] :?> MimePart

                    use ms = new MemoryStream()
                    calPart.Content.DecodeTo(ms)
                    let icsText = Encoding.UTF8.GetString(ms.ToArray())
                    let cal = Calendar.Load(icsText)
                    let evt = cal.Events.[0]

                    let description = if isNull evt.Description then "" else evt.Description

                    Expect.isFalse
                        (description.Contains("/cancel/"))
                        "ICS DESCRIPTION must not contain a cancellation URL when booking has no token"
                }

                test "ICS attachment has METHOD REQUEST" {
                    let booking = makeBooking ()

                    use msg = buildConfirmationMimeMessage testNotificationConfig booking None

                    let mp = msg.Body :?> Multipart
                    let calPart = mp.[1] :?> MimePart
                    Expect.equal (calPart.ContentType.Parameters.["method"]) "REQUEST" "ICS MIME method is REQUEST"

                    use ms = new MemoryStream()
                    calPart.Content.DecodeTo(ms)
                    let icsText = Encoding.UTF8.GetString(ms.ToArray())
                    let cal = Calendar.Load(icsText)
                    Expect.equal cal.Method "REQUEST" "iCalendar METHOD property is REQUEST"
                } ]

          testList
              "buildSmtpConfig"
              [ let envFrom (vars: (string * string) list) =
                    let lookup = Map.ofList vars
                    fun name -> Map.tryFind name lookup

                let requiredVars =
                    [ "MICHAEL_SMTP_HOST", "mail.example.com"
                      "MICHAEL_SMTP_PORT", "587"
                      "MICHAEL_SMTP_FROM", "noreply@example.com" ]

                test "returns Ok None when no env vars are set" {
                    let result = buildSmtpConfig (envFrom [])
                    Expect.equal result (Ok None) "no config when nothing set"
                }

                test "returns Ok None when host is missing" {
                    let vars = [ "MICHAEL_SMTP_PORT", "587"; "MICHAEL_SMTP_FROM", "a@b.com" ]
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.equal result (Ok None) "no config without host"
                }

                test "returns Ok None when port is missing" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"; "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.equal result (Ok None) "no config without port"
                }

                test "returns Ok None when from address is missing" {
                    let vars = [ "MICHAEL_SMTP_HOST", "mail.example.com"; "MICHAEL_SMTP_PORT", "587" ]
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.equal result (Ok None) "no config without from"
                }

                test "returns Error when host contains spaces" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail example.com"
                          "MICHAEL_SMTP_PORT", "587"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "host with spaces is invalid"
                }

                test "returns Error when host contains URL scheme" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "http://mail.example.com"
                          "MICHAEL_SMTP_PORT", "587"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "host with scheme is invalid"
                }

                test "returns Error when host is empty" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "  "
                          "MICHAEL_SMTP_PORT", "587"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "whitespace-only host is invalid"
                }

                test "returns Error when from address is empty string" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"
                          "MICHAEL_SMTP_PORT", "587"
                          "MICHAEL_SMTP_FROM", "" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "empty from address is invalid"
                }

                test "returns Error when from address contains newlines" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"
                          "MICHAEL_SMTP_PORT", "587"
                          "MICHAEL_SMTP_FROM", "a@b.com\r\nBcc: attacker@evil.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "from address with header injection is invalid"
                }

                test "returns Error when port is non-numeric" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"
                          "MICHAEL_SMTP_PORT", "abc"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "non-numeric port is an error"
                }

                test "returns Error when port is zero" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"
                          "MICHAEL_SMTP_PORT", "0"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "port 0 is out of range"
                }

                test "returns Error when port is negative" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"
                          "MICHAEL_SMTP_PORT", "-1"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "negative port is out of range"
                }

                test "returns Error when port exceeds 65535" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "mail.example.com"
                          "MICHAEL_SMTP_PORT", "99999"
                          "MICHAEL_SMTP_FROM", "a@b.com" ]

                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "port above 65535 is out of range"
                }

                test "builds valid config with required fields only" {
                    let result = buildSmtpConfig (envFrom requiredVars)

                    let config = Expect.wantOk result "should be Ok" |> Option.get

                    Expect.equal config.Host "mail.example.com" "host"
                    Expect.equal config.Port 587 "port"
                    Expect.equal config.FromAddress "noreply@example.com" "from address"
                    Expect.equal config.FromName "Michael" "default from name"
                    Expect.isNone config.Username "no username"
                    Expect.isNone config.Password "no password"
                    Expect.equal config.TlsMode StartTls "TLS defaults to StartTls"
                }

                test "TlsMode defaults to StartTls when MICHAEL_SMTP_TLS is unset" {
                    let result = buildSmtpConfig (envFrom requiredVars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode StartTls "defaults to StartTls"
                }

                test "TlsMode is NoTls for 'false'" {
                    let vars = ("MICHAEL_SMTP_TLS", "false") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode NoTls "false → NoTls"
                }

                test "TlsMode is NoTls for 'False' (case-insensitive)" {
                    let vars = ("MICHAEL_SMTP_TLS", "False") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode NoTls "False → NoTls"
                }

                test "TlsMode is NoTls for 'none'" {
                    let vars = ("MICHAEL_SMTP_TLS", "none") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode NoTls "none → NoTls"
                }

                test "TlsMode is StartTls for 'true'" {
                    let vars = ("MICHAEL_SMTP_TLS", "true") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode StartTls "true → StartTls"
                }

                test "TlsMode is StartTls for 'starttls'" {
                    let vars = ("MICHAEL_SMTP_TLS", "starttls") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode StartTls "starttls → StartTls"
                }

                test "TlsMode is StartTls for 'STARTTLS' (case-insensitive)" {
                    let vars = ("MICHAEL_SMTP_TLS", "STARTTLS") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode StartTls "STARTTLS → StartTls"
                }

                test "TlsMode is SslOnConnect for 'sslon'" {
                    let vars = ("MICHAEL_SMTP_TLS", "sslon") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode SslOnConnect "sslon → SslOnConnect"
                }

                test "TlsMode is SslOnConnect for 'sslonconnect'" {
                    let vars = ("MICHAEL_SMTP_TLS", "sslonconnect") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.TlsMode SslOnConnect "sslonconnect → SslOnConnect"
                }

                test "returns Error for unrecognized TLS value" {
                    let vars = ("MICHAEL_SMTP_TLS", "yes") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "unrecognized value is an error"
                }

                test "returns Error for '0' (not a valid TLS mode)" {
                    let vars = ("MICHAEL_SMTP_TLS", "0") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "'0' is not a valid TLS mode"
                }

                test "returns Error for 'no' (not a valid TLS mode)" {
                    let vars = ("MICHAEL_SMTP_TLS", "no") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "'no' is not a valid TLS mode"
                }

                test "includes credentials when both provided" {
                    let vars =
                        ("MICHAEL_SMTP_USERNAME", "user@example.com")
                        :: ("MICHAEL_SMTP_PASSWORD", "secret")
                        :: requiredVars

                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.Username (Some "user@example.com") "username set"
                    Expect.equal config.Password (Some "secret") "password set"
                }

                test "username without password returns Error" {
                    let vars = ("MICHAEL_SMTP_USERNAME", "user@example.com") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "username without password is an error"
                }

                test "password without username returns Error" {
                    let vars = ("MICHAEL_SMTP_PASSWORD", "secret") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    Expect.isError result "password without username is an error"
                }

                test "custom from name is used" {
                    let vars = ("MICHAEL_SMTP_FROM_NAME", "Scheduler") :: requiredVars
                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get
                    Expect.equal config.FromName "Scheduler" "custom from name"
                }

                test "full configuration with all fields" {
                    let vars =
                        [ "MICHAEL_SMTP_HOST", "smtp.fastmail.com"
                          "MICHAEL_SMTP_PORT", "465"
                          "MICHAEL_SMTP_FROM", "cal@example.com"
                          "MICHAEL_SMTP_FROM_NAME", "My Calendar"
                          "MICHAEL_SMTP_USERNAME", "cal@example.com"
                          "MICHAEL_SMTP_PASSWORD", "app-password"
                          "MICHAEL_SMTP_TLS", "true" ]

                    let result = buildSmtpConfig (envFrom vars)
                    let config = Expect.wantOk result "should be Ok" |> Option.get

                    Expect.equal config.Host "smtp.fastmail.com" "host"
                    Expect.equal config.Port 465 "port"
                    Expect.equal config.FromAddress "cal@example.com" "from"
                    Expect.equal config.FromName "My Calendar" "from name"
                    Expect.equal config.Username (Some "cal@example.com") "username"
                    Expect.equal config.Password (Some "app-password") "password"
                    Expect.equal config.TlsMode StartTls "TLS mode"
                } ]

          testList
              "buildNotificationConfig"
              [ test "builds valid config with all fields" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "https://cal.example.com")
                            (Some "host@example.com")
                            (Some "Brian")

                    let config = Expect.wantOk result "should be Ok"
                    Expect.equal config.PublicUrl "https://cal.example.com" "publicUrl"
                    Expect.equal config.HostEmail "host@example.com" "hostEmail"
                    Expect.equal config.HostName "Brian" "hostName"
                }

                test "hostName defaults to Host when None" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "https://cal.example.com")
                            (Some "host@example.com")
                            None

                    let config = Expect.wantOk result "should be Ok"
                    Expect.equal config.HostName "Host" "default hostName"
                }

                test "trims trailing slash from publicUrl" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "https://cal.example.com/")
                            (Some "host@example.com")
                            None

                    let config = Expect.wantOk result "should be Ok"
                    Expect.equal config.PublicUrl "https://cal.example.com" "trailing slash trimmed"
                }

                test "trims multiple trailing slashes from publicUrl" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "https://cal.example.com///")
                            (Some "host@example.com")
                            None

                    let config = Expect.wantOk result "should be Ok"
                    Expect.equal config.PublicUrl "https://cal.example.com" "trailing slashes trimmed"
                }

                test "returns Error when publicUrl is None" {
                    let result =
                        buildNotificationConfig testSmtpConfig None (Some "host@example.com") None

                    Expect.isError result "missing publicUrl is an error"
                }

                test "returns Error when publicUrl has no scheme" {
                    let result =
                        buildNotificationConfig testSmtpConfig (Some "cal.example.com") (Some "host@example.com") None

                    Expect.isError result "URL without scheme is an error"
                }

                test "returns Error when publicUrl is just a scheme" {
                    let result =
                        buildNotificationConfig testSmtpConfig (Some "https://") (Some "host@example.com") None

                    Expect.isError result "scheme-only URL is an error"
                }

                test "returns Error when publicUrl has empty host (https:///path)" {
                    // Regression: the previous manual prefix/substring check accepted
                    // "https:///path" because the portion after "://" was "/path"
                    // (not whitespace). Uri.TryCreate correctly parses this as an
                    // empty-host URI and the host check rejects it.
                    let result =
                        buildNotificationConfig testSmtpConfig (Some "https:///path") (Some "host@example.com") None

                    Expect.isError result "URL with empty host must be rejected"
                }

                test "accepts http:// scheme" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "http://localhost:8080")
                            (Some "host@example.com")
                            None

                    Expect.isOk result "http scheme is valid"
                }

                test "returns Error when hostEmail is None" {
                    let result =
                        buildNotificationConfig testSmtpConfig (Some "https://cal.example.com") None None

                    Expect.isError result "missing hostEmail is an error"
                }

                test "returns Error when hostEmail is invalid" {
                    let result =
                        buildNotificationConfig testSmtpConfig (Some "https://cal.example.com") (Some "") None

                    Expect.isError result "invalid email is an error"
                }

                test "returns Error when hostEmail contains header injection" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "https://cal.example.com")
                            (Some "a@b.com\r\nBcc: attacker@evil.com")
                            None

                    Expect.isError result "header injection in email is an error"
                }

                test "attaches smtp config to result" {
                    let result =
                        buildNotificationConfig
                            testSmtpConfig
                            (Some "https://cal.example.com")
                            (Some "host@example.com")
                            None

                    let config = Expect.wantOk result "should be Ok"
                    Expect.equal config.Smtp testSmtpConfig "smtp config forwarded"
                } ] ]
