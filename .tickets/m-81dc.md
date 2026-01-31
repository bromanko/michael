---
id: m-81dc
status: open
deps: []
links: []
created: 2026-01-31T16:04:59Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Handle malformed/missing request bodies gracefully

All three API handlers call ReadFromJsonAsync without handling failure cases. Missing body, wrong Content-Type, or malformed JSON causes JsonException or returns default-initialized records with null fields, leading to NullReferenceException instead of a clean 400 response.

