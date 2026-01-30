# Spike: CalDAV Calendar Fetching

**Date:** 2026-01-30
**Status:** Complete — concept validated

## Goal

Validate that we can connect to CalDAV providers (Fastmail and iCloud),
discover calendars, and fetch events from F#. This answers two questions:

1. Can we reliably get calendar data from both CalDAV providers?
2. Is the .NET CalDAV/iCal ecosystem good enough for production use?

## Setup

- F# script (`spike/caldav_spike.fsx`) runnable with `dotnet fsi`
- Raw HTTP (`HttpClient` + WebDAV XML) for CalDAV protocol operations
- Ical.Net 4.3.1 for iCalendar (RFC 5545) parsing
- Credentials via environment variables (app passwords, Basic Auth)
- Two providers tested: Fastmail and iCloud

## Providers Tested

| Provider | Endpoint | Auth |
|----------|----------|------|
| Fastmail | `https://caldav.fastmail.com/dav/calendars` | App password (Basic Auth) |
| iCloud   | `https://caldav.icloud.com/` | App-specific password (Basic Auth) |

## Results

### Feature Matrix

| Feature | Fastmail | iCloud |
|---------|:--------:|:------:|
| Basic Auth | Pass | Pass |
| Principal discovery | Pass | Pass |
| Calendar home discovery | Pass | Pass |
| Calendar listing | Pass (2 calendars) | Pass (6 calendars) |
| VEVENT/VTODO filtering | Pass | Pass |
| Event fetching (date range) | Pass | Pass |
| Timezone handling | Pass | Pass |
| All-day events | Not tested | Pass |
| Recurring event detection (RRULE) | Pass (`FREQ=WEEKLY`) | Not tested |
| Multi-timezone events | Not tested | Pass (LAX→PHX flight) |
| Location parsing | Not tested | Pass |
| Cluster redirect | N/A | Pass (redirected to `p161-caldav.icloud.com`) |

### Notable Findings

**Base URL matters.** Neither provider works with a naive PROPFIND on the
root URL. Fastmail requires `/dav/calendars` as the starting point
(discoverable via `.well-known/caldav` → 301 redirect). iCloud works on `/`
but only with auth headers present — unauthenticated requests return 401.

**iCloud cluster redirect works transparently.** iCloud's principal discovery
returns a path like `/152885063/principal/`, and the calendar home redirects
to a cluster-specific host (e.g., `p161-caldav.icloud.com`). The
`HttpClientHandler` with `AllowAutoRedirect = true` handles this correctly.

**Depth header matters.** The `Depth` header on PROPFIND requests must match
what the server expects. iCloud returns 400 on `Depth: 1` for root-level
discovery — `Depth: 0` is required for principal and calendar-home discovery.
`Depth: 1` is correct for calendar listing.

**Recurring events are returned as a single VEVENT with RRULE.** The CalDAV
`time-range` filter matches recurring events that have instances within the
range, but returns the original VEVENT with the RRULE — not expanded
instances. Client-side RRULE expansion (via Ical.Net) will be needed in
production to compute actual occurrence times.

**All-day events use DATE (not DATETIME).** Ical.Net exposes `IsAllDay` and
represents these with date-only values and floating timezone. Production code
will need to handle the distinction between timed and all-day events when
computing availability.

**Multi-timezone events exist.** Travel events (flights) can have different
start and end timezones (e.g., `America/Los_Angeles` → `America/Phoenix`).
The parser handles this correctly via Ical.Net's `TzId` property.

**CalDAVNet was not used.** The spike went directly to raw HTTP + Ical.Net.
This gave full control over the WebDAV XML requests and avoided depending on
a less-mature library. The raw HTTP approach is straightforward — the CalDAV
protocol is simple XML over HTTP — and is recommended for production.

## Recommendation

**Use raw HTTP + Ical.Net for production.** The CalDAV protocol is simple
enough that a dedicated client library isn't needed. The three operations
(PROPFIND for discovery, PROPFIND for listing, REPORT for fetching) are
well-defined XML payloads over HTTP. Ical.Net handles the harder problem of
iCalendar parsing, including RRULE expansion and timezone conversion.

**Production implementation should:**

1. Start with `.well-known/caldav` for initial discovery (handles both
   providers)
2. Cache the calendar-home URL after first discovery (it doesn't change)
3. Use `Depth: 0` for principal/home discovery, `Depth: 1` for listing
4. Expand recurring events client-side using Ical.Net's recurrence API
5. Handle all-day events as full-day blockers in availability computation
6. Support multiple timezones per event

## Output Files

- `spike/caldav_spike.fsx` — F# spike script
- `spike/caldav_output.txt` — redacted sample output from both providers

## Next Steps

- Implement RRULE expansion for recurring events (Ical.Net supports this)
- Build the Calendar Sync module in the F# backend
- Add incremental sync using ctag/sync tokens (optimization, not required
  for MVP)
- Integrate with the Availability Resolver to block fetched events
