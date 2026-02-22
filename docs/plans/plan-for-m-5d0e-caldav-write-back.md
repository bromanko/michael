# CalDAV Write-Back: Create Calendar Event on Booking

This ExecPlan is a living document. The sections Progress, Surprises & Discoveries,
Decision Log, and Outcomes & Retrospective must be kept up to date as work proceeds.


## Purpose / Big Picture

After this change, when a participant books a meeting through Michael, the booking is automatically created as a VEVENT on the host's CalDAV calendar (Fastmail or iCloud). The host sees the booking alongside their other calendar events — not just in the admin dashboard or via the BCC'd `.ics` email attachment.

When a booking is cancelled (by the host from the admin dashboard), the corresponding CalDAV event is deleted from the calendar.

Currently CalDAV integration is read-only: `CalDav.fs` fetches events from external calendars and `CalendarSync.fs` caches them locally for conflict checking. This feature adds the write direction — HTTP PUT to create a VEVENT resource and HTTP DELETE to remove it.

### Prerequisite Spike

Before implementing this plan, a spike is needed to confirm the CalDAV PUT/DELETE behavior on Fastmail (and optionally iCloud). Specifically:

1. PUT a VCALENDAR with no `METHOD` property to a resource URL — does the server accept it and create the event?
2. Does Fastmail return a `Location` header on 201 Created, or does the resource live at the request URL?
3. DELETE the resource — does it return 204? What happens on a second DELETE (404)?

Create ticket and complete the spike before starting implementation.


## Progress

- [ ] Milestone 0: Spike — Validate CalDAV PUT/DELETE on Fastmail
- [ ] Milestone 1: Database schema and domain type changes
- [ ] Milestone 2: CalDAV PUT and DELETE functions in CalDav.fs
- [ ] Milestone 3: ICS generation for CalDAV storage
- [ ] Milestone 4: Write-back orchestration in CalendarSync.fs
- [ ] Milestone 5: Wire into booking and cancellation handlers
- [ ] Milestone 6: Admin dashboard — surface write-back status
- [ ] Milestone 7: Tests
- [ ] Milestone 8: Configuration and Program.fs wiring


## Surprises & Discoveries

(To be filled during implementation.)


## Decision Log

- Decision: Use a single env var `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL` pointing to the full calendar collection URL, rather than two env vars (source + calendar name).
  Rationale: The calendar collection URL is unambiguous. A display-name-based approach would be fragile (names can change, aren't unique). Credentials are resolved by matching the URL prefix against existing CalDAV source configs — no new credential env vars needed. The env var is required at startup (fail-fast); there is no "write-back disabled" mode.
  Date: 2026-02-22

- Decision: Use the booking's `Id` (a GUID) as the iCalendar UID, formatted as `{booking-id}@michael`. The resource URL is `{calendarCollectionUrl}{booking-id}.ics`.
  Rationale: Same UID format already used in `Email.buildConfirmationIcs` for the `.ics` email attachment. Stable across create and delete. The `@michael` suffix follows iCalendar convention.
  Date: 2026-02-22

- Decision: Create a dedicated `buildCalDavEventIcs` function rather than reusing `Email.buildConfirmationIcs`.
  Rationale: The email `.ics` uses `METHOD:REQUEST` with `ORGANIZER`/`ATTENDEE` (iTIP invitation semantics). CalDAV stored events should omit `METHOD` entirely — they are personal calendar entries, not invitations. The SUMMARY should include the participant name (e.g., "Meeting with Alice Smith") since the host is viewing their own calendar. DESCRIPTION should include participant contact info. These are different enough from the email `.ics` to warrant a separate function.
  Date: 2026-02-22

- Decision: Store the CalDAV resource href on the booking record (`caldav_event_href TEXT` column) after a successful PUT.
  Rationale: The DELETE on cancellation needs the exact resource URL. Some CalDAV servers may normalize or relocate the resource (returning a different URL in the `Location` response header). Storing the actual href ensures reliable deletion.
  Date: 2026-02-22

- Decision: Use `CalDavEventHref` (not `CaldavEventUrl`) for the F# field name.
  Rationale: The codebase consistently uses `CalDav` with a capital D (`CalDavProvider`, `CalDavSourceConfig`). "Href" is the WebDAV/CalDAV term for resource identifiers (from the `DAV:href` XML element).
  Date: 2026-02-22

- Decision: Fire-and-forget for both write-back and deletion, same pattern as email notifications.
  Rationale: The booking's confirmed/cancelled status in SQLite is the source of truth. A CalDAV failure should not block the booking flow. If the initial PUT fails, the next background sync cycle will not contain the event — but the host can see the booking in the admin dashboard. A failed write-back is surfaced in the admin UI.
  Date: 2026-02-22

- Decision: `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL` is required at startup. There is no "write-back disabled" mode.
  Rationale: This is a personal scheduling tool where the host always wants bookings on their calendar. Failing fast if the env var is missing catches misconfiguration immediately.
  Date: 2026-02-22


## Outcomes & Retrospective

(To be filled after implementation.)


## Context and Orientation

Michael is a self-hosted scheduling tool. The backend is F# with Falco, using SQLite for storage. The frontend is Elm.

Here is how the relevant files fit together for this feature:

- `src/backend/CalDav.fs` — Module `Michael.CalDav`. CalDAV protocol operations: HTTP helpers (`sendWebDAV`), discovery (`discoverPrincipal`, `discoverCalendarHome`, `listCalendars`), event fetching (`fetchRawEvents`), and ICS parsing (`parseAndExpandEvents`). Uses `Ical.Net` for parsing. This is where `putEvent` and `deleteEvent` will be added.
- `src/backend/CalendarSync.fs` — Module `Michael.CalendarSync`. Orchestrates background sync: `syncAllSources` (polls all CalDAV sources), `getCachedBlockers` (reads cached events), `startBackgroundSync` (10-minute timer). Write-back orchestration (`writeBackBookingEvent`, `deleteWriteBackEvent`) will be added here.
- `src/backend/Email.fs` — Module `Michael.Email`. Contains `buildConfirmationIcs` and `buildCancellationIcs` which generate `.ics` content for email attachments using `Ical.Net`. The CalDAV `.ics` generation will be a new separate function (different properties needed).
- `src/backend/Domain.fs` — Module `Michael.Domain`. Contains `Booking`, `CalDavSourceConfig`, `CalendarSource`, and other domain types. `CalDavWriteBackConfig` will be added here.
- `src/backend/Database.fs` — Module `Michael.Database`. Contains `readBooking`, `insertBookingInternal`, `insertBookingIfSlotAvailable`, `cancelBooking`, and all booking queries. Four SELECT queries explicitly list booking columns and all use `readBooking` as the row reader. Both the column lists and `readBooking` need updating.
- `src/backend/Handlers.fs` — Module `Michael.Handlers`. Contains `handleBook` which fires email confirmation as fire-and-forget. CalDAV write-back will follow the same pattern with an injectable `writeBackFn`.
- `src/backend/AdminHandlers.fs` — Module `Michael.AdminHandlers`. Contains `handleCancelBooking` which fires cancellation email as fire-and-forget. CalDAV deletion will follow the same pattern.
- `src/backend/Program.fs` — Application entry point. Reads env vars, builds configs, wires routes. Currently builds `calDavSources` list from `MICHAEL_CALDAV_FASTMAIL_*` and `MICHAEL_CALDAV_ICLOUD_*` env vars. Write-back config will be built here by matching the new URL env var against the existing source list.
- `src/backend/schema.sql` — Declarative schema (Atlas source of truth). Needs the new column.
- `src/backend/migrations/` — Atlas-managed migrations. Latest is `20260220000000_add_cancellation_token.sql`.
- `src/fake-caldav/Handlers.fs` — Fake CalDAV server for development/testing. Currently handles PROPFIND and REPORT. PUT and DELETE handlers will be added.

Key conventions (from AGENTS.md):
- Use `task { }` for all async I/O.
- Inject configuration — don't read env vars inside library modules.
- Use `Result` for errors, not exceptions.
- Fail fast on missing configuration at startup.
- F# field naming: `CalDav` with capital D (matching `CalDavProvider`, `CalDavSourceConfig`).


## Plan of Work


### Milestone 0: Spike — Validate CalDAV PUT/DELETE on Fastmail

Before writing any production code, validate the CalDAV PUT and DELETE behavior with a quick F# script (similar to the existing `spike/caldav_spike.fsx`).

**Questions to answer:**
1. Does Fastmail accept a PUT to `{calendarCollectionUrl}/{uid}.ics` with a VCALENDAR body containing no `METHOD` property?
2. Does it return 201 Created? Does it include a `Location` header?
3. Does a subsequent GET or REPORT return the created event?
4. Does DELETE on the resource URL return 204? What does a second DELETE return?
5. Does the background sync pick up the created event in the next cycle?

**Output:** Update `docs/spike-caldav-writeback.md` with findings. If behavior differs from expectations, update this plan before proceeding.


### Milestone 1: Database Schema and Domain Type Changes

**Migration file:** `src/backend/migrations/20260223000000_add_caldav_event_href.sql`

```sql
ALTER TABLE bookings ADD COLUMN caldav_event_href TEXT;
```

**Schema update:** Add the column to `src/backend/schema.sql`:

```sql
CREATE TABLE bookings (
    ...existing columns...
    caldav_event_href TEXT
);
```

After adding the migration file, regenerate the Atlas checksum:

```
atlas migrate hash --dir "file://src/backend/migrations"
```

If `atlas` is not available, compute manually following `src/backend/Migrations.fs`.

**Domain type change** in `src/backend/Domain.fs`:

Add `CalDavEventHref: string option` to the `Booking` record (after `CancellationToken`).

Add write-back configuration type in the CalDAV types section:

```fsharp
type CalDavWriteBackConfig =
    { SourceConfig: CalDavSourceConfig
      CalendarUrl: string }
```

**Database module changes** in `src/backend/Database.fs`:

1. Update `readBooking` to read the `caldav_event_href` column:
   ```fsharp
   CalDavEventHref = rd.ReadStringOption "caldav_event_href"
   ```

2. Update `insertBookingInternal` to write `caldav_event_href` (always NULL on initial insert — it's set after the CalDAV PUT succeeds).

3. Update all four SELECT query strings that list booking columns to include `caldav_event_href`:
   - `getBookingsInRange` (line ~174)
   - `listBookings` (line ~498)
   - `getBookingById` (line ~518)
   - `getNextBooking` (line ~555)

4. Add `updateBookingCalDavEventHref`:
   ```fsharp
   let updateBookingCalDavEventHref (conn: SqliteConnection) (bookingId: Guid) (href: string) : Result<unit, string>
   ```
   This UPDATE sets `caldav_event_href` for a specific booking.

**Test updates:** Every test file that constructs a `Booking` record needs the new field. There are ~25 occurrences across:
- `tests/Michael.Tests/CalDavTests.fs`
- `tests/Michael.Tests/CalendarViewTests.fs`
- `tests/Michael.Tests/DatabaseTests.fs`
- `tests/Michael.Tests/EmailTests.fs`
- `tests/Michael.Tests/HandlerTests.fs`
- `tests/Michael.Tests/AvailabilityTests.fs`
- `tests/Michael.Tests/PropertyTests.fs`
- `tests/Michael.Tests/AdminTests.fs`

Add `CalDavEventHref = None` to all existing `Booking` record constructions.

**Verification:** `dotnet build` succeeds. All existing tests pass with the new field defaulting to `None`.


### Milestone 2: CalDAV PUT and DELETE Functions

**File:** `src/backend/CalDav.fs`

Add two new functions after the existing `fetchRawEvents` section:

**`putEvent`** — HTTP PUT a VCALENDAR to a CalDAV resource URL.

```fsharp
let putEvent (client: HttpClient) (resourceUrl: string) (icsContent: string)
    : Task<Result<string, string>>
```

Implementation:
- Create an `HttpRequestMessage` with method `PUT` and the resource URL.
- Set `Content-Type: text/calendar; charset=utf-8`.
- Do NOT set `If-None-Match: *` (the UID is unique per booking, and omitting this header simplifies retries — if the event already exists from a previous attempt, the PUT updates it).
- Send the request.
- On 2xx (201 Created or 204 No Content): check for `Location` response header. If present, return `Ok locationHeader`. Otherwise return `Ok resourceUrl`.
- On 4xx or 5xx: return `Error` with status code and response body.

**`deleteEvent`** — HTTP DELETE a CalDAV resource by URL.

```fsharp
let deleteEvent (client: HttpClient) (resourceUrl: string)
    : Task<Result<unit, string>>
```

Implementation:
- Create an `HttpRequestMessage` with method `DELETE`.
- On 2xx (200, 204): return `Ok ()`.
- On 404: return `Ok ()` (idempotent — already deleted).
- On other 4xx/5xx: return `Error` with status code and response body.

**Verification:** Unit tests (see Milestone 7).


### Milestone 3: ICS Generation for CalDAV Storage

**File:** `src/backend/CalDav.fs` — add a new section "ICS Generation for Write-Back" after the parsing section.

**`buildCalDavEventIcs`** — Generate a VCALENDAR for storing on the host's personal calendar.

```fsharp
let buildCalDavEventIcs
    (booking: Booking)
    (hostEmail: string)
    (videoLink: string option)
    : string
```

This function produces a VCALENDAR with a single VEVENT. Key differences from `Email.buildConfirmationIcs`:

- **No METHOD** on the VCALENDAR (this is a stored resource, not an iTIP message).
- **SUMMARY:** `"Meeting with {participantName}: {title}"` — the host is viewing their own calendar, so the participant's name is most useful.
- **DESCRIPTION:** Include participant contact info: name, email, phone (if provided), and the booking description (if provided).
- **LOCATION:** Video link if configured.
- **No ORGANIZER/ATTENDEE** — this is a personal calendar entry, not an invitation.
- **UID:** `{booking.Id}@michael` (same as email `.ics`).
- **DTSTART/DTEND:** UTC format (`YYYYMMDDTHHMMSSZ`), same approach as email `.ics`.
- **DTSTAMP:** `booking.CreatedAt` in UTC.
- **STATUS:** `CONFIRMED`.
- **SEQUENCE:** `0`.
- **PRODID:** `-//Michael//Michael//EN`.

Use `Ical.Net` for generation (same as `Email.fs`):

```fsharp
let cal = Calendar()
cal.AddProperty("PRODID", "-//Michael//Michael//EN")
// No cal.Method — intentionally omitted
let evt = CalendarEvent()
// ... set properties ...
cal.Events.Add(evt)
let serializer = CalendarSerializer()
serializer.SerializeToString(cal)
```

All user-supplied strings must be passed through `Sanitize.stripControlChars` before assignment to iCal properties (defence in depth, same as `Email.buildConfirmationIcs`).

**Verification:** Unit tests (see Milestone 7).


### Milestone 4: Write-Back Orchestration

**File:** `src/backend/CalendarSync.fs`

Add two new functions:

**`writeBackBookingEvent`** — Full pipeline: generate ICS → PUT to CalDAV → store href in DB.

```fsharp
let writeBackBookingEvent
    (createConn: unit -> SqliteConnection)
    (client: HttpClient)
    (writeConfig: CalDavWriteBackConfig)
    (booking: Booking)
    (hostEmail: string)
    (videoLink: string option)
    : Task<unit>
```

Implementation:
1. Build the resource URL: `{writeConfig.CalendarUrl.TrimEnd('/')}/{booking.Id}.ics`
2. Generate ICS content via `CalDav.buildCalDavEventIcs booking hostEmail videoLink`.
3. Call `CalDav.putEvent client resourceUrl icsContent`.
4. On `Ok href`: call `Database.updateBookingCalDavEventHref conn booking.Id href` to persist the href.
5. On `Error msg`: log a warning. Do not propagate — the booking is confirmed regardless.
6. Catch all exceptions and log them (same defensive pattern as `sendConfirmationNotification` in `Handlers.fs`). The returned Task must never fault since the caller fires-and-forgets it.

**`deleteWriteBackEvent`** — Delete a CalDAV event for a cancelled booking.

```fsharp
let deleteWriteBackEvent
    (client: HttpClient)
    (booking: Booking)
    : Task<unit>
```

Implementation:
1. If `booking.CalDavEventHref` is `None`: log debug and return (nothing to delete — the PUT may have failed).
2. Call `CalDav.deleteEvent client href`.
3. On `Ok ()`: log info.
4. On `Error msg`: log warning.
5. Catch all exceptions and log them. The returned Task must never fault.

**Verification:** Unit tests (see Milestone 7).


### Milestone 5: Wire Into Handlers

**File:** `src/backend/Handlers.fs`

Update `handleBook` to accept an additional injectable write-back function:

```fsharp
let handleBook
    (createConn: unit -> SqliteConnection)
    (hostTz: DateTimeZone)
    (clock: IClock)
    (notificationConfig: NotificationConfig option)
    (videoLink: unit -> string option)
    (sendFn: NotificationConfig -> Booking -> string option -> Task<Result<unit, string>>)
    (writeBackFn: Booking -> string option -> Task<unit>)
    : HttpHandler
```

After the booking is confirmed (after `insertBookingIfSlotAvailable` returns `Ok true`), add fire-and-forget write-back alongside the existing email fire-and-forget:

```fsharp
// Send confirmation email fire-and-forget.
sendConfirmationNotification sendFn notificationConfig booking (videoLink ())
|> ignore

// Write-back to CalDAV calendar fire-and-forget.
writeBackFn booking (videoLink ()) |> ignore
```

**File:** `src/backend/AdminHandlers.fs`

Update `handleCancelBooking` to accept an additional injectable delete function:

```fsharp
let handleCancelBooking
    (createConn: unit -> SqliteConnection)
    (clock: IClock)
    (notificationConfig: NotificationConfig option)
    (sendFn: NotificationConfig -> Booking -> bool -> Instant -> Task<Result<unit, string>>)
    (deleteCalDavFn: Booking -> Task<unit>)
    : HttpHandler
```

After successful cancellation, alongside the existing email, fire-and-forget the CalDAV deletion:

```fsharp
// Delete CalDAV event fire-and-forget
match bookingOpt with
| Some booking -> deleteCalDavFn booking |> ignore
| None -> ()
```

**Verification:** Build succeeds. Handler tests updated (see Milestone 7).


### Milestone 6: Admin Dashboard — Surface Write-Back Status

Add a `CalDavEventHref` field to the booking detail API response so the admin dashboard can show whether the CalDAV event was created.

**File:** `src/backend/AdminHandlers.fs`

Update `BookingDto` to include:

```fsharp
CalDavEventHref: string option
```

Update `bookingToDto` to map the field.

**File:** `src/frontend/admin/`

In the booking detail view, show an indicator:
- If `CalDavEventHref` is `Some url`: show "📅 On calendar" (or similar).
- If `CalDavEventHref` is `None`: show "⚠️ Not on calendar" warning.

This gives the host visibility into write-back failures without needing to check logs.

**Note:** The Elm frontend changes are minimal — just reading one new optional field from the booking detail JSON and conditionally rendering an indicator. Update the booking decoder in the admin API module and add a line to the booking detail view.

**Verification:** Start the app, create a booking, verify the admin dashboard shows the calendar status indicator.


### Milestone 7: Tests

**`tests/Michael.Tests/CalDavTests.fs`** — New tests for PUT/DELETE:

- `putEvent` returns `Ok` with resource URL on 201 response
- `putEvent` returns `Ok` with Location header value when server provides one
- `putEvent` returns `Error` on 403 response
- `putEvent` returns `Error` on 500 response
- `putEvent` sets correct `Content-Type: text/calendar` header
- `deleteEvent` returns `Ok` on 204 response
- `deleteEvent` returns `Ok` on 404 response (idempotent)
- `deleteEvent` returns `Error` on 500 response

**`tests/Michael.Tests/CalDavTests.fs`** — New tests for ICS generation:

- `buildCalDavEventIcs` output parses back with `Calendar.Load()`
- Output has no `METHOD` property
- Output has correct UID (`{bookingId}@michael`)
- SUMMARY includes participant name and title
- DESCRIPTION includes participant email
- DESCRIPTION includes participant phone when present
- DESCRIPTION omits phone line when not present
- LOCATION set to video link when present
- LOCATION absent when no video link
- DTSTART/DTEND in UTC (contain `Z`, no `TZID`)
- No `ORGANIZER` or `ATTENDEE` properties
- User-supplied strings are sanitized (control chars stripped)

**`tests/Michael.Tests/CalendarSyncTests.fs`** — Write-back orchestration:

- `writeBackBookingEvent` calls `putEvent` with correct URL construction
- `writeBackBookingEvent` stores href in DB on success (round-trip via `getBookingById`)
- `writeBackBookingEvent` does not update DB on PUT failure
- `writeBackBookingEvent` does not throw on PUT failure (logs only)
- `deleteWriteBackEvent` calls `deleteEvent` with stored href
- `deleteWriteBackEvent` is a no-op when `CalDavEventHref` is `None`
- `deleteWriteBackEvent` does not throw on DELETE failure (logs only)

**`tests/Michael.Tests/DatabaseTests.fs`** — Database round-trip:

- Insert booking with `CalDavEventHref = None`, read back, verify None
- `updateBookingCalDavEventHref` sets the column, read back, verify value
- Existing booking tests pass with new column

**`tests/Michael.Tests/HandlerTests.fs`** — Handler integration:

- `handleBook` calls `writeBackFn` after successful booking
- `handleBook` still returns 200 even when `writeBackFn` throws
- Write-back receives correct booking and videoLink arguments

**`tests/Michael.Tests/AdminTests.fs`** — Cancellation integration:

- `handleCancelBooking` calls `deleteCalDavFn` with the booking
- `handleCancelBooking` still returns success even when `deleteCalDavFn` throws

**Verification:** `dotnet run --project tests/Michael.Tests` — all tests pass.


### Milestone 8: Configuration and Program.fs Wiring

**File:** `src/backend/Program.fs`

After building `calDavSources`, read the write-back calendar URL and resolve credentials:

```fsharp
let calDavWriteBackConfig =
    let calendarUrl =
        Environment.GetEnvironmentVariable("MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL")
        |> Option.ofObj
        |> Option.defaultWith (fun () ->
            failwith "MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL environment variable is required.")

    // Find the CalDAV source whose base URL is a prefix of the calendar URL
    let matchingSource =
        calDavSources
        |> List.tryFind (fun s ->
            calendarUrl.StartsWith(s.Source.BaseUrl.TrimEnd('/'))
            || calendarUrl.Contains(s.Source.BaseUrl.TrimEnd('/').Split("//").[1]))

    match matchingSource with
    | None ->
        failwith
            $"MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL ({calendarUrl}) does not match any configured CalDAV source. Configure the matching MICHAEL_CALDAV_FASTMAIL_* or MICHAEL_CALDAV_ICLOUD_* env vars."
    | Some sourceConfig ->
        Log.Information(
            "CalDAV write-back configured: {Provider} → {CalendarUrl}",
            sourceConfig.Source.Provider,
            calendarUrl
        )
        { SourceConfig = sourceConfig
          CalendarUrl = calendarUrl }
```

Create an `HttpClient` for write-back (reuses existing `createHttpClient` with the source's credentials):

```fsharp
let writeBackClient =
    createHttpClient httpClientFactory calDavWriteBackConfig.SourceConfig.Username calDavWriteBackConfig.SourceConfig.Password
```

Build the write-back and delete functions to inject into handlers:

```fsharp
let writeBackFn (booking: Booking) (videoLink: string option) =
    CalendarSync.writeBackBookingEvent
        createConn writeBackClient calDavWriteBackConfig booking
        notificationConfig.Value.HostEmail videoLink

let deleteCalDavFn (booking: Booking) =
    CalendarSync.deleteWriteBackEvent writeBackClient booking
```

Wire into the route handlers — update the `handleBook` call to pass `writeBackFn` and the `handleCancelBooking` call to pass `deleteCalDavFn`.

**Dispose the write-back client** on app shutdown (alongside `syncDisposable`).

**Verification:**
1. `dotnet build` succeeds.
2. All tests pass.
3. `selfci check` passes.
4. Manual test: set all env vars (CalDAV source + write-back URL), create a booking, verify the event appears on the calendar. Cancel the booking, verify the event is removed.


## Concrete Steps

All commands are run from the repository root.

Build the backend:

    dotnet build src/backend/

Run the test suite:

    dotnet run --project tests/Michael.Tests/

Run full CI:

    selfci check

Start the app locally with write-back configured:

    MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL=https://caldav.fastmail.com/dav/calendars/user/brian@fastmail.com/Default/ \
    MICHAEL_CALDAV_FASTMAIL_URL=https://caldav.fastmail.com/dav/calendars \
    MICHAEL_CALDAV_FASTMAIL_USERNAME=brian@fastmail.com \
    MICHAEL_CALDAV_FASTMAIL_PASSWORD=app-password-here \
    ... (other required env vars) ...
    dotnet run --project src/backend/

Check the Atlas migration hash:

    atlas migrate hash --dir "file://src/backend/migrations"


## Validation and Acceptance

The feature is complete when:

1. **Spike completed:** CalDAV PUT/DELETE behavior validated on Fastmail.

2. **Automated tests pass:** All new and existing tests pass.

3. **CI passes:** `selfci check` succeeds.

4. **Manual end-to-end test:**
   - Complete a booking via the frontend.
   - Verify the booking appears as an event on the host's Fastmail calendar within seconds.
   - Verify the event has the correct summary (includes participant name), time, and description (includes participant contact info).
   - Verify the admin dashboard shows "On calendar" for the booking.
   - Cancel the booking from the admin dashboard.
   - Verify the event is removed from the Fastmail calendar.

5. **Startup validation:** Starting without `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL` set fails immediately with a clear error.

6. **Sync interaction:** After a write-back, the next sync cycle fetches the event back as a `cached_event`. Slot computation still works correctly (no double-blocking or lost availability).


## Files Changed

| File | Change |
|---|---|
| `src/backend/schema.sql` | Add `caldav_event_href TEXT` column to `bookings` table |
| `src/backend/migrations/20260223000000_add_caldav_event_href.sql` | New migration |
| `src/backend/Domain.fs` | Add `CalDavEventHref: string option` to `Booking`; add `CalDavWriteBackConfig` type |
| `src/backend/Database.fs` | Update `readBooking`, `insertBookingInternal`, all 4 booking SELECT queries; add `updateBookingCalDavEventHref` |
| `src/backend/CalDav.fs` | Add `putEvent`, `deleteEvent`, `buildCalDavEventIcs` |
| `src/backend/CalendarSync.fs` | Add `writeBackBookingEvent`, `deleteWriteBackEvent` |
| `src/backend/Handlers.fs` | Add `writeBackFn` parameter to `handleBook`; fire-and-forget write-back |
| `src/backend/AdminHandlers.fs` | Add `deleteCalDavFn` parameter to `handleCancelBooking`; fire-and-forget deletion; update `BookingDto` |
| `src/backend/Program.fs` | Read `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL`; build `CalDavWriteBackConfig`; wire into handlers |
| `src/fake-caldav/Handlers.fs` | Add PUT and DELETE handlers |
| `src/frontend/admin/` | Show CalDAV status indicator on booking detail |
| `tests/Michael.Tests/CalDavTests.fs` | Tests for `putEvent`, `deleteEvent`, `buildCalDavEventIcs`; update existing `Booking` constructions |
| `tests/Michael.Tests/CalendarSyncTests.fs` | Tests for write-back orchestration |
| `tests/Michael.Tests/HandlerTests.fs` | Tests for write-back in booking handler; update existing `Booking` constructions |
| `tests/Michael.Tests/AdminTests.fs` | Tests for deletion in cancellation handler; update existing `Booking` constructions |
| `tests/Michael.Tests/DatabaseTests.fs` | Tests for `caldav_event_href` column; update existing `Booking` constructions |
| `tests/Michael.Tests/CalendarViewTests.fs` | Update existing `Booking` constructions |
| `tests/Michael.Tests/EmailTests.fs` | Update existing `Booking` constructions |
| `tests/Michael.Tests/AvailabilityTests.fs` | Update existing `Booking` constructions |
| `tests/Michael.Tests/PropertyTests.fs` | Update existing `Booking` constructions |


## Environment Variables

| Variable | Required | Purpose |
|---|---|---|
| `MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL` | **Yes** | Full URL to the CalDAV calendar collection for write-back (e.g., `https://caldav.fastmail.com/dav/calendars/user/alice@fastmail.com/Default/`). Credentials resolved from matching CalDAV source config. |

All existing CalDAV env vars (`MICHAEL_CALDAV_FASTMAIL_*`, `MICHAEL_CALDAV_ICLOUD_*`) remain unchanged and are still needed for the matching source's credentials.


## Edge Cases

1. **CalDAV server unreachable during write-back** — Log warning. Booking confirmed in SQLite. `caldav_event_href` is NULL. Admin dashboard shows "Not on calendar" warning. The sync service does not retry write-backs — it only reads; but if the host manually creates the event or the next booking attempt succeeds, it will appear.

2. **CalDAV server rejects the PUT (403, 409, etc.)** — Same as above: log and move on.

3. **Cancellation when `caldav_event_href` is NULL** — Skip DELETE silently. This happens when the initial PUT failed.

4. **Cancellation DELETE returns 404** — Treat as success (idempotent). Event may have been manually deleted by the host.

5. **Race between write-back and sync** — The sync cycle replaces all cached events for a source. A written-back event will be fetched as a `cached_event`. This is harmless — `computeSlots` subtracts both bookings and calendar blockers independently, and subtracting the same time range twice produces the same result.

6. **Multiple CalDAV sources but one write target** — Write-back targets only the configured calendar. Read sync continues across all sources.

7. **Write-back credentials become invalid** — PUT/DELETE fail with auth error. Logged as warning. Booking flow unaffected.


## Interfaces and Dependencies

**NuGet packages** (already present — no additions needed):
- `Ical.Net` 5.2.0 — iCalendar generation
- No new dependencies

In `src/backend/Domain.fs`, the updated `Booking` type:

```fsharp
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
      CancellationToken: string option
      CalDavEventHref: string option }
```

In `src/backend/Domain.fs`, the new config type:

```fsharp
type CalDavWriteBackConfig =
    { SourceConfig: CalDavSourceConfig
      CalendarUrl: string }
```

In `src/backend/CalDav.fs`, new functions:

```fsharp
val putEvent: client:HttpClient -> resourceUrl:string -> icsContent:string -> Task<Result<string, string>>
val deleteEvent: client:HttpClient -> resourceUrl:string -> Task<Result<unit, string>>
val buildCalDavEventIcs: booking:Booking -> hostEmail:string -> videoLink:string option -> string
```

In `src/backend/CalendarSync.fs`, new functions:

```fsharp
val writeBackBookingEvent:
    createConn:(unit -> SqliteConnection) -> client:HttpClient -> writeConfig:CalDavWriteBackConfig
    -> booking:Booking -> hostEmail:string -> videoLink:string option -> Task<unit>

val deleteWriteBackEvent: client:HttpClient -> booking:Booking -> Task<unit>
```
