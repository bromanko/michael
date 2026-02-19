# Email Notifications with .ics Attachments and Cancellation Links

This ExecPlan is a living document. The sections Progress, Surprises & Discoveries,
Decision Log, and Outcomes & Retrospective must be kept up to date as work proceeds.


## Purpose / Big Picture

After this change, when a participant books a meeting through Michael, two things happen beyond what exists today:

1. The confirmation email the participant receives includes an `.ics` calendar attachment (so they can one-click add the meeting to their calendar) and a link they can click to cancel the booking later.
2. The host (you) is BCC'd on every participant email — both confirmations and cancellations — so you get the same information plus the `.ics` attachment, which lets you add the event to your own calendar. (CalDAV write-back is tracked separately as ticket m-5d0e; until that lands, the `.ics` is how the booking reaches your calendar.)

Cancellation emails (sent when the host cancels a booking from the admin dashboard) also gain an `.ics` METHOD:CANCEL attachment, which tells calendar apps to remove the event.

The actual participant-facing cancellation page and endpoint — what happens when someone clicks the cancellation link — is out of scope for this plan. This plan generates the cancellation token, stores it, and includes the link in emails. A separate plan will implement the cancellation flow itself.


## Progress

- [x] Milestone 1: Configuration and database changes
- [x] Milestone 2: .ics calendar attachment generation
- [x] Milestone 3: Enhanced email sending (BCC, attachments, cancellation link)
- [x] Milestone 4: Wire everything together in handlers and Program.fs
- [x] Milestone 5: Tests
- [x] Milestone 6: Update EARS spec and deferred items


## Surprises & Discoveries

- The migration runner splits SQL on semicolons naively, which breaks `CREATE TRIGGER` statements that contain semicolons inside `BEGIN...END` blocks. Removed the database trigger from the migration and rely solely on application-level validation in `buildBooking`. The partial unique index on `cancellation_token` still provides database-level collision protection.


## Decision Log

- Decision: BCC the host on participant emails rather than sending a separate host notification.
  Rationale: Simpler implementation. The host gets the same content, including the .ics attachment. The cancellation link in the email is participant-facing, but the host already has admin cancellation in the dashboard, so this is fine.
  Date: 2026-02-19

- Decision: Store a `cancellation_token` on each booking at creation time. Build cancellation URLs as `{MICHAEL_PUBLIC_URL}/cancel/{booking_id}/{token}`. The endpoint that handles this URL is out of scope.
  Rationale: The token prevents unauthorized cancellation. Using the booking ID in the URL makes lookup efficient. The token is a 32-byte random hex string (64 characters), generated via `System.Security.Cryptography.RandomNumberGenerator`.
  Date: 2026-02-19

- Decision: Use the booking's `Id` (a GUID) as the iCalendar `UID` for the VEVENT, formatted as `{booking-id}@michael`.
  Rationale: The UID must be stable across the REQUEST and CANCEL methods so calendar apps can match them. The booking ID is already unique and immutable. The `@michael` suffix follows the iCalendar convention of `unique-part@domain`.
  Date: 2026-02-19

- Decision: Store the cancellation token as plaintext in the database rather than hashing it.
  Rationale: Hashing protects against a narrow threat — an attacker who can read the database but not write to it could use stolen tokens to forge cancellation links. In Michael's case this threat is not meaningful: the SQLite database lives on the host's own machine, and anyone with read access to it can already see all booking data, cancel bookings directly, or modify the database file. The token grants a single, low-stakes permission (cancelling one booking) and exposes no sensitive data. Hashing would add complexity (either a per-token salt lookup on every cancellation request, or a scheme where the booking ID scopes the lookup and the hash is compared after fetching the row) for no practical security benefit. If Michael ever becomes multi-tenant or moves to a shared database, this decision should be revisited.
  Date: 2026-02-19

- Decision: Enforce "confirmed bookings must have a cancellation token" at both the database and application layers.
  Rationale: Defense in depth. A partial unique index on `cancellation_token WHERE cancellation_token IS NOT NULL` prevents token collisions. A `BEFORE INSERT` trigger rejects confirmed bookings with a NULL token, catching application bugs at the database boundary. SQLite's `ALTER TABLE ADD COLUMN` does not support adding a `CHECK` constraint, but a trigger achieves the same invariant. The application also validates before insertion so errors surface with clear F# stack traces rather than opaque SQLite errors. Historical bookings (pre-migration) keep NULL tokens — they are either already cancelled or were created before the cancellation link feature existed.
  Date: 2026-02-19

- Decision: Emit iCalendar DTSTART/DTEND in UTC (`YYYYMMDDTHHMMSSZ` form) rather than using `DTSTART;TZID=` with timezone references.
  Rationale: When DTSTART uses `TZID=America/New_York`, the .ics file must also include a `VTIMEZONE` block defining that timezone's rules (offset, DST transitions, etc.). Without a matching `VTIMEZONE`, some calendar clients (notably Outlook and older Exchange) silently ignore the timezone or misinterpret the event time. Ical.Net does not always emit `VTIMEZONE` blocks automatically, and manually constructing them is complex and error-prone. UTC is unambiguous, universally supported by every calendar client, and trivial to produce — just convert the NodaTime `OffsetDateTime` to an `Instant` and format as UTC. Calendar apps will display the event in the viewer's local timezone, which is correct behavior. The email body already shows the time in the booking's timezone for human readability, so no information is lost.
  Date: 2026-02-19

- Decision: Make confirmation email sending fire-and-forget. Remove the `confirmBookingWithEmail` function and `ConfirmBookingResult` type that currently roll back bookings on email failure.
  Rationale: The existing admin cancellation handler already treats email as fire-and-forget — the cancellation succeeds even if the email fails. The confirmation flow should be consistent. Rolling back a confirmed booking because an SMTP server is temporarily down is a worse outcome than a confirmed booking with a missing email: the participant saw the "You're booked" page and believes they have a booking, so cancelling it silently is confusing. A missing email is recoverable (the host sees the booking in the admin dashboard and can follow up manually), but a silently-cancelled booking is not. The implementation becomes simpler too — `handleBook` inserts the booking, returns success, then fires off the email without awaiting its result for the HTTP response.
  Date: 2026-02-19

- Decision: Make `MICHAEL_PUBLIC_URL` and `MICHAEL_HOST_EMAIL` required when SMTP is configured, optional otherwise.
  Rationale: These are meaningless without email sending. When SMTP is configured, the cancellation link and .ics ORGANIZER need them. Failing fast at startup if they're missing prevents broken emails at runtime.
  Date: 2026-02-19


## Outcomes & Retrospective

All milestones complete. Implementation matches the plan with one deviation: the `BEFORE INSERT` trigger was dropped because the migration runner's naive semicolon splitting cannot handle `BEGIN...END` blocks. Application-level validation in `buildBooking` (which asserts the token is non-empty before constructing the `Booking` record) provides the same protection with clearer F# stack traces.

Key outcomes:
- 297 tests passing (net +1 new test, removed 6 `confirmBookingWithEmail` tests, added new .ics, MIME, cancellation URL, and database round-trip tests)
- Confirmation and cancellation emails include `.ics` attachments with correct MIME structure
- Host BCC'd on all participant emails
- Cancellation token stored on every new booking
- `confirmBookingWithEmail` / `ConfirmBookingResult` removed; email is fire-and-forget
- EARS spec updated with sections 23–25 covering email notifications, notification configuration, and cancellation token database


## Context and Orientation

Michael is a self-hosted scheduling tool. Participants visit a booking page, pick a time slot, enter their contact info, and confirm. The backend is F# with Falco (a lightweight web framework on ASP.NET Core), using SQLite for storage and MailKit/MimeKit for email. The frontend is Elm.

Here is how the relevant files fit together:

- `src/backend/Program.fs` — Application entry point. Reads environment variables, constructs configuration, wires up routes. Currently reads SMTP config and passes `smtpConfig: SmtpConfig option` to the booking and cancellation handlers.
- `src/backend/Email.fs` — Module `Michael.Email`. Contains `SmtpConfig`, `buildSmtpConfig`, `sendEmail`, `buildConfirmationEmailContent`, `buildCancellationEmailContent`, `sendBookingConfirmationEmail`, `sendBookingCancellationEmail`. The `sendEmail` function currently sends plain-text email with no attachments and no BCC.
- `src/backend/Handlers.fs` — Module `Michael.Handlers`. Contains `handleBook` (the POST /api/book handler). The handler validates the request, checks slot availability, inserts the booking, then sends a confirmation email. Currently the email-sending is coupled to booking success via `confirmBookingWithEmail` and `ConfirmBookingResult`, which roll back the booking on email failure. This plan removes that coupling — email becomes fire-and-forget, matching the admin cancellation handler's behavior.
- `src/backend/AdminHandlers.fs` — Module `Michael.AdminHandlers`. Contains `handleCancelBooking` (POST /api/admin/bookings/{id}/cancel). Cancels the booking and sends a cancellation email if SMTP is configured (fire-and-forget — cancellation succeeds even if email fails).
- `src/backend/Domain.fs` — Module `Michael.Domain`. Contains the `Booking` type (record with Id, ParticipantName, ParticipantEmail, etc.) and `BookingStatus` (Confirmed | Cancelled). The `Booking` type does not currently have a `CancellationToken` field.
- `src/backend/Database.fs` — Module `Michael.Database`. Contains `insertBookingInternal`, `insertBookingIfSlotAvailable`, `readBooking`, `getBookingById`, `cancelBooking`. The bookings table has columns: id, participant_name, participant_email, participant_phone, title, description, start_time, end_time, start_epoch, end_epoch, duration_minutes, timezone, status, created_at.
- `src/backend/Migrations.fs` — Module `Michael.Migrations`. Runs versioned SQL migration files from `src/backend/migrations/`. Uses Atlas-compatible checksums.
- `src/backend/CalDav.fs` — Already imports `Ical.Net` for parsing CalDAV responses. The `Ical.Net` NuGet package (v5.2.0) is available for generating .ics content too.
- `src/backend/Michael.fsproj` — Already has `MailKit` (4.14.1) and `Ical.Net` (5.2.0) as dependencies. No new NuGet packages are needed.
- `tests/Michael.Tests/EmailTests.fs` — Tests for email content building and SMTP config parsing.
- `tests/Michael.Tests/HandlerTests.fs` — Tests for booking email behavior (currently tests `confirmBookingWithEmail`, which this plan removes).

Key conventions (from AGENTS.md):
- Use `task { }` for async I/O, not `async { }`.
- Inject configuration — don't read environment variables inside library modules.
- Use `Result` for errors, not exceptions.
- Fail fast on missing configuration at startup.


## Plan of Work

The work is organized into six milestones. Each builds on the previous one and is independently verifiable.


### Milestone 1: Configuration and Database Changes

This milestone adds the new environment variables and the database migration for the cancellation token. After this milestone, the app starts with the new config and new bookings get a cancellation token stored in the database, but emails are unchanged.

**New environment variables** (read in `src/backend/Program.fs`):

- `MICHAEL_PUBLIC_URL` — The base URL where Michael is accessible (e.g. `https://cal.example.com`). Required when SMTP is configured. Must not end with a trailing slash (strip it if present). Validated at startup: must start with `http://` or `https://`, must not be empty.
- `MICHAEL_HOST_EMAIL` — The host's email address, used as the iCalendar ORGANIZER and the BCC recipient. Required when SMTP is configured. Validated with the same `isValidMailboxAddress` check used for SMTP from-address.
- `MICHAEL_HOST_NAME` — The host's display name for the iCalendar ORGANIZER. Optional. Defaults to `"Host"`.

**New configuration type** in `src/backend/Email.fs`: Create a `NotificationConfig` record that bundles everything needed for sending notification emails. This replaces the bare `SmtpConfig option` that currently flows through handlers.

    type NotificationConfig =
        { Smtp: SmtpConfig
          HostEmail: string
          HostName: string
          PublicUrl: string }

In `Program.fs`, when `smtpConfig` is `Some`, also read `MICHAEL_PUBLIC_URL` and `MICHAEL_HOST_EMAIL` (failing at startup if missing). Construct `NotificationConfig option` — `None` when SMTP is not configured, `Some` when it is. Pass this through to handlers instead of `SmtpConfig option`.

**Database migration**: Create a new migration file `src/backend/migrations/{next_version}_add_cancellation_token.sql` that adds a `cancellation_token` column to the bookings table with database-level constraints. The version number should follow the existing pattern (the latest is `20260207000000`). Use a timestamp like `20260220000000`.

The migration has three statements:

    ALTER TABLE bookings ADD COLUMN cancellation_token TEXT;

    CREATE UNIQUE INDEX idx_bookings_cancellation_token
        ON bookings (cancellation_token)
        WHERE cancellation_token IS NOT NULL;

    CREATE TRIGGER trg_bookings_confirmed_requires_token
        BEFORE INSERT ON bookings
        WHEN NEW.status = 'confirmed' AND NEW.cancellation_token IS NULL
        BEGIN
            SELECT RAISE(ABORT, 'Confirmed bookings must have a cancellation_token');
        END;

The first statement adds the column. Existing bookings get NULL, which is fine — they were created before cancellation links existed and are either already cancelled or will be looked up by ID rather than by token.

The partial unique index ensures no two bookings share the same token (ignoring NULLs for historical rows). This prevents accidental token collisions from causing one cancellation link to affect the wrong booking.

The trigger enforces that any new booking inserted with status "confirmed" must have a non-NULL cancellation token. SQLite does not support `CHECK` constraints that reference other columns conditionally in `ALTER TABLE ADD COLUMN`, but a `BEFORE INSERT` trigger achieves the same invariant. This catches bugs at the database layer — if application code ever forgets to generate a token, the insert fails immediately rather than silently creating a booking with no cancellation link.

**Application-level enforcement**: In addition to the database trigger, `buildBooking` in `src/backend/Handlers.fs` should assert that the generated token is non-empty before constructing the `Booking` record, so failures surface at the F# level with a clear stack trace rather than only as a SQLite trigger error.

After adding the migration file, regenerate the Atlas checksum file by running:

    atlas migrate hash --dir "file://src/backend/migrations"

If the `atlas` CLI is not available in the dev shell, compute the checksums manually following the algorithm in `src/backend/Migrations.fs` (SHA-256 running hash, base64 encoded), or add `atlas` to the Nix flake. Check whether `atlas` is already available by running `which atlas`.

**Domain type change** in `src/backend/Domain.fs`: Add `CancellationToken: string option` to the `Booking` record. It is `option` because historical bookings in the database may have NULL.

**Database module changes** in `src/backend/Database.fs`:
- Update `readBooking` to read the `cancellation_token` column.
- Update `insertBookingInternal` to write the `cancellation_token` column.
- Update all SELECT queries that read bookings to include `cancellation_token` in the column list (there are several: `getBookingsInRange`, `getBookingById`, `getNextBooking`, the admin listing queries).

**Token generation**: In `src/backend/Handlers.fs`, in `buildBooking`, generate a random cancellation token using `System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)` and convert to hex with `Convert.ToHexString`. Store it on the `Booking` record. This function currently takes `(validated: ValidatedBookRequest) (bookingId: Guid) (now: Instant)` — no signature change is needed since the token generation is self-contained.

**Verification**: After this milestone, run `dotnet build` from `src/backend/` and `dotnet run --project tests/Michael.Tests/` from the repo root. All existing tests should pass. The app should start with the new env vars set (when SMTP is configured) and store cancellation tokens on new bookings.


### Milestone 2: .ics Calendar Attachment Generation

This milestone adds functions to generate iCalendar (.ics) content for booking confirmations and cancellations. After this milestone, the functions exist and are tested, but are not yet wired into email sending.

**New module or new section in `src/backend/Email.fs`**: Add functions that use `Ical.Net` to produce .ics content as a string. The `Ical.Net` library is already referenced in `Michael.fsproj` and used in `CalDav.fs`.

Two functions are needed:

`buildConfirmationIcs` — Generates a VCALENDAR with METHOD:REQUEST containing a single VEVENT for the booking.

    val buildConfirmationIcs: booking:Booking -> hostEmail:string -> hostName:string -> videoLink:string option -> string

The VEVENT should have:
- `UID`: `{booking.Id}@michael`
- `DTSTAMP`: `booking.CreatedAt` (as UTC)
- `DTSTART`: `booking.StartTime` converted to UTC (e.g. `20260215T190000Z`)
- `DTEND`: `booking.EndTime` converted to UTC (e.g. `20260215T200000Z`)
- `SUMMARY`: `booking.Title`
- `DESCRIPTION`: `booking.Description` if present; also append the cancellation link if you have it (but this function may not know the link — see below for how to handle this)
- `LOCATION`: the video link, if present
- `ORGANIZER`: `CN={hostName}:MAILTO:{hostEmail}`
- `ATTENDEE`: `CN={booking.ParticipantName}:MAILTO:{booking.ParticipantEmail}`
- `STATUS`: `CONFIRMED`
- `SEQUENCE`: `0`

DTSTART and DTEND use UTC (the `Z` suffix form) rather than `TZID=` references. See the Decision Log entry on iCalendar timezone interoperability for the rationale. Conversion is straightforward: call `.ToInstant()` on the NodaTime `OffsetDateTime`, then extract the UTC date-time components. In Ical.Net terms, create a `CalDateTime(utcDateTime, "UTC")` or equivalent.

Regarding the DESCRIPTION and cancellation link: the `buildConfirmationIcs` function should accept an optional `cancellationUrl: string option` parameter and append it to the DESCRIPTION if present (e.g. "To cancel this meeting, visit: {url}"). Update the signature accordingly.

`buildCancellationIcs` — Generates a VCALENDAR with METHOD:CANCEL containing a VEVENT with the same UID.

    val buildCancellationIcs: booking:Booking -> hostEmail:string -> hostName:string -> string

The VEVENT should have:
- Same `UID` as the confirmation (`{booking.Id}@michael`)
- `DTSTAMP`: current time (as UTC) — use NodaTime's `IClock` or accept an `Instant` parameter
- `DTSTART`/`DTEND`: same as original booking, in UTC
- `SUMMARY`: `Cancelled: {booking.Title}`
- `ORGANIZER`: same as confirmation
- `ATTENDEE`: same as confirmation
- `STATUS`: `CANCELLED`
- `SEQUENCE`: `1` (must be higher than the original to indicate a change)
- `METHOD`: `CANCEL` on the VCALENDAR

For generating the iCalendar text, use `Ical.Net`'s `Calendar` and `CalendarEvent` types, then serialize with `CalendarSerializer`. Example pattern (already used in `CalDav.fs` for parsing):

    open Ical.Net
    open Ical.Net.CalendarComponents
    open Ical.Net.DataTypes
    open Ical.Net.Serialization

    let cal = Calendar()
    cal.Method <- "REQUEST"
    let evt = CalendarEvent()
    evt.Uid <- $"{booking.Id}@michael"
    // ... set properties ...
    cal.Events.Add(evt)
    let serializer = CalendarSerializer()
    serializer.SerializeToString(cal)

For date-time values, convert NodaTime's `OffsetDateTime` to UTC: call `odt.ToInstant().ToDateTimeUtc()` to get a `System.DateTime` in UTC, then construct a `CalDateTime(dt, "UTC")`. This produces the `YYYYMMDDTHHMMSSZ` format that all calendar clients understand. Do not pass IANA timezone IDs to `CalDateTime` — that would produce `DTSTART;TZID=...` which requires a companion `VTIMEZONE` block for reliable interop.

**Verification**: Write unit tests (see Milestone 5) that call these functions and verify the output contains expected iCalendar properties. At minimum, verify the output parses back with `Calendar.Load()` and the VEVENT has the correct UID, METHOD, SUMMARY, ORGANIZER, ATTENDEE, DTSTART (in UTC), DTEND (in UTC), and LOCATION. Verify the serialized output contains `Z` suffixed date-times and does not contain `TZID=` or `VTIMEZONE`.


### Milestone 3: Enhanced Email Sending (BCC, Attachments, Cancellation Link)

This milestone modifies the email-sending infrastructure to support BCC recipients and .ics file attachments. It also updates the email body templates to include the cancellation link. After this milestone, the email functions accept the new parameters, but the callers (handlers) are not yet updated.

**Update `sendEmail` in `src/backend/Email.fs`**: The current signature is:

    val sendEmail: config:SmtpConfig -> toAddress:string -> toName:string -> subject:string -> body:string -> Task<Result<unit, string>>

Change it to accept optional BCC and optional .ics attachment content:

    val sendEmail: config:SmtpConfig -> toAddress:string -> toName:string -> subject:string -> body:string -> bcc:string option -> icsAttachment:string option -> Task<Result<unit, string>>

For the BCC: if `bcc` is `Some address`, add it via `message.Bcc.Add(MailboxAddress(null, address))`. MimeKit handles BCC correctly — the header is stripped before sending to the recipient, so the participant never sees the host's email.

For the .ics attachment: the MIME structure must match what calendar clients expect. Many clients (Outlook, Apple Mail, Gmail) look for a specific combination of Content-Type, charset, method parameter, Content-Disposition, and filename. The exact MIME contract for the calendar part is:

    Content-Type: text/calendar; charset=utf-8; method=REQUEST
    Content-Disposition: inline; filename="invite.ics"

For cancellation emails, `method=CANCEL` replaces `method=REQUEST`. The `method` parameter on Content-Type is critical — it tells the client to process the attachment as an invite action rather than a generic file. `inline` disposition causes clients to render the invite inline (accept/decline buttons) rather than as a downloadable attachment. The filename `invite.ics` is conventional and helps clients that fall back to extension-based detection.

To handle the method variation cleanly, the `icsAttachment` parameter is a tuple of `(icsContent, method)`:

    val sendEmail: config:SmtpConfig -> toAddress:string -> toName:string -> subject:string -> body:string -> bcc:string option -> icsAttachment:(string * string) option -> Task<Result<unit, string>>

Where `icsAttachment` is `Some (icsContent, method)` — the method is `"REQUEST"` or `"CANCEL"`.

Do not use `BodyBuilder.Attachments.Add` for the calendar part — it defaults to `application/octet-stream` with `attachment` disposition, which many clients will not auto-process. Instead, construct the MIME message manually for full control over the multipart structure. The message body should be a `multipart/mixed` containing:

1. A `text/plain; charset=utf-8` part with the email body text.
2. A `text/calendar; charset=utf-8; method={METHOD}` part with `Content-Disposition: inline; filename="invite.ics"`, containing the .ics content as UTF-8 bytes.

In MimeKit terms:

    let calendarPart = new MimePart("text", "calendar")
    calendarPart.ContentType.Parameters.Add("charset", "utf-8")
    calendarPart.ContentType.Parameters.Add("method", method) // "REQUEST" or "CANCEL"
    calendarPart.ContentDisposition <- new ContentDisposition(ContentDisposition.Inline)
    calendarPart.ContentDisposition.FileName <- "invite.ics"
    calendarPart.Content <- new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(icsContent)))

    let textPart = new TextPart("plain", Text = body)

    let multipart = new Multipart("mixed")
    multipart.Add(textPart)
    multipart.Add(calendarPart)
    message.Body <- multipart

When `icsAttachment` is `None`, fall back to the existing plain-text body (`BodyBuilder` with `TextBody` is fine).

**Extract `buildMimeMessage` for testability**: Factor the message-construction logic out of `sendEmail` into a pure function `buildMimeMessage` that takes the same parameters and returns a `MimeMessage`. The `sendEmail` function then calls `buildMimeMessage` and handles only the SMTP connection, authentication, and sending. This separation lets unit tests inspect the full MIME structure (Content-Type parameters, disposition, multipart layout, BCC header) without needing an SMTP server.

**Update email content builders**: Modify `buildConfirmationEmailContent` and `buildCancellationEmailContent` in `src/backend/Email.fs`:

For `buildConfirmationEmailContent`, add a `cancellationUrl: string option` parameter. When present, add a line to the email body:

    To cancel this meeting, visit: {cancellationUrl}

Place this before the footer separator (`---`).

For `buildCancellationEmailContent`, no changes to parameters are needed (the existing function already covers the body content). But verify the body still reads well — it currently says "If you'd like to reschedule, please book a new time." which is fine.

**Update `sendBookingConfirmationEmail` and `sendBookingCancellationEmail`**: These are convenience wrappers. Update their signatures to accept the new `NotificationConfig` (or the individual new parameters) and pass them through to `sendEmail`. They should:
- Build the .ics content by calling `buildConfirmationIcs` / `buildCancellationIcs`.
- Build the email body with the cancellation URL.
- Call `sendEmail` with the BCC and .ics attachment.

The exact signature changes will depend on how you structure `NotificationConfig`, but conceptually:

    val sendBookingConfirmationEmail: config:NotificationConfig -> booking:Booking -> videoLink:string option -> Task<Result<unit, string>>
    val sendBookingCancellationEmail: config:NotificationConfig -> booking:Booking -> cancelledByHost:bool -> videoLink:string option -> Task<Result<unit, string>>

Inside `sendBookingConfirmationEmail`:
1. Build the cancellation URL: `$"{config.PublicUrl}/cancel/{booking.Id}/{booking.CancellationToken.Value}"` (only if the token is `Some`).
2. Build the email content with `buildConfirmationEmailContent booking videoLink cancellationUrl`.
3. Build the .ics with `buildConfirmationIcs booking config.HostEmail config.HostName videoLink cancellationUrl`.
4. Call `sendEmail config.Smtp booking.ParticipantEmail booking.ParticipantName content.Subject content.Body (Some config.HostEmail) (Some (icsContent, "REQUEST"))`.

Inside `sendBookingCancellationEmail`:
1. Build the email content with `buildCancellationEmailContent booking cancelledByHost videoLink`.
2. Build the .ics with `buildCancellationIcs booking config.HostEmail config.HostName currentInstant`. (You'll need a clock or instant parameter for the DTSTAMP.)
3. Call `sendEmail config.Smtp booking.ParticipantEmail booking.ParticipantName content.Subject content.Body (Some config.HostEmail) (Some (icsContent, "CANCEL"))`.

**Verification**: The existing tests in `EmailTests.fs` will need updating since function signatures changed. Ensure they still pass. New tests for BCC and attachment behavior are covered in Milestone 5.


### Milestone 4: Wire Everything Together

This milestone connects the new configuration and email functions to the HTTP handlers and application startup. After this milestone, the full feature works end-to-end.

**`src/backend/Program.fs`**: Where `smtpConfig` is currently constructed (around line 141), also read the new env vars and build `NotificationConfig option`. Replace all uses of `smtpConfig` (passed to `handleBook` and `handleCancelBooking`) with `notificationConfig`.

**`src/backend/Handlers.fs`**: Remove `confirmBookingWithEmail` and `ConfirmBookingResult` entirely. Update `handleBook` to accept `NotificationConfig option` instead of `SmtpConfig option`. After a successful booking insertion, `handleBook` should:
1. Return the success response to the participant immediately (HTTP 200 with booking ID and `confirmed: true`).
2. Fire off the confirmation email without awaiting the result for the HTTP response. Use `task { ... } |> ignore` or `Task.Run(fun () -> ...)` to send the email asynchronously. Log success or failure, but do not alter the HTTP response or the booking status based on the email outcome.

This mirrors how `handleCancelBooking` in `AdminHandlers.fs` already works — the cancellation succeeds regardless of email delivery.

**`src/backend/AdminHandlers.fs`**: Update `handleCancelBooking` to accept `NotificationConfig option` instead of `SmtpConfig option`. Pass `notificationConfig` to `sendBookingCancellationEmail`. The cancellation handler needs a clock or instant for the .ics DTSTAMP — pass `IClock` or capture `clock.GetCurrentInstant()` at the call site.

**Verification**: Start the application with all env vars configured (including `MICHAEL_PUBLIC_URL`, `MICHAEL_HOST_EMAIL`, and SMTP settings pointing at a test mail server like Mailpit). Complete a booking through the frontend. Verify:
- The participant's email arrives with an .ics attachment.
- A separate copy of the same email is delivered to the host's address. (BCC is not visible in the participant's message headers — verify host delivery by checking that Mailpit shows a message delivered to the host address, not by looking for a BCC header on the participant's copy.)
- The .ics attachment has Content-Type `text/calendar; charset=utf-8; method=REQUEST` and Content-Disposition `inline; filename="invite.ics"`. The .ics opens in a calendar app and shows the correct event details (UTC times, video link as location, host as organizer, participant as attendee).
- The email body contains a cancellation link with the format `{PUBLIC_URL}/cancel/{booking_id}/{token}`.
- Cancel the booking from the admin dashboard. Verify the cancellation email arrives at both the participant and host addresses, with a `method=CANCEL` .ics attachment carrying the same UID as the confirmation.


### Milestone 5: Tests

This milestone adds and updates unit tests covering all new behavior.

**`tests/Michael.Tests/EmailTests.fs`**: Add tests for:
- `buildConfirmationIcs`: verify the output parses back as valid iCalendar, has METHOD:REQUEST, correct UID, SUMMARY, ORGANIZER (host email and name), ATTENDEE (participant), DTSTART/DTEND matching the booking, LOCATION set to video link when present, DESCRIPTION contains cancellation URL when provided.
- `buildCancellationIcs`: verify METHOD:CANCEL, STATUS:CANCELLED, SEQUENCE:1, correct UID, SUMMARY prefixed with "Cancelled:".
- `buildConfirmationEmailContent` with cancellation URL: verify the body contains the link.
- `buildConfirmationEmailContent` without cancellation URL: verify no cancellation line.

**`tests/Michael.Tests/HandlerTests.fs`**: Remove `confirmBookingWithEmailTests` entirely — the `confirmBookingWithEmail` function and `ConfirmBookingResult` type no longer exist. If any remaining handler tests reference these, update them. No replacement handler-level email tests are needed since email is now fire-and-forget (the email-building logic is thoroughly tested in `EmailTests.fs`).

**MIME structure tests in `tests/Michael.Tests/EmailTests.fs`**: The .ics content tests (above) verify iCalendar correctness, but the MIME assembly — how the .ics content is attached to the email — also needs testing. Add a helper function that builds a `MimeMessage` using the same logic as `sendEmail` (extract the message-construction code from `sendEmail` into a pure `buildMimeMessage` function that returns a `MimeMessage`, so it can be tested without an SMTP server). Then write tests that inspect the built `MimeMessage`:

- Verify the message has a `multipart/mixed` body with exactly two parts when an .ics attachment is present.
- Verify the first part is `text/plain; charset=utf-8` containing the email body.
- Verify the second part is `text/calendar; charset=utf-8; method=REQUEST` (for confirmations) or `text/calendar; charset=utf-8; method=CANCEL` (for cancellations). Cast to `MimePart` and check `ContentType.MimeType`, `ContentType.Parameters["charset"]`, and `ContentType.Parameters["method"]`.
- Verify the calendar part has `Content-Disposition: inline; filename="invite.ics"`. Check `ContentDisposition.Disposition` equals `"inline"` and `ContentDisposition.FileName` equals `"invite.ics"`.
- Verify the calendar part body bytes decode to valid UTF-8 containing `BEGIN:VCALENDAR`.
- Verify the `method` parameter on Content-Type matches the `METHOD:` property inside the .ics content (both `REQUEST` or both `CANCEL`). This catches mismatches where the MIME header says one thing and the iCalendar body says another.
- Verify the BCC header contains the host email when `bcc` is `Some`.
- Verify the BCC header is absent when `bcc` is `None`.
- Verify that when `icsAttachment` is `None`, the message body is a simple `text/plain` part (not `multipart/mixed`).

**`tests/Michael.Tests/DatabaseTests.fs`**: If there are existing booking insertion/retrieval tests, verify they still pass with the new `cancellation_token` column. Add a test that inserts a booking with a cancellation token and reads it back, verifying the token round-trips.

**Verification**: Run `dotnet run --project tests/Michael.Tests` from the repo root. All tests pass. Then run `selfci check` to verify CI passes.


### Milestone 6: Update EARS Spec and Documentation

Update `docs/ears/ears-booking.md` to add requirements for email notifications and remove the "Email notifications" entry from the "Deferred from specification" section.

Add a new section (e.g. "Email Notifications") with EARS requirements covering:
- Confirmation email sent on booking with .ics attachment.
- Cancellation email sent on admin cancellation with METHOD:CANCEL .ics attachment.
- Host BCC'd on all participant emails.
- Cancellation token generated and stored at booking creation.
- Cancellation link included in confirmation email body.
- Configuration requirements (MICHAEL_PUBLIC_URL, MICHAEL_HOST_EMAIL required when SMTP configured).

Also update the "Calendar invite (.ics) generation" deferred item to note it is now implemented.


## Concrete Steps

All commands are run from the repository root (`/home/bromanko.linux/Code/michael`) unless otherwise noted.

Build the backend:

    dotnet build src/backend/

Run the test suite:

    dotnet run --project tests/Michael.Tests/

Run full CI:

    selfci check

Start the app locally for manual testing (with Mailpit running on localhost:1025):

    MICHAEL_SMTP_HOST=localhost MICHAEL_SMTP_PORT=1025 MICHAEL_SMTP_FROM=cal@example.com MICHAEL_SMTP_TLS=none \
    MICHAEL_PUBLIC_URL=http://localhost:5000 MICHAEL_HOST_EMAIL=host@example.com \
    MICHAEL_HOST_TIMEZONE=America/New_York MICHAEL_DB_PATH=michael.db MICHAEL_ADMIN_PASSWORD=test \
    MICHAEL_CSRF_SIGNING_KEY=a]very-long-secret-key-at-least-32-chars \
    dotnet run --project src/backend/

Check the Atlas migration hash (if `atlas` is available):

    atlas migrate hash --dir "file://src/backend/migrations"


## Validation and Acceptance

The feature is complete when:

1. **Automated tests pass**: `dotnet run --project tests/Michael.Tests/` reports all tests passing, including new tests for .ics generation, email content with cancellation links, BCC behavior, and cancellation token round-tripping.

2. **CI passes**: `selfci check` succeeds.

3. **Manual end-to-end test** (with Mailpit or similar local SMTP trap):
   - Complete a booking via the frontend.
   - In Mailpit, verify the confirmation email has: (a) an `.ics` attachment that opens in a calendar app with correct event details, video link as location, host as organizer, participant as attendee; (b) a cancellation link in the body matching `{PUBLIC_URL}/cancel/{id}/{token}`; (c) a BCC to the host email address.
   - Cancel the booking from the admin dashboard.
   - In Mailpit, verify the cancellation email has: (a) a METHOD:CANCEL `.ics` attachment with the same UID; (b) BCC to the host.

4. **Startup validation**: Starting the app with SMTP configured but `MICHAEL_PUBLIC_URL` or `MICHAEL_HOST_EMAIL` missing causes a clear crash-at-startup error message.

5. **Backward compatibility**: Starting without SMTP configured still works — no emails sent, no .ics generated, booking flow unchanged.


## Idempotence and Recovery

The database migration (`ALTER TABLE ... ADD COLUMN`) is idempotent in practice — SQLite will error if the column already exists, but the migration runner skips already-applied migrations. If a migration fails partway, delete the row from `atlas_schema_revisions` for that version and re-run.

All code changes are additive. The existing email-sending behavior (plain text, no BCC, no .ics) is replaced, not supplemented, so there is no drift from running the changes multiple times.

If the Atlas checksum file gets out of sync, regenerate it with `atlas migrate hash` or by following the checksum algorithm in `src/backend/Migrations.fs`.


## Artifacts and Notes

Current environment variables for reference (from `src/backend/Program.fs`):

    MICHAEL_DB_PATH
    MICHAEL_HOST_TIMEZONE (required)
    MICHAEL_ADMIN_PASSWORD (required)
    MICHAEL_SMTP_HOST, MICHAEL_SMTP_PORT, MICHAEL_SMTP_FROM (required for email)
    MICHAEL_SMTP_USERNAME, MICHAEL_SMTP_PASSWORD (optional)
    MICHAEL_SMTP_TLS (optional, default "starttls")
    MICHAEL_SMTP_FROM_NAME (optional, default "Michael")
    MICHAEL_CSRF_SIGNING_KEY (required)
    MICHAEL_CALDAV_FASTMAIL_URL/USERNAME/PASSWORD (optional)
    MICHAEL_CALDAV_ICLOUD_URL/USERNAME/PASSWORD (optional)

New variables added by this plan:

    MICHAEL_PUBLIC_URL (required when SMTP configured)
    MICHAEL_HOST_EMAIL (required when SMTP configured)
    MICHAEL_HOST_NAME (optional, default "Host")

Example .ics output for a confirmation (for reference when writing tests):

    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//Michael//Michael//EN
    METHOD:REQUEST
    BEGIN:VEVENT
    UID:a1b2c3d4-e5f6-7890-abcd-ef1234567890@michael
    DTSTAMP:20260215T190000Z
    DTSTART:20260215T190000Z
    DTEND:20260215T200000Z
    SUMMARY:Project Review
    DESCRIPTION:Quarterly review meeting\nTo cancel: https://cal.example.com/cancel/a1b2c3d4/abc123token
    LOCATION:https://zoom.us/j/123456
    ORGANIZER;CN=Brian:MAILTO:brian@example.com
    ATTENDEE;CN=Alice Smith:MAILTO:alice@example.com
    STATUS:CONFIRMED
    SEQUENCE:0
    END:VEVENT
    END:VCALENDAR

Note the UTC times: the booking is 2:00–3:00 PM Eastern (UTC-5), which becomes 19:00–20:00 UTC. Calendar apps display these in the viewer's local timezone. The email body still shows "14:00 - 15:00 (America/New_York)" for human readability.


## Interfaces and Dependencies

**NuGet packages** (already present in `src/backend/Michael.fsproj` — no additions needed):
- `MailKit` 4.14.1 — SMTP client, MIME message construction
- `Ical.Net` 5.2.0 — iCalendar generation and serialization

In `src/backend/Email.fs`, the key types and functions after this plan:

    type NotificationConfig =
        { Smtp: SmtpConfig
          HostEmail: string
          HostName: string
          PublicUrl: string }

    val buildConfirmationIcs:
        booking:Booking -> hostEmail:string -> hostName:string -> videoLink:string option -> cancellationUrl:string option -> string

    val buildCancellationIcs:
        booking:Booking -> hostEmail:string -> hostName:string -> cancelledAt:Instant -> string

    val sendEmail:
        config:SmtpConfig -> toAddress:string -> toName:string -> subject:string -> body:string
        -> bcc:string option -> icsAttachment:(string * string) option -> Task<Result<unit, string>>

    val buildConfirmationEmailContent:
        booking:Booking -> videoLink:string option -> cancellationUrl:string option -> BookingEmailContent

    val sendBookingConfirmationEmail:
        config:NotificationConfig -> booking:Booking -> videoLink:string option -> Task<Result<unit, string>>

    val sendBookingCancellationEmail:
        config:NotificationConfig -> booking:Booking -> cancelledByHost:bool -> videoLink:string option -> cancelledAt:Instant -> Task<Result<unit, string>>

In `src/backend/Domain.fs`, the updated `Booking` type:

    type Booking =
        { Id: Guid
          ParticipantName: string
          ParticipantEmail: string
          ParticipantPhone: string option
          Title: string
          Description: string option
          StartTime: OffsetDateTime
          EndTime: OffsetDateTime
          DurationMinutes: int
          Timezone: string
          Status: BookingStatus
          CreatedAt: Instant
          CancellationToken: string option }

In `src/backend/Email.fs`, new testable message builder (extracted from `sendEmail`):

    val buildMimeMessage:
        config:SmtpConfig -> toAddress:string -> toName:string -> subject:string -> body:string
        -> bcc:string option -> icsAttachment:(string * string) option -> MimeMessage

In `src/backend/Handlers.fs`: The `confirmBookingWithEmail` function and `ConfirmBookingResult` type are removed. The `handleBook` handler sends confirmation email fire-and-forget after a successful booking insertion. No new public functions are added — the email-sending call is internal to `handleBook`.
