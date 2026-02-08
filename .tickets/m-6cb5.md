---
id: m-6cb5
status: closed
deps: []
links: []
created: 2026-02-08T19:34:16Z
type: bug
priority: 2
assignee: Brian Romanko
---
# Enforce scheduling settings in slot generation and booking validation

Spec-writing phase found that minNoticeHours and bookingWindowDays are stored/editable but not enforced in /api/slots and /api/book. Implement server-side enforcement in slot filtering and booking-time revalidation.


## Notes

**2026-02-08T19:34:20Z**

Created during NL spec writing/reconciliation to capture behavior gap discovered while formalizing booking constraints.

**2026-02-08T19:46:32Z**

Implemented scheduling settings enforcement and booking-time revalidation. /api/slots now filters computed slots by minNoticeHours and bookingWindowDays. /api/book now revalidates requested slot against host availability, confirmed bookings, cached calendar blockers, and scheduling settings before insert, and returns 409 slot_unavailable when invalid/stale.
