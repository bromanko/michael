module Michael.Email

open System
open System.Threading.Tasks
open MailKit.Net.Smtp
open MailKit.Security
open MimeKit
open NodaTime
open Serilog
open Michael.Domain
open Michael.Sanitize

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

/// TLS mode for SMTP connections. Maps to MailKit's SecureSocketOptions.
type TlsMode =
    /// No encryption. Only safe for local dev servers (e.g. Mailpit).
    | NoTls
    /// STARTTLS — connect in cleartext then upgrade. Standard for port 587.
    | StartTls
    /// Implicit TLS — the connection is encrypted from the start. Standard for port 465.
    | SslOnConnect

type SmtpConfig =
    { Host: string
      Port: int
      Username: string option
      Password: string option
      TlsMode: TlsMode
      FromAddress: string
      FromName: string }

/// Bundles everything needed for sending notification emails.
/// None when SMTP is not configured; Some when it is.
type NotificationConfig =
    { Smtp: SmtpConfig
      HostEmail: string
      HostName: string
      PublicUrl: string }

/// An iCalendar attachment to include in an email.
type IcsAttachment = { Content: string; Method: string }

/// Validate an email address using MailKit's parser, which guards against
/// header injection (newlines) and structurally invalid addresses.
/// The parsed result is checked to ensure the Address property is non-empty,
/// catching any edge case where TryParse succeeds but yields an empty string.
let private isValidMailboxAddress (address: string) =
    let mutable parsed = Unchecked.defaultof<MailboxAddress>

    MailboxAddress.TryParse(address, &parsed)
    && not (String.IsNullOrEmpty(parsed.Address))

/// Build a NotificationConfig from raw environment values.
/// Returns Ok config when all values are valid, or Error with a
/// user-facing message on the first validation failure.
let buildNotificationConfig
    (smtp: SmtpConfig)
    (publicUrl: string option)
    (hostEmail: string option)
    (hostName: string option)
    : Result<NotificationConfig, string> =
    match publicUrl with
    | None -> Error "MICHAEL_PUBLIC_URL is required when SMTP is configured."
    | Some rawUrl ->
        let url = rawUrl.TrimEnd('/')

        // Use Uri.TryCreate for structural validation: this correctly rejects
        // inputs like "https:///path" (empty host) that the previous manual
        // prefix-and-substring check would have accepted.
        let mutable parsedUri = Unchecked.defaultof<Uri>

        let isValidUrl =
            Uri.TryCreate(url, UriKind.Absolute, &parsedUri)
            && (parsedUri.Scheme = "http" || parsedUri.Scheme = "https")
            && not (String.IsNullOrEmpty(parsedUri.Host))

        if not isValidUrl then
            Error $"MICHAEL_PUBLIC_URL must be a valid http:// or https:// URL, got: '{url}'"
        else
            match hostEmail with
            | None -> Error "MICHAEL_HOST_EMAIL is required when SMTP is configured."
            | Some email ->
                if not (isValidMailboxAddress email) then
                    Error $"MICHAEL_HOST_EMAIL is not a valid email address: '{email}'"
                else
                    Ok
                        { Smtp = smtp
                          HostEmail = email
                          HostName = hostName |> Option.defaultValue "Host"
                          PublicUrl = url }

/// A hostname must be non-empty, contain no whitespace or URI-scheme
/// characters, and have no path separators. This rejects obvious
/// misconfigurations like "http://host" or "host name" while still
/// accepting IPs, FQDNs, and localhost.
let private isValidSmtpHost (host: string) =
    not (String.IsNullOrWhiteSpace(host))
    && not (host.Contains(' '))
    && not (host.Contains('/'))
    && not (host.Contains(':'))

/// Build an SmtpConfig from an environment-variable reader.
/// Returns Ok (Some config) when configured, Ok None when the required
/// variables (host, port, from) are absent, or Error when present but
/// invalid (e.g. non-numeric port).
let buildSmtpConfig (getEnv: string -> string option) : Result<SmtpConfig option, string> =
    let host = getEnv "MICHAEL_SMTP_HOST"
    let port = getEnv "MICHAEL_SMTP_PORT"
    let username = getEnv "MICHAEL_SMTP_USERNAME"
    let password = getEnv "MICHAEL_SMTP_PASSWORD"
    let fromAddress = getEnv "MICHAEL_SMTP_FROM"
    let fromName = getEnv "MICHAEL_SMTP_FROM_NAME"

    let tlsModeResult =
        match getEnv "MICHAEL_SMTP_TLS" with
        | None -> Ok StartTls
        | Some v when
            String.Equals(v, "false", StringComparison.OrdinalIgnoreCase)
            || String.Equals(v, "none", StringComparison.OrdinalIgnoreCase)
            ->
            Ok NoTls
        | Some v when
            String.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || String.Equals(v, "starttls", StringComparison.OrdinalIgnoreCase)
            ->
            Ok StartTls
        | Some v when
            String.Equals(v, "sslon", StringComparison.OrdinalIgnoreCase)
            || String.Equals(v, "sslonconnect", StringComparison.OrdinalIgnoreCase)
            ->
            Ok SslOnConnect
        | Some v -> Error $"Invalid MICHAEL_SMTP_TLS value: '{v}'. Expected: none, starttls, sslon, true, or false."

    match host, port, fromAddress with
    | Some h, Some p, Some from ->
        if not (isValidSmtpHost h) then
            Error $"Invalid SMTP host: '{h}'. Must be a hostname or IP address."
        elif not (isValidMailboxAddress from) then
            Error $"Invalid SMTP from address: '{from}'."
        else
            match tlsModeResult with
            | Error msg -> Error msg
            | Ok tlsMode ->
                match Int32.TryParse(p) with
                | true, portNum when portNum >= 1 && portNum <= 65535 ->
                    match username, password with
                    | Some _, None ->
                        Error
                            "SMTP username configured without password. Set both MICHAEL_SMTP_USERNAME and MICHAEL_SMTP_PASSWORD, or neither."
                    | None, Some _ ->
                        Error
                            "SMTP password configured without username. Set both MICHAEL_SMTP_USERNAME and MICHAEL_SMTP_PASSWORD, or neither."
                    | _ ->
                        Ok(
                            Some
                                { Host = h
                                  Port = portNum
                                  Username = username
                                  Password = password
                                  TlsMode = tlsMode
                                  FromAddress = from
                                  FromName = fromName |> Option.defaultValue "Michael" }
                        )
                | true, _ -> Error $"SMTP port out of valid range (1-65535): {p}"
                | false, _ -> Error $"Invalid SMTP port: {p}"
    | _ -> Ok None

// ---------------------------------------------------------------------------
// .ics calendar attachment generation
// ---------------------------------------------------------------------------

open System.IO
open System.Text
open Ical.Net
open Ical.Net.CalendarComponents
open Ical.Net.DataTypes
open Ical.Net.Serialization

let private toCalDateTime (odt: OffsetDateTime) =
    let utc = odt.ToInstant().ToDateTimeUtc()
    CalDateTime(utc, "UTC")

let private instantToCalDateTime (instant: Instant) =
    CalDateTime(instant.ToDateTimeUtc(), "UTC")

/// Generate a VCALENDAR with METHOD:REQUEST for a booking confirmation.
///
/// All user-supplied strings are passed through stripControlChars before
/// being assigned to iCal properties. Ical.Net correctly escapes TEXT
/// property values (SUMMARY, DESCRIPTION, LOCATION) per RFC 5545 §3.3.11,
/// but it does NOT escape control characters inside PARAM values such as CN:
/// an embedded CRLF in a CommonName breaks the property line and causes parse
/// errors in calendar clients. Stripping at this layer is defence in depth —
/// the booking handler already sanitizes input, but the ICS builders must be
/// safe regardless of how they are called.
let buildConfirmationIcs
    (booking: Booking)
    (hostEmail: string)
    (hostName: string)
    (videoLink: string option)
    (cancellationUrl: string option)
    : string =
    let cal = Calendar()
    cal.Method <- "REQUEST"
    cal.AddProperty("PRODID", "-//Michael//Michael//EN")

    let evt = CalendarEvent()
    evt.Uid <- $"{booking.Id}@michael"
    evt.DtStamp <- instantToCalDateTime booking.CreatedAt
    evt.DtStart <- toCalDateTime booking.StartTime
    evt.DtEnd <- toCalDateTime booking.EndTime
    evt.Summary <- stripControlChars booking.Title

    let desc =
        match booking.Description, cancellationUrl with
        | Some d, Some url -> $"{stripControlChars d}\nTo cancel this meeting, visit: {url}"
        | Some d, None -> stripControlChars d
        | None, Some url -> $"To cancel this meeting, visit: {url}"
        | None, None -> ""

    if desc <> "" then
        evt.Description <- desc

    match videoLink with
    | Some link when not (String.IsNullOrWhiteSpace(link)) -> evt.Location <- link
    | _ -> ()

    let organizer = Organizer($"MAILTO:{hostEmail}")
    organizer.CommonName <- stripControlChars hostName
    evt.Organizer <- organizer

    let attendee = Attendee($"MAILTO:{booking.ParticipantEmail}")
    attendee.CommonName <- stripControlChars booking.ParticipantName
    evt.Attendees.Add(attendee)

    evt.Status <- "CONFIRMED"
    evt.Sequence <- 0

    cal.Events.Add(evt)
    // CalendarSerializer is NOT thread-safe (its SerializationContext uses
    // an internal stack) — allocate a fresh instance per call.
    // CalendarSerializer does not implement IDisposable, so let (not use).
    let serializer = CalendarSerializer()
    serializer.SerializeToString(cal)

/// Generate a VCALENDAR with METHOD:CANCEL for a booking cancellation.
/// See buildConfirmationIcs for the rationale behind stripControlChars calls.
let buildCancellationIcs (booking: Booking) (hostEmail: string) (hostName: string) (cancelledAt: Instant) : string =
    let cal = Calendar()
    cal.Method <- "CANCEL"
    cal.AddProperty("PRODID", "-//Michael//Michael//EN")

    let evt = CalendarEvent()
    evt.Uid <- $"{booking.Id}@michael"
    evt.DtStamp <- instantToCalDateTime cancelledAt
    evt.DtStart <- toCalDateTime booking.StartTime
    evt.DtEnd <- toCalDateTime booking.EndTime
    evt.Summary <- $"Cancelled: {stripControlChars booking.Title}"

    let organizer = Organizer($"MAILTO:{hostEmail}")
    organizer.CommonName <- stripControlChars hostName
    evt.Organizer <- organizer

    let attendee = Attendee($"MAILTO:{booking.ParticipantEmail}")
    attendee.CommonName <- stripControlChars booking.ParticipantName
    evt.Attendees.Add(attendee)

    evt.Status <- "CANCELLED"
    evt.Sequence <- 1

    cal.Events.Add(evt)
    let serializer = CalendarSerializer()
    serializer.SerializeToString(cal)

// ---------------------------------------------------------------------------
// Email sending
// ---------------------------------------------------------------------------

let private log () =
    Log.ForContext("SourceContext", "Michael.Email")

/// Build a MimeMessage for testability. Extracted from sendEmail so unit
/// tests can inspect the full MIME structure without an SMTP server.
let buildMimeMessage
    (config: SmtpConfig)
    (toAddress: string)
    (toName: string)
    (subject: string)
    (body: string)
    (bcc: string option)
    (icsAttachment: IcsAttachment option)
    : MimeMessage =
    let message = new MimeMessage()
    message.From.Add(new MailboxAddress(config.FromName, config.FromAddress))
    message.To.Add(new MailboxAddress(toName, toAddress))
    message.Subject <- subject

    match bcc with
    | Some bccAddress -> message.Bcc.Add(new MailboxAddress(null, bccAddress))
    | None -> ()

    match icsAttachment with
    | Some ics ->
        let textPart = new TextPart("plain", Text = body)

        let calendarPart = new MimePart("text", "calendar")
        calendarPart.ContentType.Parameters.Add("charset", "utf-8")
        calendarPart.ContentType.Parameters.Add("method", ics.Method)

        calendarPart.ContentDisposition <- new ContentDisposition(ContentDisposition.Inline, FileName = "invite.ics")

        // MimeContent takes ownership of the MemoryStream and will dispose it
        // when the owning MimeMessage is disposed (via MimePart → MimeContent).
        // Do NOT wrap in `use` — the stream must remain open for serialization.
        calendarPart.Content <- new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(ics.Content)))

        let multipart = new Multipart("mixed")
        multipart.Add(textPart)
        multipart.Add(calendarPart)
        message.Body <- multipart
    | None ->
        let bodyBuilder = new BodyBuilder()
        bodyBuilder.TextBody <- body
        message.Body <- bodyBuilder.ToMessageBody()

    message

let sendEmail
    (config: SmtpConfig)
    (toAddress: string)
    (toName: string)
    (subject: string)
    (body: string)
    (bcc: string option)
    (icsAttachment: IcsAttachment option)
    : Task<Result<unit, string>> =
    task {
        try
            use message =
                buildMimeMessage config toAddress toName subject body bcc icsAttachment

            use client = new SmtpClient()
            client.Timeout <- 10_000 // 10 seconds — prevents unbounded blocking on hung SMTP servers

            let tlsOption =
                match config.TlsMode, config.Username with
                | NoTls, Some _ ->
                    failwith
                        "SMTP credentials configured without TLS — refusing to send credentials in cleartext. Set MICHAEL_SMTP_TLS to starttls or sslon, or remove credentials."
                | NoTls, None -> SecureSocketOptions.None
                | StartTls, _ -> SecureSocketOptions.StartTls
                | SslOnConnect, _ -> SecureSocketOptions.SslOnConnect

            do! client.ConnectAsync(config.Host, config.Port, tlsOption)

            let! authResult =
                task {
                    match config.Username, config.Password with
                    | Some user, Some pass ->
                        do! client.AuthenticateAsync(user, pass)
                        return Ok()
                    | None, None -> return Ok()
                    | Some _, None
                    | None, Some _ ->
                        return
                            Error
                                "SMTP partially configured: both Username and Password must be set for authentication."
                }

            match authResult with
            | Error msg -> return Error msg
            | Ok() ->
                let! _ = client.SendAsync(message)
                do! client.DisconnectAsync(true)
                log().Information("Email sent to {ToAddress} with subject '{Subject}'", toAddress, subject)
                return Ok()
        with ex ->
            log().Error(ex, "Failed to send email to {ToAddress}: {Error}", toAddress, ex.Message)
            return Error ex.Message
    }

// ---------------------------------------------------------------------------
// Booking notification emails
// ---------------------------------------------------------------------------

let formatBookingDate (odt: OffsetDateTime) =
    let date = odt.Date
    $"{date.Year}-{date.Month:D2}-{date.Day:D2}"

let formatBookingTime (odt: OffsetDateTime) =
    let time = odt.TimeOfDay
    $"{time.Hour:D2}:{time.Minute:D2}"

type BookingEmailContent = { Subject: string; Body: string }

let private videoLinkLine (videoLink: string option) =
    match videoLink with
    | Some link when not (System.String.IsNullOrWhiteSpace(link)) -> $"Video link: {link}\n"
    | _ -> ""

let buildCancellationEmailContent (booking: Booking) (cancelledByHost: bool) : BookingEmailContent =
    let subject = $"Meeting Cancelled: {booking.Title}"

    let cancelledBy =
        if cancelledByHost then
            "The host has cancelled"
        else
            "You have cancelled"

    let body =
        $"""{cancelledBy} the following meeting:

Title: {booking.Title}
Date: {formatBookingDate booking.StartTime}
Time: {formatBookingTime booking.StartTime} - {formatBookingTime booking.EndTime} ({booking.Timezone})
Duration: {booking.DurationMinutes} minutes

If you'd like to reschedule, please book a new time.

---
This is an automated message from Michael.
"""

    { Subject = subject; Body = body }

let buildConfirmationEmailContent
    (booking: Booking)
    (videoLink: string option)
    (cancellationUrl: string option)
    : BookingEmailContent =
    let subject = $"Meeting Confirmed: {booking.Title}"

    let descriptionLine =
        match booking.Description with
        | Some desc -> $"Description: {desc}\n"
        | None -> ""

    let cancellationLine =
        match cancellationUrl with
        | Some url -> $"To cancel this meeting, visit: {url}\n"
        | None -> ""

    let body =
        $"""Your meeting has been confirmed:

Title: {booking.Title}
Date: {formatBookingDate booking.StartTime}
Time: {formatBookingTime booking.StartTime} - {formatBookingTime booking.EndTime} ({booking.Timezone})
Duration: {booking.DurationMinutes} minutes
{videoLinkLine videoLink}{descriptionLine}{cancellationLine}
---
This is an automated message from Michael.
"""

    { Subject = subject; Body = body }

/// Build the participant-facing cancellation URL for a booking.
/// Returns None when the booking has no cancellation token.
let buildCancellationUrl (publicUrl: string) (booking: Booking) : string option =
    booking.CancellationToken
    |> Option.map (fun token -> $"{publicUrl}/cancel/{booking.Id}/{token}")

let sendBookingConfirmationEmail
    (config: NotificationConfig)
    (booking: Booking)
    (videoLink: string option)
    : Task<Result<unit, string>> =
    let cancellationUrl = buildCancellationUrl config.PublicUrl booking

    let content = buildConfirmationEmailContent booking videoLink cancellationUrl

    let icsContent =
        buildConfirmationIcs booking config.HostEmail config.HostName videoLink cancellationUrl

    sendEmail
        config.Smtp
        booking.ParticipantEmail
        booking.ParticipantName
        content.Subject
        content.Body
        (Some config.HostEmail)
        (Some
            { Content = icsContent
              Method = "REQUEST" })

let sendBookingCancellationEmail
    (config: NotificationConfig)
    (booking: Booking)
    (cancelledByHost: bool)
    (cancelledAt: Instant)
    : Task<Result<unit, string>> =
    let content = buildCancellationEmailContent booking cancelledByHost

    let icsContent =
        buildCancellationIcs booking config.HostEmail config.HostName cancelledAt

    sendEmail
        config.Smtp
        booking.ParticipantEmail
        booking.ParticipantName
        content.Subject
        content.Body
        (Some config.HostEmail)
        (Some
            { Content = icsContent
              Method = "CANCEL" })
