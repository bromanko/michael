---
id: mwbp-37d6
status: open
deps: []
links: []
created: 2026-01-30T21:35:21Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Sanitize error responses to avoid leaking internal details

API error responses in Handlers.fs currently return raw error strings from Gemini API failures and database exceptions. These could expose file paths, connection strings, or stack traces to the client. Replace with generic user-facing error messages once logging is in place.

