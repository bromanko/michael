---
id: mwadp-2e14
status: open
deps: []
links: []
created: 2026-02-01T16:06:26Z
type: chore
priority: 2
assignee: Brian Romanko
---
# Add hookify rule: Elm shared types must be in Types.elm

Create a hookify hook that warns when custom types or type aliases are defined outside of Types.elm but are imported by other modules. This enforces the convention: 'Keep all shared type aliases and custom types in Types.elm.'

