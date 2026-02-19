module Michael.Email

open System
open System.Threading.Tasks
open MailKit.Net.Smtp
open MailKit.Security
open MimeKit
open NodaTime
open Serilog
open Michael.Domain

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

/// A hostname must be non-empty, contain no whitespace or URI-scheme
/// characters, and have no path separators. This rejects obvious
/// misconfigurations like "http://host" or "host name" while still
/// accepting IPs, FQDNs, and localhost.
let private isValidSmtpHost (host: string) =
    not (String.IsNullOrWhiteSpace(host))
    && not (host.Contains(' '))
    && not (host.Contains('/'))
    && not (host.Contains(':'))

/// Validate an email address using MailKit's parser, which guards against
/// header injection (newlines) and structurally invalid addresses.
let private isValidMailboxAddress (address: string) =
    let mutable parsed: MailboxAddress = null
    MailboxAddress.TryParse(address, &parsed)

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
// Email sending
// ---------------------------------------------------------------------------

let private log () =
    Log.ForContext("SourceContext", "Michael.Email")

let sendEmail
    (config: SmtpConfig)
    (toAddress: string)
    (toName: string)
    (subject: string)
    (body: string)
    : Task<Result<unit, string>> =
    task {
        try
            let message = new MimeMessage()
            message.From.Add(MailboxAddress(config.FromName, config.FromAddress))
            message.To.Add(MailboxAddress(toName, toAddress))
            message.Subject <- subject

            let bodyBuilder = BodyBuilder()
            bodyBuilder.TextBody <- body
            message.Body <- bodyBuilder.ToMessageBody()

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

let buildCancellationEmailContent
    (booking: Booking)
    (cancelledByHost: bool)
    (videoLink: string option)
    : BookingEmailContent =
    let subject = $"Meeting Cancelled: {booking.Title}"

    let cancelledBy =
        if cancelledByHost then
            "The host has cancelled"
        else
            "This meeting has been cancelled"

    let body =
        $"""{cancelledBy} the following meeting:

Title: {booking.Title}
Date: {formatBookingDate booking.StartTime}
Time: {formatBookingTime booking.StartTime} - {formatBookingTime booking.EndTime} ({booking.Timezone})
Duration: {booking.DurationMinutes} minutes
{videoLinkLine videoLink}
If you'd like to reschedule, please book a new time.

---
This is an automated message from Michael.
"""

    { Subject = subject; Body = body }

let buildConfirmationEmailContent (booking: Booking) (videoLink: string option) : BookingEmailContent =
    let subject = $"Meeting Confirmed: {booking.Title}"

    let descriptionLine =
        match booking.Description with
        | Some desc -> $"Description: {desc}\n"
        | None -> ""

    let body =
        $"""Your meeting has been confirmed:

Title: {booking.Title}
Date: {formatBookingDate booking.StartTime}
Time: {formatBookingTime booking.StartTime} - {formatBookingTime booking.EndTime} ({booking.Timezone})
Duration: {booking.DurationMinutes} minutes
{videoLinkLine videoLink}{descriptionLine}
If you need to cancel, please contact the host.

---
This is an automated message from Michael.
"""

    { Subject = subject; Body = body }

let sendBookingCancellationEmail
    (config: SmtpConfig)
    (booking: Booking)
    (cancelledByHost: bool)
    (videoLink: string option)
    : Task<Result<unit, string>> =
    let content = buildCancellationEmailContent booking cancelledByHost videoLink
    sendEmail config booking.ParticipantEmail booking.ParticipantName content.Subject content.Body

let sendBookingConfirmationEmail
    (config: SmtpConfig)
    (booking: Booking)
    (videoLink: string option)
    : Task<Result<unit, string>> =
    let content = buildConfirmationEmailContent booking videoLink
    sendEmail config booking.ParticipantEmail booking.ParticipantName content.Subject content.Body
