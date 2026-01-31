# CalDAV Calendar Integration — Implementation Plan

## Summary

Replace the current hardcoded host availability with real calendar data fetched
from CalDAV providers (Fastmail, iCloud). The model becomes:

> **base availability windows** (existing `host_availability` table) **MINUS
> calendar events** (new CalDAV sync) **MINUS existing bookings** → available
> slots

Two new modules: `CalDav.fs` (protocol + parsing) and `CalendarSync.fs`
(orchestration + caching). One new NuGet dependency: `Ical.Net 4.3.1`.

Google Calendar is explicitly deferred — CalDAV covers Fastmail and iCloud.

---

## Step 1: Domain Types + Database Schema

**Files:** `Domain.fs`, `Database.fs`

Add to `Domain.fs`:

```fsharp
type CalDavProvider = Fastmail | ICloud

// In-memory only — credentials are NEVER persisted to the database.
// Built in Program.fs from environment variables.
type CalDavSourceConfig =
    { Id: Guid; Provider: CalDavProvider; BaseUrl: string
      Username: string; Password: string
      CalendarHomeUrl: string option }

type CachedEvent =
    { Id: Guid; SourceId: Guid; CalendarUrl: string; Uid: string
      Summary: string; StartInstant: Instant; EndInstant: Instant
      IsAllDay: bool }
```

Add to `Database.fs` — two new tables (using existing `CREATE TABLE IF NOT
EXISTS` pattern):

```sql
calendar_sources (id, provider, base_url, calendar_home_url,
                  last_synced_at, last_sync_result)
-- NOTE: No credential columns. Credentials live in env vars only.

cached_events (id, source_id, calendar_url, uid, summary,
               start_instant, end_instant, is_all_day,
               UNIQUE(source_id, uid, start_instant))
-- Composite unique key: recurring events share a UID but have
-- different start_instant per occurrence.
```

**Enable WAL mode** on SQLite connections to prevent the background sync's
write transactions from blocking concurrent HTTP request reads:

```fsharp
let createConnection (dbPath: string) =
    let conn = new SqliteConnection($"Data Source={dbPath}")
    conn.Open()
    Db.newCommand "PRAGMA journal_mode=WAL" conn |> Db.exec
    conn
```

New DB functions (all returning `Result` per conventions — no `try/with`):

- `getCalendarSources` — read non-secret metadata
- `upsertCalendarSource` — store provider, base URL, cached home URL
- `updateSyncStatus` — record last sync time + result
- `getCachedEventsInRange` — query events overlapping a time range
- `replaceEventsForSource` — DELETE + INSERT in a transaction for a
  source + time range

---

## Step 2: CalDav.fs Module

**New file:** `CalDav.fs` (between `Database.fs` and `Availability.fs`)

Port the spike (`spike/caldav_spike.fsx`) to a proper module.

### Network functions (use `task { }` CE):

- `discoverPrincipal` — PROPFIND for `current-user-principal` (Depth: 0)
- `discoverCalendarHome` — PROPFIND for `calendar-home-set` (Depth: 0)
- `listCalendars` — PROPFIND with Depth: 1, filter to VEVENT-supporting
  calendars
- `fetchRawEvents` — REPORT with `time-range` filter (Depth: 1)

HTTP setup per source:
- `HttpClientHandler` with `AllowAutoRedirect = true`,
  `MaxAutomaticRedirections = 10`
- 30-second timeout on `HttpClient`
- Basic Auth via `Authorization` header
- TLS certificate validation left at defaults (system trust store)

### Parsing function (pure, synchronous — no `task`):

`parseAndExpandEvents` — the core parsing logic:

1. Parse ICS text with `Calendar.Load(icsText)`
2. **Filter by STATUS**: skip events with `STATUS:CANCELLED`
3. **Filter by TRANSP**: skip events with `TRANSP:TRANSPARENT` (these don't
   block time — e.g., "Working from home" all-day entries)
4. For recurring events: expand with `CalendarEvent.GetOccurrences(start, end)`
   bounded to sync window
5. For each occurrence:
   - **All-day events**: `DTEND` is exclusive per RFC 5545. Convert to
     midnight-to-midnight in host timezone → UTC Instants. Handle DST
     transitions (a "day" may be 23 or 25 hours).
   - **Events with no DTEND**: for timed events default duration is zero
     (skip as no-ops); for all-day default is one day.
   - **Timed events**: extract timezone from `DtStart.TzId`, look up in
     NodaTime `DateTimeZoneProviders.Tzdb`, convert to Instants.

### Timezone bridging (Ical.Net → NodaTime):

This is the hardest part. Ical.Net uses VTIMEZONE-based timezone IDs which
may not match IANA IDs exactly. Strategy:

1. Try `DateTimeZoneProviders.Tzdb.GetZoneOrNull(tzId)` directly
2. If null, try stripping path prefixes (some servers emit
   `/citadel.org/.../America/New_York` — extract after last `/`)
3. If still null, fall back to host timezone (from `host_availability` table)
4. For UTC events (`IsUtc = true`), use `DateTimeZone.Utc` directly
5. For floating/no-TZ events, assume host timezone

### High-level sync function:

`syncSource` — discover (skip if `CalendarHomeUrl` is cached) → list
calendars → fetch events from each → parse and expand → return
`Result<CachedEvent list, string>`

---

## Step 3: CalendarSync.fs Module

**New file:** `CalendarSync.fs` (after `CalDav.fs`)

Orchestration layer:

- `syncAllSources` — iterate sources, call `CalDav.syncSource`, write
  results to DB. Serialize with `SemaphoreSlim(1, 1)` using
  `WaitAsync(timeout)` (60-second timeout to prevent deadlock).
- `getCachedBlockers` — read `cached_events` for a time range, convert
  to `Interval list`

Accept `IClock` parameter (not `SystemClock.Instance`) for testability.

Sync range: now → now + 2 months (covers the default 1-month booking window
with margin).

Background timer: `System.Threading.Timer` every 10 minutes. Log sync
results to stderr (`eprintfn`) — structured logging is a separate ticket.

---

## Step 4: Wire Into Availability Computation

**Files:** `Availability.fs`, `Handlers.fs`

Change `computeSlots` to accept a new `calendarBlockers: Interval list`
parameter. The only logic change:

```fsharp
// Before:
let available = intersected |> List.collect (fun i -> subtract i bookingIntervals)

// After:
let allBlockers = bookingIntervals @ calendarBlockers
let available = intersected |> List.collect (fun i -> subtract i allBlockers)
```

Update `handleSlots` to call `CalendarSync.getCachedBlockers` and pass the
result to `computeSlots`.

Update existing tests to pass `[]` for the new parameter.

---

## Step 5: Wire Into Program.fs

**File:** `Program.fs`

- Read optional env vars: `MICHAEL_CALDAV_FASTMAIL_USER`,
  `MICHAEL_CALDAV_FASTMAIL_PASSWORD`, `MICHAEL_CALDAV_ICLOUD_USER`,
  `MICHAEL_CALDAV_ICLOUD_PASSWORD`
- Build `CalDavSourceConfig list` (empty if no credentials set — app works
  as before)
- Upsert `calendar_sources` rows on startup (non-secret metadata only)
- Run initial sync (fire-and-forget — first requests before sync completes
  will compute slots without calendar blockers, which is acceptable)
- Start `System.Threading.Timer` for background sync (10-min interval)
- Pass configs through to `handleSlots`

---

## Step 6: Tests

- Database tests for new tables (insert, query, replace, WAL mode)
- `parseAndExpandEvents` unit tests with synthetic ICS strings:
  - Single event
  - Recurring event with RRULE (verify expansion)
  - Recurring event with EXDATE (verify exclusion)
  - All-day event (verify exclusive DTEND handling)
  - Event with no DTEND
  - Cancelled event (verify filtered out)
  - Transparent event (verify filtered out)
  - Non-standard timezone ID (verify fallback)
- `getCachedBlockers` test (seed DB, verify Interval conversion)
- `computeSlots` test with calendar blockers (verify events subtract
  correctly)
- Existing tests updated for new `computeSlots` signature

---

## Compile Order (Michael.fsproj)

```xml
<Compile Include="Domain.fs" />
<Compile Include="Database.fs" />
<Compile Include="CalDav.fs" />            <!-- NEW -->
<Compile Include="CalendarSync.fs" />       <!-- NEW -->
<Compile Include="Availability.fs" />
<Compile Include="GeminiClient.fs" />
<Compile Include="Handlers.fs" />
<Compile Include="Program.fs" />
```

Note: preserve any other existing `<Compile>` entries (e.g., `DevReload.fs`
if present) in their current position.

---

## Env Var Summary

| Variable | Required | Purpose |
|---|---|---|
| `MICHAEL_CALDAV_FASTMAIL_USER` | No | Fastmail CalDAV username |
| `MICHAEL_CALDAV_FASTMAIL_PASSWORD` | No | Fastmail app password |
| `MICHAEL_CALDAV_ICLOUD_USER` | No | iCloud CalDAV username |
| `MICHAEL_CALDAV_ICLOUD_PASSWORD` | No | iCloud app-specific password |

All optional. If no CalDAV credentials are set, the system works exactly as
before (base availability windows only, no calendar subtraction).

---

## Known Risks

- **Ical.Net RRULE expansion** — the spike validated parsing but not
  expansion. Edge cases with EXDATE, COUNT+UNTIL, timezone-sensitive
  recurrences may surface. Mitigated by thorough unit tests with synthetic
  ICS.
- **Ical.Net 4.3.1 on .NET 9** — verify compatibility. Consider checking
  for a newer version or the `Ical.Net.Core` fork if issues arise.
- **CalDAV rate limiting** — providers may throttle aggressive polling. The
  10-minute interval is conservative, but we should handle HTTP 429/503 with
  a simple backoff (skip the sync cycle, try again next interval).

---

## Verification

1. `dotnet build` succeeds
2. `dotnet test` passes (existing + new tests)
3. Manual: set Fastmail env vars, start app, hit `POST /api/slots` — verify
   that events on the Fastmail calendar reduce available slots
4. Manual: verify app still works correctly with no CalDAV env vars set
