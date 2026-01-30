# Spike: CalDAV Calendar Fetching (F#)

## Goal

Validate that we can connect to CalDAV providers (Fastmail and iCloud),
discover calendars, and fetch events from F#. This answers two questions:

1. Can we reliably get calendar data from both CalDAV providers?
2. Is the .NET CalDAV/iCal ecosystem good enough for production use?

## Approach

F# script (`.fsx`) in the spike directory, runnable with `dotnet fsi`.
Uses NuGet package references inline — no separate project needed.

### Files to create

```
spike/
└── caldav_spike.fsx      # All-in-one: connect, discover, fetch, print results
```

### Files to modify

None. The `.fsx` script pulls NuGet packages via `#r "nuget:..."` directives,
so no flake.nix changes needed.

### Libraries

- **CalDAVNet** (`HaemmerElectronics.SeppPenner.CalDAVNet` 1.0.3) — CalDAV
  client for .NET 9. Handles PROPFIND/REPORT, auth, calendar discovery.
- **Ical.Net** (5.2.0) — iCalendar (RFC 5545) parsing. Handles RRULE
  expansion, timezone conversion, event serialization.

If CalDAVNet proves too limited, fall back to raw HTTP with `HttpClient` +
WebDAV XML requests + Ical.Net for parsing. This is a valid spike outcome.

### What the script does

1. **Provider config** — Discriminated union for Fastmail / iCloud with
   endpoint URLs and auth details
2. **Credentials from env vars**:
   - `FASTMAIL_CALDAV_USER` / `FASTMAIL_CALDAV_PASSWORD`
   - `ICLOUD_CALDAV_USER` / `ICLOUD_CALDAV_PASSWORD`
3. **Connect** — Authenticate via Basic Auth (app passwords)
4. **Discover calendars** — List all calendars for the account
   (name, URL, supported components)
5. **Fetch events** — Get events for a 14-day window from each calendar
6. **Print results** — Formatted output: calendars found, events per
   calendar, event details (summary, start/end, timezone, recurring flag)
7. **Edge cases to probe**:
   - Recurring events (do they expand within the date range?)
   - All-day events (how are they represented?)
   - Timezone handling (UTC vs local times)
   - iCloud cluster redirect (p22/p23 discovery)

### What we're NOT doing (out of scope)

- Availability computation / free slot calculation
- Incremental sync / sync tokens
- OAuth authentication
- Google Calendar integration
- Integration with the parser spike
- Creating a reusable library (this is exploratory)

## Key things to validate

| # | Question | How we'll test it |
|---|----------|-------------------|
| 1 | Fastmail Basic Auth works | Connect with app password |
| 2 | iCloud discovery + cluster redirect | Connect, follow redirects |
| 3 | Calendar listing | Print all discovered calendars |
| 4 | Event fetching by date range | Fetch 14-day window |
| 5 | Recurring event expansion | Check if RRULEs produce instances |
| 6 | Timezone info preserved | Print raw + parsed timezone data |
| 7 | All-day events | Check representation vs timed events |
| 8 | CalDAVNet library quality | Note any gaps, quirks, or dealbreakers |

## CalDAV Protocol Reference

### Fastmail

- **Endpoint**: `https://caldav.fastmail.com/`
- **Auth**: HTTP Basic Auth with app password
- **Username**: Full email address (e.g., `user@domain.com`)
- **App password**: Generate in Settings → Privacy & Security → Integrations

### iCloud

- **Endpoint**: `https://caldav.icloud.com/`
- **Auth**: HTTP Basic Auth with app-specific password
- **Discovery**: Returns cluster-specific URLs (p22, p23, etc.) — must follow redirects
- **App password**: Generate in Apple ID Settings → Security → App-Specific Passwords

### CalDAV Operations Used

- **PROPFIND** — Discover calendars (list collections, get display names)
- **REPORT (calendar-query)** — Fetch events matching a date range filter
- **iCalendar parsing** — Parse VEVENT components from returned data

## Documentation

After running the spike, write `docs/spike-caldav-fetch.md`:
- Goal, setup, providers tested
- Results per provider (what worked, what didn't)
- Library assessment (CalDAVNet: keep or replace with raw HTTP?)
- Notable findings (auth, recurring events, timezones)
- Recommendation for production implementation approach
- Next steps

## Verification

```bash
# Set credentials
export FASTMAIL_CALDAV_USER="..."
export FASTMAIL_CALDAV_PASSWORD="..."
export ICLOUD_CALDAV_USER="..."
export ICLOUD_CALDAV_PASSWORD="..."

# Run the spike
dotnet fsi spike/caldav_spike.fsx
```

Expected output: list of calendars and events from both providers,
or clear error messages indicating what failed and why.
