# Spike: CalDAV Write-Back (PUT/DELETE)

**Date:** _(fill in after running)_
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

_(Paste the spike script output below and fill in the answers.)_

### Fastmail

**PUT status:**
**Location header:**
**GET after PUT:**
**REPORT found UID:**
**DELETE status:**
**2nd DELETE status:**

### iCloud (optional)

**PUT status:**
**Location header:**
**GET after PUT:**
**REPORT found UID:**
**DELETE status:**
**2nd DELETE status:**

## Conclusions

_(Summarize findings. Note any surprises or deviations from expectations.)_
