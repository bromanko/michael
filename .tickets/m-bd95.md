---
id: m-bd95
status: open
deps: []
links: []
created: 2026-01-31T16:00:12Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Validate datetime parsing in handleSlots and handleBook

odtPattern.Parse(...).Value is called on user-supplied datetime strings without checking ParseResult.Success. Malformed ISO-8601 input throws UnparsableValueException causing an unhandled 500. Should check parse success and return 400 with a clear message.

