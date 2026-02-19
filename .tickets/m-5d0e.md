---
id: m-5d0e
status: open
deps: []
links: []
created: 2026-02-19T17:03:31Z
type: feature
priority: 2
assignee: Brian Romanko
---
# CalDAV write-back: create calendar event on booking

When a booking is confirmed, create a VEVENT on the host's CalDAV calendar (Fastmail/iCloud). Currently CalDAV sync is read-only — it pulls events for conflict checking but never writes back. The host only sees bookings in the SQLite database and the admin dashboard, not on their actual calendar. This should create the event on the primary calendar source so the booking appears alongside other events. Should also handle cancellation (delete/cancel the CalDAV event when a booking is cancelled).

