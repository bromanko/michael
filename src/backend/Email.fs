module Michael.Email

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

type SmtpConfig =
    { Host: string
      Port: int
      Username: string
      Password: string
      FromAddress: string
      FromName: string }

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

            // Connect with STARTTLS (standard for port 587)
            do! client.ConnectAsync(config.Host, config.Port, SecureSocketOptions.StartTls)
            do! client.AuthenticateAsync(config.Username, config.Password)
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

let private formatDate (odt: OffsetDateTime) =
    let date = odt.Date
    $"{date.Year}-{date.Month:D2}-{date.Day:D2}"

let private formatTime (odt: OffsetDateTime) =
    let time = odt.TimeOfDay
    $"{time.Hour:D2}:{time.Minute:D2}"

let sendBookingCancellationEmail
    (config: SmtpConfig)
    (booking: Booking)
    (cancelledByHost: bool)
    : Task<Result<unit, string>> =
    let subject = $"Meeting Cancelled: {booking.Title}"

    let cancelledBy =
        if cancelledByHost then
            "The host has cancelled"
        else
            "This meeting has been cancelled"

    let body =
        $"""{cancelledBy} the following meeting:

Title: {booking.Title}
Date: {formatDate booking.StartTime}
Time: {formatTime booking.StartTime} - {formatTime booking.EndTime} ({booking.Timezone})
Duration: {booking.DurationMinutes} minutes

If you'd like to reschedule, please book a new time.

---
This is an automated message from Michael.
"""

    sendEmail config booking.ParticipantEmail booking.ParticipantName subject body

let sendBookingConfirmationEmail (config: SmtpConfig) (booking: Booking) : Task<Result<unit, string>> =
    let subject = $"Meeting Confirmed: {booking.Title}"

    let descriptionLine =
        match booking.Description with
        | Some desc -> $"Description: {desc}\n"
        | None -> ""

    let body =
        $"""Your meeting has been confirmed:

Title: {booking.Title}
Date: {formatDate booking.StartTime}
Time: {formatTime booking.StartTime} - {formatTime booking.EndTime} ({booking.Timezone})
Duration: {booking.DurationMinutes} minutes

{descriptionLine}If you need to cancel, please contact the host.

---
This is an automated message from Michael.
"""

    sendEmail config booking.ParticipantEmail booking.ParticipantName subject body
