---
id: m-cce4
status: in_progress
deps: []
links: []
created: 2026-01-31T16:05:24Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Reject invalid timezones instead of silent fallback

handleParse silently falls back to America/New_York when an invalid timezone is provided. handleSlots and handleBook validate timezone is non-empty but never check it's a valid IANA timezone, causing DateTimeZoneNotFoundException. All handlers should validate the timezone and return 400 for unrecognized values.

