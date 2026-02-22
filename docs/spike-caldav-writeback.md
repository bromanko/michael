# Spike: CalDAV Write-Back (PUT/DELETE)

**Date:** 2026-02-22
**Ticket:** m-5d0e

## Purpose

Validate CalDAV PUT and DELETE behavior on Fastmail (and optionally iCloud)
before relying on it in production.

## How to Run

```
export FASTMAIL_CALDAV_USER="your-email@domain.com"
export FASTMAIL_CALDAV_PASSWORD="your-app-password"
export FASTMAIL_CALDAV_CALENDAR_URL="https://caldav.fastmail.com/dav/calendars/user/your-email@domain.com/Default/"
dotnet fsi spike/caldav_writeback_spike.fsx
```

Tip: Run `dotnet fsi spike/caldav_spike.fsx` first to discover your calendar
URLs if you don't know the exact collection URL.

## Questions

1. Does Fastmail accept a PUT to `{calendarCollectionUrl}/{uid}.ics` with a
   VCALENDAR body containing no `METHOD` property?
2. Does it return 201 Created? Does it include a `Location` header?
3. Does a subsequent GET or REPORT return the created event?
4. Does DELETE on the resource URL return 204? What does a second DELETE return?

## Results

### Fastmail

```
────────────────────────────────────────────────────────────
  CalDAV Write-Back Spike
────────────────────────────────────────────────────────────
  Date: 2026-02-22

  [1/5] PUT a VCALENDAR (no METHOD property)
    ℹ️  Generated ICS content (no METHOD, no ORGANIZER/ATTENDEE)
    Status: 201 Created
    ✅ Server accepted the PUT (Created)
    ℹ️  No Location header in response — resource lives at request URL
    ETag: (present)

  [2/5] GET the resource back
    Status: 200 OK
    ✅ Resource exists at the request URL
    ✅ Response body contains our UID
    ✅ No METHOD property in stored resource

  [3/5] REPORT calendar-query to verify event appears
    ✅ Event found in REPORT results (UID matched)

  [4/5] DELETE the resource
    Status: 204 NoContent
    ✅ Server accepted the DELETE (NoContent)

  [5/5] DELETE again (idempotency check)
    Status: 404 NotFound
    ✅ Second DELETE returned 404 (resource already gone)

────────────────────────────────────────────────────────────
  Fastmail Summary
────────────────────────────────────────────────────────────
  PUT accepted:      YES
  PUT status:        201
  Location header:   (none)
  GET after PUT:     200
  REPORT found UID:  YES
  DELETE status:     204
  2nd DELETE status: 404
```

### iCloud (optional)

Not tested.

## Conclusions

All assumptions from the plan are confirmed on Fastmail:

1. **PUT accepted without METHOD** — Fastmail returns 201 Created for a
   VCALENDAR with no `METHOD` property. No special headers needed.
2. **No Location header** — the resource lives at the request URL. Our
   implementation correctly falls back to the request URL when no Location
   header is present.
3. **Immediately visible** — both GET and REPORT return the event right after
   PUT. No delay or sync cycle needed.
4. **DELETE returns 204** — clean removal. Second DELETE returns 404, which
   our implementation already treats as success (idempotent).
5. **ETag returned** — Fastmail provides an ETag on PUT, though we don't
   currently use it. Could be useful for conditional updates in the future.

No surprises. The implementation matches server behavior exactly.
