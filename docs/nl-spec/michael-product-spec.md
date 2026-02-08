# Michael Product Specification (NL Spec)

Defines product intent and user-visible behavior for Michael.

---

## 1. Product Summary

Michael is a self-hosted personal scheduling tool (Calendly alternative) where participants provide availability in natural language and Michael computes overlap with host availability.

The product has two primary surfaces:

- **Public booking flow** for participants
- **Admin dashboard** for the host

---

## 2. Core Principles

1. **Natural-language first**: participants describe availability in free text.
2. **Timezone-aware UX**: always represent meeting times in participant-selected timezone on booking side.
3. **Simple host operations**: one admin surface for bookings, availability, calendars, and settings.
4. **Fail-fast backend**: required runtime configuration errors must stop startup clearly.
5. **Single-instance simplicity**: SQLite-backed deployment is acceptable and expected.

---

## 3. Participant Journey

1. Participant enters a meeting title.
2. Participant chooses duration (preset or custom minutes).
3. Participant provides availability text.
4. System parses text into windows.
5. Participant confirms interpreted windows.
6. System shows available slots (overlap with host minus blockers, plus scheduling-window constraints).
7. Participant selects slot.
8. Participant enters contact info (name, email, optional phone).
9. Participant confirms booking summary.
10. System stores booking and returns confirmation.

### Participant requirements

- Title is required.
- Duration is required and must be in the system-supported range.
- Availability text is required.
- Name and valid email are required.
- Phone is optional.

### Scheduling-window requirements

The host-configured settings MUST be enforced for participant-visible slots and booking acceptance:

- `minNoticeHours`
- `bookingWindowDays`

This means:

- unavailable slots are not shown, and
- stale/invalid selections are rejected at booking time.

---

## 4. Admin Journey

1. Admin logs in with configured password.
2. Admin views dashboard stats.
3. Admin lists bookings with pagination and status filtering.
4. Admin opens booking detail.
5. Admin cancels booking when needed.
6. Admin views connected calendars and sync history.
7. Admin triggers manual sync for any configured source.
8. Admin edits host weekly availability.
9. Admin edits scheduling settings.
10. Admin views merged calendar (availability + external events + bookings).

---

## 5. Feature Modules

### 5.1 Booking flow

Must support:

- Parsing natural-language availability
- Explicit parsed-availability confirmation
- Slot selection based on overlap computation
- Final booking confirmation step
- Booking-time revalidation to prevent stale-slot/double-booking outcomes

### 5.2 Calendar integration

- Ingest external events via CalDAV
- Use cached events as blockers in slot computation
- Support multiple sources (at minimum Fastmail and iCloud configurations)

### 5.3 Host availability

- Weekly recurring host availability slots
- Editable via admin UI
- Serves as base availability before applying blockers

### 5.4 Scheduling settings

Editable host settings include:

- minimum scheduling notice hours
- booking window days
- default duration minutes
- optional video link

Settings are not cosmetic; they directly constrain booking and slot generation.

### 5.5 Admin auth

- Password-based login
- Session cookie auth for admin routes

---

## 6. UX Constraints

- Booking flow is stepwise and conversational in tone.
- Form validation errors should be immediate and clear.
- API failures should surface readable error banners.
- Progress/state should be obvious to users.

---

## 7. Explicit Non-Goals (Current Baseline)

- Multi-host tenancy
- Dedicated reschedule workflow (cancel + rebook is acceptable)
- Required participant email verification
- Distributed/multi-instance operation
- Passkey/OIDC auth as baseline requirement

---

## 8. Product Definition of Done

Implementation satisfies this spec when:

1. Participant journey works end-to-end from input to booking confirmation.
2. Admin journey works end-to-end with session-gated APIs.
3. Slot options correctly reflect host availability, blockers, and scheduling-window constraints.
4. Booking-time revalidation prevents stale-slot booking.
5. Calendar sync feeds blocker data into booking logic.
6. The system behavior matches this product spec and companion technical specs.
