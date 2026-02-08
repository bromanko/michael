---
id: m-cd77
status: open
deps: []
links: []
created: 2026-02-08T19:34:16Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Unify booking duration validation policy across API and UI

Spec-writing phase found duration constraints are inconsistent (backend accepts >0 while settings/frontend imply bounded range). Define and enforce one global duration policy (currently spec now says 5..480 minutes) across /api/slots, /api/book, and frontend validation.


## Notes

**2026-02-08T19:34:20Z**

Created during NL spec writing/reconciliation to capture API/frontend/backend duration-policy mismatch.
