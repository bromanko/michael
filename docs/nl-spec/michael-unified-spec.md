# Michael Unified Specification (NL Spec)

A complete natural-language specification for **Michael**, a self-hosted personal scheduling tool (a Calendly alternative) that uses natural-language availability input.

---

## Table of Contents

1. Product Definition
2. Core User Journeys
3. System Architecture
4. Domain Model and Data Rules
5. HTTP API Specification
6. Booking Slot Computation Rules
7. Calendar Sync and CalDAV Behavior
8. Admin Authentication and Session Model
9. Email Notification Behavior
10. Frontend Behavior (Booking + Admin)
11. Static Assets and Browser Bootstrap
12. Database Schema and Migration Model
13. Configuration and Environment Variables
14. Build, Development, and CI Requirements
15. Testing Requirements
16. Non-Goals / Explicitly Out of Scope
17. Definition of Done

---

## 1. Product Definition

Michael is a personal scheduling tool where participants provide availability in natural language and Michael finds overlap with host availability.

This project must provide:

- A public booking flow (participant-facing)
- A password-protected admin dashboard (host-facing)
- Calendar ingestion via CalDAV into a local event cache
- Slot computation based on participant windows + host weekly availability - blockers
- Booking creation and cancellation
- Optional SMTP notifications for cancellation events
- SQLite persistence with versioned migrations

The product is self-hosted and optimized for a single-host/single-instance deployment.

---

## 2. Core User Journeys

### 2.1 Participant booking journey

1. Participant enters meeting title.
2. Participant chooses duration (preset or custom).
3. Participant writes free-text availability.
4. Backend parses text into structured windows using Gemini.
5. Participant confirms parsed windows.
6. System computes overlapping slots.
7. Participant selects a slot.
8. Participant enters name/email/optional phone.
9. Participant confirms booking.
10. Backend stores booking and returns booking ID.
11. Participant can later cancel from an emailed cancellation link via public confirmation flow.

### 2.2 Host/admin journey

1. Admin logs in with a configured password.
2. Admin sees dashboard (upcoming count + next booking).
3. Admin can list and inspect bookings.
4. Admin can cancel bookings.
5. Admin can view configured calendar sources and sync history.
6. Admin can manually trigger a sync for a source.
7. Admin can edit weekly host availability slots.
8. Admin can edit scheduling settings (notice, window, default duration, optional video link).
9. Admin can open merged calendar view for any date range and timezone.

---

## 3. System Architecture

Use a monorepo with these major components:

- `src/backend`: HTTP server + domain logic + DB + calendar sync + AI parsing + admin auth
- `src/frontend/booking`: Elm SPA for participant booking flow
- `src/frontend/admin`: Elm SPA for admin dashboard
- `src/frontend/styles`: Tailwind source CSS
- `src/fake-caldav`: local fake CalDAV server for development/testing
- `tests/Michael.Tests`: backend test suite

Current implementation stack (normative default):

- Backend: F# + Falco
- Frontend: Elm
- Styling: Tailwind CSS
- DB: SQLite
- Time library: NodaTime
- Email: SMTP via MailKit
- AI parser: Google Gemini generateContent API

---

## 4. Domain Model and Data Rules

### 4.1 Availability window

A participant availability window has:

- start (ISO-8601 date-time with offset)
- end (ISO-8601 date-time with offset)
- optional timezone string (IANA)

### 4.2 Parse result

Parse result includes:

- availability windows (list)
- duration minutes (optional)
- title (optional)
- description (optional)
- name (optional)
- email (optional)
- phone (optional)
- missing fields (list of required fields still missing)

### 4.3 Booking entity

Booking fields:

- id (GUID)
- participant name (required)
- participant email (required)
- participant phone (optional)
- title (required)
- description (optional)
- start/end as offset datetime
- duration minutes
- timezone string
- status: confirmed or cancelled
- created-at instant
- cancellation token (nullable for historical rows, populated for new bookings)

### 4.4 Host availability slot

Weekly recurring slot:

- id (GUID)
- day of week (1-7, Monday=1)
- start local time (HH:MM)
- end local time (HH:MM)

Seed default when DB is empty: Monday-Friday 09:00-17:00.

### 4.5 Scheduling settings

Stored settings:

- min notice hours (default 6)
- booking window days (default 30)
- default duration minutes (default 30)
- optional video link

### 4.6 Calendar source and cached events

Calendar source:

- id (GUID, deterministic from provider+url in runtime config)
- provider (`fastmail` or `icloud`)
- base URL
- optional calendar home URL
- last sync metadata

Cached event:

- id (GUID)
- source id
- calendar URL
- UID
- summary
- start/end instant
- all-day boolean

---

## 5. HTTP API Specification

All JSON keys are camelCase.

### 5.1 Public booking API

#### POST `/api/parse`

Request:

- `message` (required, non-empty)
- `timezone` (required, valid IANA timezone)
- `previousMessages` (array of strings)

Behavior:

- Concatenate previous messages + current message.
- Call Gemini parser with a strict extraction prompt and current reference datetime in participant timezone.
- Return:
  - `parseResult`
  - `systemMessage` summarizing parsed fields and missing fields.

Errors:

- 400 for invalid input/timezone/malformed body
- 500 for parser/internal failure (sanitized message)

#### POST `/api/slots`

Request:

- `availabilityWindows` (non-empty list)
- `durationMinutes` (>0)
- `timezone` (valid IANA timezone)

Behavior:

- Parse and validate each window datetime.
- Query host availability, existing confirmed bookings in the relevant range, cached calendar blockers.
- Compute candidate slots (algorithm defined later).
- Return `slots` list with start/end ISO offset datetimes.

Errors: 400 validation errors, 500 internal errors.

#### POST `/api/book`

Request:

- `name` required non-empty
- `email` required valid format (`local@domain` with dot in domain)
- `phone` optional
- `title` required non-empty
- `description` optional
- `slot.start` + `slot.end` required valid offset datetime
- `durationMinutes` > 0
- `timezone` valid IANA timezone

Behavior:

- Create confirmed booking with new GUID.
- Persist to DB.
- Return `{ bookingId, confirmed: true }`.

Errors: 400 validation, 500 internal.

#### POST `/api/bookings/{id}/cancel`

Public participant-cancellation endpoint (no admin session).

Request:

- route `id`: booking GUID
- body `{ token }` where token is compared as exact case-sensitive opaque string

Behavior:

- if booking exists, token matches, and booking is confirmed: set status to cancelled
- if booking exists, token matches, and already cancelled: return success (idempotent)
- if booking missing or token mismatch: return generic 404
- if SMTP enabled, attempt participant cancellation email path; failure is logged and does not fail response

Success response: `{ ok: true }`

### 5.2 Admin auth API

#### POST `/api/admin/login`

- Body: `{ password }`
- Verify against configured admin password hash (PBKDF2).
- Create session token, store in DB, set HttpOnly session cookie.
- Cookie path: `/api/admin`, SameSite Strict, 7-day expiry.
- `Secure` true except in development environment.

#### POST `/api/admin/logout`

- Delete current session if present.
- Clear session cookie.
- Always return success payload.

#### GET `/api/admin/session`

- Return success when session token exists and is not expired.
- Return 401 with reason message when missing/expired/invalid.

### 5.3 Admin protected API

All endpoints below require valid admin session:

- `GET /api/admin/bookings`
  - supports `page`, `pageSize`, `status=confirmed|cancelled`
- `GET /api/admin/bookings/{id}`
- `POST /api/admin/bookings/{id}/cancel`
- `GET /api/admin/dashboard`
- `GET /api/admin/calendars`
- `GET /api/admin/calendars/{id}/history?limit=1..50`
- `POST /api/admin/calendars/{id}/sync`
- `GET /api/admin/availability`
- `PUT /api/admin/availability`
- `GET /api/admin/settings`
- `PUT /api/admin/settings`
- `GET /api/admin/calendar-view?start=<instant>&end=<instant>&tz=<iana>`

Key rules:

- Availability update requires at least one slot.
- Day-of-week must be 1..7.
- Time format must be HH:MM.
- `startTime < endTime`.
- Settings constraints:
  - minNoticeHours >= 0
  - bookingWindowDays >= 1
  - defaultDurationMinutes in [5, 480]
- Calendar-view query params must be valid ISO instants; `start < end`.

### 5.4 Static routes

- Serve booking SPA from `/`.
- Serve booking SPA cancellation deep-link route from `/cancel/{id}/{token}`.
- Serve admin SPA assets from `/admin`.
- Provide SPA fallback for `/admin/{**path}` to `admin/index.html`.

---

## 6. Booking Slot Computation Rules

Compute slots as follows:

1. Convert participant windows to instants.
2. Determine participant date range from earliest start to latest end.
3. Expand host weekly slots into concrete intervals over that date range (host timezone).
4. Intersect each participant window with each expanded host interval.
5. Build blocker intervals as:
   - confirmed bookings in range
   - cached calendar events in range
6. Subtract blockers from each intersected interval.
7. Chunk resulting intervals by requested duration.
8. Drop remainder shorter than full duration.
9. Return slots in participant timezone as offset datetimes.

Interval semantics:

- Adjacent intervals do not overlap.
- Intersection exists only when `start < end`.
- Subtraction may split intervals.

---

## 7. Calendar Sync and CalDAV Behavior

### 7.1 Source configuration

Sources are created from env vars for Fastmail and iCloud independently.
Each source requires URL + username + password.

Source ID must be deterministic from provider/url so it is stable across restarts.

### 7.2 Background sync

- Start sync loop only if at least one source configured.
- Sync immediately on startup, then every 10 minutes.
- Use per-source basic-auth HTTP clients.
- Prevent overlapping sync runs with a lock/semaphore.

### 7.3 Sync range

Default sync range for background sync:

- from now - 30 days
- to now + 60 days

Manual sync uses now to now+60 days.

### 7.4 CalDAV protocol flow

For each source:

1. PROPFIND base URL for `current-user-principal`
2. PROPFIND principal URL for `calendar-home-set`
3. PROPFIND home URL Depth=1 to list calendars
4. Filter calendars supporting VEVENT (or no explicit component set)
5. REPORT calendar-query for each calendar with date range
6. Parse ICS payloads, expand recurring events
7. Normalize to cached events (instants)

All-day events:

- treat as host-timezone full-day blocks from local day start to next local day start.

On sync completion:

- replace cached events for source atomically
- update source status
- append sync history row
- prune history to latest 50 entries per source

---

## 8. Admin Authentication and Session Model

- Password is supplied by environment variable at startup.
- Hash at startup using PBKDF2 SHA-256, 100k iterations, 16-byte salt, 32-byte hash.
- Login verification uses constant-time comparison.
- Session token is random 32-byte URL-safe base64.
- Session duration is 7 days.
- Expired sessions are cleaned up during login and when encountered.

---

## 9. Email Notification Behavior

SMTP is optional.

Enable SMTP only when all required SMTP values are present:

- host, port, username, password, from address

If any are missing, email sending is disabled.

Current required behavior:

- On admin cancellation, attempt to send cancellation email to participant when SMTP enabled.
- On participant cancellation, attempt participant cancellation confirmation email and notify host via BCC copy when SMTP enabled.
- Log send failures but do not fail cancellation API call.

---

## 10. Frontend Behavior (Booking + Admin)

### 10.1 Booking app behavior

Booking UI is a single-page, step-based conversational flow with steps:

1. title
2. duration
3. availability text
4. parsed availability confirmation
5. slot selection
6. contact info
7. final confirmation
8. complete

Behavioral requirements:

- Validate required fields client-side before submitting.
- Support duration presets 15/30/45/60 and optional custom duration.
- Use Enter-to-submit for key steps (including textarea with Shift+Enter exception).
- Keep timezone in model, initialized from browser timezone.
- If timezone changes during availability confirmation or slot step, re-run parse/slot fetch.
- Show error banner for API failures.
- After success, show booking completion state.
- Support `/cancel/:bookingId/:token` route with explicit confirmation UI before calling cancellation API.
- Render generic invalid-link state for cancellation `404` responses.

### 10.2 Admin app behavior

Admin SPA must provide routes/pages:

- Login
- Dashboard
- Bookings list
- Booking detail
- Calendars
- Calendar view
- Availability editor
- Settings

Admin app requirements:

- Check session on startup.
- Redirect/guard authenticated pages.
- Decode/encode all API payloads with strict typed decoders.
- Provide pagination + status filter for bookings.
- Provide manual sync trigger and sync history display.
- Provide weekly availability CRUD (replace-all semantics from API).
- Provide settings save flow.
- Render mixed calendar events (availability, external calendar, bookings).

---

## 11. Static Assets and Browser Bootstrap

Provide two entry HTML files:

- booking index (`/`): mounts Elm booking app and passes browser timezone as flag
- admin index (`/admin`): mounts Elm admin app and passes timezone + currentDate flags

Booking static hosting must support deep-link entry for participant cancellation route (`/cancel/:bookingId/:token`) by serving booking SPA.

Serve compiled JS bundles and generated Tailwind CSS from backend `wwwroot`.

---

## 12. Database Schema and Migration Model

Use SQLite with migration files and checksum validation.

Core tables:

- `bookings`
- `host_availability`
- `calendar_sources`
- `cached_events`
- `scheduling_settings`
- `sync_status`
- `admin_sessions`
- `sync_history`
- `atlas_schema_revisions` (migration bookkeeping)

Migration runner requirements:

- Discover migration files by `<version>_<name>.sql`.
- Verify integrity against `atlas.sum` before applying.
- Apply pending migrations in version order in transactions.
- Record applied migration version and timestamp.

---

## 13. Configuration and Environment Variables

Required at backend startup:

- `MICHAEL_HOST_TIMEZONE`
- `GEMINI_API_KEY`
- `MICHAEL_ADMIN_PASSWORD`

Optional:

- `MICHAEL_DB_PATH` (default `michael.db`)
- SMTP config (`MICHAEL_SMTP_*`)
- CalDAV Fastmail config (`MICHAEL_CALDAV_FASTMAIL_*`)
- CalDAV iCloud config (`MICHAEL_CALDAV_ICLOUD_*`)

Fail-fast requirement:

- Missing required variables must fail startup with clear error.

---

## 14. Build, Development, and CI Requirements

### 14.1 Build outputs

- Compile booking Elm to backend static JS.
- Compile admin Elm to backend static JS.
- Compile Tailwind CSS to backend static stylesheet.
- Build backend and fake-caldav .NET projects.

### 14.2 Dev process

Provide commands/workflow for:

- backend watch run
- booking Elm watch compile
- admin Elm watch compile
- tailwind watch
- fake-caldav run

### 14.3 CI pipeline

CI must run parallel jobs:

- lint (`treefmt --fail-on-change`)
- backend build (`dotnet restore`, `dotnet build`, fake-caldav build)
- frontend checks (Elm compile, elm-review, elm-test)
- backend tests

---

## 15. Testing Requirements

Backend tests must cover at least:

- interval math (`intersect`, `subtract`, `chunk`)
- slot computation scenarios (overlap, blockers, empty windows)
- timezone parsing and validation helpers
- email validation helper
- admin session persistence and expiry cleanup
- booking listing, filtering, cancellation semantics
- migration behavior and checksum validation
- CalDAV parsing/sync and calendar view composition

Frontend tests should cover key decoders and module logic for both Elm apps.

---

## 16. Non-Goals / Explicitly Out of Scope

These features are intentionally not required in the current baseline:

- Multi-user host support
- Distributed/multi-instance DB deployment
- Dedicated rescheduling workflow (cancel + rebook is acceptable)
- Mandatory participant email verification
- Full OIDC/passkey auth (current baseline uses password session auth)
- Guaranteed invite/ICS generation pipeline for every booking

---

## 17. Definition of Done

An implementation is complete when all of the following are true:

1. Public booking flow works end-to-end: parse -> slot fetch -> book.
2. Public participant cancellation flow works end-to-end: email link -> confirm -> cancelled.
3. Admin login/session guarding works and protected endpoints are enforced.
4. Admin dashboard, bookings, calendars, availability, settings, and calendar-view routes function.
5. Background CalDAV sync runs and populates cached events.
6. Slot computation excludes cached calendar events and existing confirmed bookings.
7. SQLite schema + migrations initialize correctly in a fresh environment.
8. Startup fails fast on missing required configuration.
9. Build and CI pipeline pass (lint, build, frontend checks, tests).
10. Static files and SPA fallback are served correctly.
11. Behavior and API contracts match this specification.
