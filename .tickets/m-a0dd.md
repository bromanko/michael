---
id: m-a0dd
status: closed
deps: []
links: []
created: 2026-02-22T16:58:29Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Fix created_at parsing for space-separated datetime format

The bookings table DEFAULT (datetime('now')) produces '2026-02-16 21:33:26' format, but InstantPattern.ExtendedIso expects 'T' separator and 'Z' suffix. Existing rows with this format cause NodaTime.Text.UnparsableValueException when reading bookings. Fix the parser to be tolerant, and update the migration default to use strftime for ISO 8601.

