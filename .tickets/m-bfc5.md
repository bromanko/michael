---
id: m-bfc5
status: closed
deps: []
links: []
created: 2026-02-18T00:40:11Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Add accessible name to timezone selector button

The timezone selector button renders as "🌐 UTC ▾" (with the current timezone name). It lacks an accessible label that identifies its purpose — the button text contains only the emoji, timezone value, and a disclosure triangle.

Add an aria-label like "Change timezone (currently UTC)" or visually hidden text so screen readers and automated agents can identify the control's purpose without relying on the emoji or visual context. Discovered via black-box Playwright testing against EARS spec TZ-004.

