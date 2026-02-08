# Michael Frontend Specification (NL Spec)

Defines participant and admin frontend behavior independent of implementation language.

---

## 1. Booking Frontend

### 1.1 Application model

The booking experience is a single-page multi-step flow with explicit states:

1. Title step
2. Duration step
3. Availability text step
4. Parsed availability confirmation step
5. Slot selection step
6. Contact info step
7. Final confirmation step
8. Completion step

### 1.2 Required interactions

- User can move forward only when current step requirements are satisfied.
- Back action returns to prior step and clears dependent derived state where needed.
- Keyboard affordances:
  - Enter submits where appropriate
  - textarea Enter submits unless Shift+Enter
- Loading state shown during parse/slot/book API calls.
- Error banner shown for validation/network/server failures.

### 1.3 Timezone UX

- Initialize timezone from browser.
- Allow timezone change from UI.
- If timezone changes while parsed-availability or slot-choice states are active, refresh affected derived data (re-parse or re-fetch slots).

### 1.4 Data validation in UI

- Required: title, duration, availability text, name, email.
- Email must pass minimal client validation consistent with backend semantics.
- Duration entered by user must be in `[5, 480]` minutes.
- Client validation should mirror server validation where practical.

### 1.5 Booking conflict handling

When booking API returns conflict (`409`, e.g., `slot_unavailable`):

- show user-friendly message that slot is no longer available
- return user to slot-selection (or refresh slots) without losing previously entered contact fields when feasible

---

## 2. Admin Frontend

### 2.1 Session behavior

- On app start, check admin session endpoint.
- Maintain explicit auth state (checking, guest, logged-in).
- Guard protected routes when not authenticated.

### 2.2 Required routes/pages

- Login
- Dashboard
- Bookings list
- Booking detail
- Calendars source list
- Calendar view
- Availability editor
- Settings
- Not found

### 2.3 Bookings UI

- Paginated list with page/pageSize controls.
- Status filter UX values: all, confirmed, cancelled.
- API mapping for "all": omit `status` query parameter (or use explicit `status=all` only if backend supports it equivalently).
- Detail view by booking ID.
- Cancel action with backend call and UI refresh.

### 2.4 Calendars UI

- Show configured sources and latest sync status metadata.
- Allow manual sync trigger per source.
- Show recent sync history entries.

### 2.5 Availability UI

- Edit weekly slots with day and start/end times.
- Save as replace-all payload.
- Show validation feedback for invalid day/time ranges.

### 2.6 Settings UI

- Edit min notice, booking window, default duration, optional video link.
- Enforce backend-compatible constraints in client where practical.
- Communicate that these settings affect real slot availability and booking acceptance.

### 2.7 Calendar view UI

- Query backend for date-range events.
- Display merged event stream by event type:
  - availability
  - external calendar
  - booking
- Support display timezone selection and range navigation.
- If timezone is not specified in query, backend host timezone behavior should be treated as default contract.

---

## 3. Static Bootstrapping

Two HTML entry pages are required:

1. Public booking root
2. Admin root

Both must initialize SPA runtime and pass bootstrap flags:

- Booking: browser timezone
- Admin: browser timezone + current date

---

## 4. Accessibility and Agent-Friendliness Requirements

- Use semantic form controls and clear labels.
- Keep deterministic IDs/selectors for major interactive elements.
- Preserve straightforward DOM structure so browser automation and AI agents can traverse flow reliably.

---

## 5. Frontend Definition of Done

Complete when:

1. Booking flow reaches completion through real backend APIs.
2. Admin pages are session-gated and fully operational.
3. Route transitions and state transitions are deterministic and recoverable.
4. Timezone and validation behaviors match product/API specs.
5. Booking conflict handling is implemented and user-recoverable.
