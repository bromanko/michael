---
id: mwadp-3657
status: open
deps: []
links: []
created: 2026-02-01T16:11:45Z
type: chore
priority: 2
assignee: Brian Romanko
---
# Add hookify rule: Elm views must use Route.toPath instead of hardcoded URL strings

Create a hookify hook that warns when Elm view code contains hardcoded admin URL paths (e.g. href "/admin/...") instead of using Route.toPath. This enforces that all navigation paths go through the Route module so changes to routes are caught at compile time.

