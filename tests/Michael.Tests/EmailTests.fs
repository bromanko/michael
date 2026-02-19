module Michael.Tests.EmailTests

open System
open Expecto
open NodaTime
open NodaTime.Text
open Michael.Domain
open Michael.Email

let private makeBooking () =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.NewGuid()
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
      CreatedAt = SystemClock.Instance.GetCurrentInstant() }

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
                    let content = buildCancellationEmailContent booking true None
                    Expect.stringContains content.Subject "Project Review" "subject contains title"
                    Expect.stringContains content.Subject "Cancelled" "subject indicates cancellation"
                }

                test "body includes all booking details" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true None
                    Expect.stringContains content.Body "Project Review" "body contains title"
                    Expect.stringContains content.Body "2026-02-15" "body contains date"
                    Expect.stringContains content.Body "14:00" "body contains start time"
                    Expect.stringContains content.Body "15:00" "body contains end time"
                    Expect.stringContains content.Body "America/New_York" "body contains timezone"
                    Expect.stringContains content.Body "60 minutes" "body contains duration"
                }

                test "host cancellation shows correct message" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true None
                    Expect.stringContains content.Body "The host has cancelled" "host cancellation message"
                }

                test "non-host cancellation shows correct message" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking false None
                    Expect.stringContains content.Body "This meeting has been cancelled" "generic cancellation message"
                }

                test "includes video link when provided" {
                    let booking = makeBooking ()

                    let content =
                        buildCancellationEmailContent booking true (Some "https://zoom.us/j/123456")

                    Expect.stringContains content.Body "https://zoom.us/j/123456" "body contains video link"
                }

                test "omits video link when None" {
                    let booking = makeBooking ()
                    let content = buildCancellationEmailContent booking true None
                    Expect.isFalse (content.Body.Contains("Video link:")) "no video link line"
                } ]

          testList
              "buildConfirmationEmailContent"
              [ test "includes booking title in subject" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None
                    Expect.stringContains content.Subject "Project Review" "subject contains title"
                    Expect.stringContains content.Subject "Confirmed" "subject indicates confirmation"
                }

                test "body includes all booking details" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None
                    Expect.stringContains content.Body "Project Review" "body contains title"
                    Expect.stringContains content.Body "2026-02-15" "body contains date"
                    Expect.stringContains content.Body "14:00" "body contains start time"
                    Expect.stringContains content.Body "15:00" "body contains end time"
                    Expect.stringContains content.Body "America/New_York" "body contains timezone"
                    Expect.stringContains content.Body "60 minutes" "body contains duration"
                }

                test "includes description when present" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None
                    Expect.stringContains content.Body "Quarterly review meeting" "body contains description"
                }

                test "omits description line when None" {
                    let booking =
                        { makeBooking () with
                            Description = None }

                    let content = buildConfirmationEmailContent booking None
                    Expect.isFalse (content.Body.Contains("Description:")) "no description line"
                }

                test "includes video link when provided" {
                    let booking = makeBooking ()

                    let content =
                        buildConfirmationEmailContent booking (Some "https://meet.google.com/abc-def")

                    Expect.stringContains content.Body "https://meet.google.com/abc-def" "body contains video link"
                }

                test "omits video link when None" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking None
                    Expect.isFalse (content.Body.Contains("Video link:")) "no video link line"
                }

                test "omits video link when whitespace" {
                    let booking = makeBooking ()
                    let content = buildConfirmationEmailContent booking (Some "  ")
                    Expect.isFalse (content.Body.Contains("Video link:")) "no video link for whitespace"
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
                } ] ]
