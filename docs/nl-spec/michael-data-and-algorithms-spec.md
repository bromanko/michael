# Michael Data and Algorithms Specification (NL Spec)

Normative model for core entities, persistence expectations, and scheduling algorithms.

---

## 1. Core Domain Entities

### 1.1 AvailabilityWindow

- `start`: ISO-8601 offset datetime
- `end`: ISO-8601 offset datetime
- `timezone`: optional IANA timezone

### 1.2 ParseResult

- `availabilityWindows`: list of AvailabilityWindow
- `durationMinutes`: optional int
- `title`: optional string
- `description`: optional string
- `name`: optional string
- `email`: optional string
- `phone`: optional string
- `missingFields`: list of required-field labels still missing

### 1.3 Booking

- `id` GUID
- participant name/email required; phone optional
- title required; description optional
- `startTime`, `endTime` as offset datetime
- `durationMinutes` int
- `timezone` string
- `status` enum: `confirmed | cancelled`
- `createdAt` instant
- `cancellationToken` optional string (required for newly created bookings; nullable for historical rows)

### 1.4 HostAvailabilitySlot

- `id` GUID
- `dayOfWeek` 1..7 (Mon..Sun)
- `startTime` local time (HH:MM)
- `endTime` local time (HH:MM)

### 1.5 SchedulingSettings

- `minNoticeHours`
- `bookingWindowDays`
- `defaultDurationMinutes`
- optional `videoLink`

Defaults if unset:

- min notice 6
- booking window 30
- default duration 30
- video link none

Duration policy (global):

- requested booking duration and computed slot duration MUST be in `[5, 480]` minutes.

### 1.6 Calendar models

Calendar source:

- `id` GUID (deterministic from provider+url)
- provider (`fastmail` or `icloud`)
- base URL
- optional calendar-home URL

Cached event:

- `id` GUID
- source id
- calendar url
- uid
- summary
- start/end instant
- all-day bool

Sync history entry:

- id
- source id
- synced-at instant
- status string
- optional error message

---

## 2. Database Schema Requirements

Required tables:

- `bookings`
- `host_availability`
- `calendar_sources`
- `cached_events`
- `scheduling_settings`
- `sync_status`
- `admin_sessions`
- `sync_history`
- migration tracking table (`atlas_schema_revisions`)

Required indexes:

- cached-events source
- cached-events range
- sync-history `(source_id, synced_at desc)`
- bookings partial unique index on `cancellation_token` where token is not null

Behavioral requirements:

- Insert booking stores both display times and epoch fields for efficient range queries.
- Insert booking generates and stores a unique cancellation token for newly created bookings.
- Booking range query returns only `confirmed` bookings and intersects by `[start,end)` semantics.
- Updating host availability is replace-all in a transaction.
- Replacing cached events for source is transactional.

### 2.1 Atlas revision table compatibility

Migration bookkeeping compatibility is **schema-level**, not just behavior-level.

`atlas_schema_revisions` MUST exist with columns:

- `version` text primary key
- `description` text not null
- `applied_at` text not null

This ensures compatibility with Atlas tooling expectations.

---

## 3. Migration Model

Migration engine must:

1. Discover migration files named `<version>_<name>.sql`.
2. Verify checksums against `atlas.sum` style integrity file.
3. Apply pending migrations in version order.
4. Wrap each migration in transaction.
5. Record version + description + applied timestamp.

Startup must run migrations before serving requests.

---

## 4. Slot Computation Algorithm

Inputs:

- participant availability windows
- host weekly slots
- host timezone
- existing confirmed bookings
- cached calendar blocker intervals
- requested duration minutes
- participant timezone
- current time `now`
- scheduling settings (`minNoticeHours`, `bookingWindowDays`)

Algorithm:

1. Validate duration in `[5, 480]`.
2. Convert participant windows to intervals (instants).
3. Determine earliest participant start and latest participant end.
4. Expand host weekly slots into concrete intervals over the participant date range.
5. Intersect participant intervals with host intervals.
6. Build blocker list from existing bookings and cached events.
7. Subtract blockers from each intersection interval.
8. Chunk remaining intervals into fixed-size duration slots.
9. Drop incomplete tail segments.
10. Filter resulting slots by scheduling settings:
    - keep only slots with `slotStart >= now + minNoticeHours`
    - keep only slots with `slotStart <= now + bookingWindowDays`
11. Convert intervals back to participant timezone offset datetimes.

Rules:

- overlap exists only when `start < end`
- adjacent edges are non-overlapping
- subtraction can split one interval into multiple intervals

---

## 5. Booking-Time Revalidation Algorithm

On `POST /api/book`, before persistence:

1. Validate request shape and duration policy.
2. Recompute slot validity using current data:
   - host availability
   - confirmed bookings
   - cached calendar blockers
   - scheduling settings window constraints
3. Confirm requested slot is still valid.
4. If invalid, reject with HTTP `409` conflict.
5. If valid, persist booking as confirmed.

This rule prevents stale-slot and double-booking races.

---

## 6. Participant Cancellation Authorization Algorithm

On `POST /api/bookings/{id}/cancel`:

1. Validate request shape (`id` parseable GUID, non-empty `token`).
2. Load booking by `id`.
3. If booking is absent, reject with HTTP `404` generic not-found response.
4. If booking has no stored cancellation token, reject with HTTP `404` generic not-found response.
5. Compare provided token with stored token as exact case-sensitive opaque string.
6. If token mismatches, reject with HTTP `404` generic not-found response.
7. If booking status is `confirmed`, persist status transition to `cancelled`.
8. If booking status is already `cancelled`, return success without further mutation (idempotent behavior).

Security semantics:

- API responses must not reveal whether failure came from unknown booking or token mismatch.
- Token is authorization secret for participant cancellation.

---

## 7. Calendar View Composition Algorithm

Given range and display timezone:

1. Read cached external events in range.
2. Read bookings in range.
3. Read host weekly availability slots.
4. Expand availability into concrete events over the range.
5. If a date contains an all-day external event, suppress availability events on that local date.
6. Emit combined event list with type tags:
   - `availability`
   - `calendar`
   - `booking`

Ordering requirement:

- Availability events should come first so external/booking events can render visually on top in DOM order.

---

## 8. Admin Session Data Model

Session record:

- token (primary key)
- created-at instant
- expires-at instant

Rules:

- session duration is 7 days
- expired sessions are deleted opportunistically
- invalid/expired sessions clear cookie and return unauthorized

---

## 9. Data-Level Definition of Done

A reimplementation is complete when:

1. All required entities and tables are representable and persisted.
2. Migration integrity checks prevent schema drift.
3. Slot computation behavior matches algorithmic rules, including scheduling-window filters.
4. Booking-time revalidation rejects stale slots with conflict semantics.
5. Participant cancellation token authorization is implemented with generic-not-found failure semantics.
6. Calendar view composition rules match blocker/all-day semantics.
7. Session lifecycle and expiry behavior are correct.
