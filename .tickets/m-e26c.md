---
id: m-e26c
status: open
deps: []
links: []
created: 2026-01-31T16:05:45Z
type: chore
priority: 2
assignee: Brian Romanko
---
# Replace try/with control flow with Result in Database and GeminiClient

insertBooking (Database.fs) and parseInput (GeminiClient.fs) use try/with as their primary error handling, catching all exceptions indiscriminately including programming bugs. This violates the project convention of using Result for errors. Refactor to use Result combinators and only let true unexpected exceptions propagate.

