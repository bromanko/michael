# Michael Runtime and Delivery Specification (NL Spec)

Defines runtime configuration, background processes, build outputs, and CI expectations.

---

## 1. Runtime Composition

A production-capable runtime includes:

- HTTP backend serving APIs and static frontend assets
- SQLite database file
- Optional background calendar sync loop (when sources configured)
- Optional SMTP integration for notification emails

---

## 2. Environment Configuration

### 2.1 Required variables (must fail startup if missing)

- `MICHAEL_HOST_TIMEZONE`
- `GEMINI_API_KEY`
- `MICHAEL_ADMIN_PASSWORD`

### 2.2 Optional variables

Database:

- `MICHAEL_DB_PATH` (default `michael.db`)

SMTP (all-or-nothing enablement):

- `MICHAEL_SMTP_HOST`
- `MICHAEL_SMTP_PORT`
- `MICHAEL_SMTP_USERNAME`
- `MICHAEL_SMTP_PASSWORD`
- `MICHAEL_SMTP_FROM`
- `MICHAEL_SMTP_FROM_NAME` (optional display-name default)

CalDAV Fastmail source (independent optional source):

- `MICHAEL_CALDAV_FASTMAIL_URL`
- `MICHAEL_CALDAV_FASTMAIL_USERNAME`
- `MICHAEL_CALDAV_FASTMAIL_PASSWORD`

CalDAV iCloud source (independent optional source):

- `MICHAEL_CALDAV_ICLOUD_URL`
- `MICHAEL_CALDAV_ICLOUD_USERNAME`
- `MICHAEL_CALDAV_ICLOUD_PASSWORD`

### 2.3 Optional integration partial-config behavior (normative)

- SMTP:
  - if any required SMTP field is missing or invalid, SMTP is disabled (no startup failure)
  - runtime MUST log that email notifications are disabled
- CalDAV provider configs (Fastmail/iCloud):
  - each provider is evaluated independently
  - if provider config is partial/missing, that provider is disabled (no startup failure)
  - runtime MUST log that provider is not configured
- Required core variables remain fail-fast.

---

## 3. Startup Sequence

On backend startup:

1. Initialize logging early.
2. Resolve required configuration and fail fast if invalid/missing.
3. Open DB connection and run migrations.
4. Seed default host availability if table empty.
5. Configure optional integrations (SMTP, CalDAV sources).
6. Register CalDAV sources in DB.
7. Start background sync if sources exist.
8. Register API routes and static/SPA routes.
9. Start web server.

---

## 4. Background Calendar Sync Runtime

When at least one source exists:

- Start immediate sync at startup.
- Repeat every 10 minutes.
- Disallow overlapping sync execution.
- Sync horizon:
  - background sync window: `now - 30 days` to `now + 60 days`
  - manual sync window: `now` to `now + 60 days`
- For each source:
  - sync events
  - replace source cache transactionally
  - update sync status
  - record sync history
  - prune history to recent 50 entries

If no sources configured:

- do not start sync timer
- log informative message

---

## 5. Notification Runtime Behavior

- SMTP is optional as defined above.
- On admin booking cancellation, if SMTP is enabled, the system MUST attempt to send cancellation email to the participant.
- Email send failure MUST NOT fail cancellation API response.
- Email failures MUST be logged.

---

## 6. Build Outputs

The build pipeline must generate:

- backend executable
- booking frontend JS bundle in backend static directory
- admin frontend JS bundle in backend static directory
- Tailwind CSS output in backend static directory
- fake-caldav executable for local integration testing/dev

---

## 7. Local Development Workflow

Provide a working dev loop including:

- backend watch run
- booking frontend incremental build/watch
- admin frontend incremental build/watch
- tailwind watch
- optional fake-caldav process

A process manager (or equivalent) should be able to run these concurrently.

---

## 8. CI Requirements

CI should execute parallel jobs with explicit pass/fail reporting:

1. **lint**
   - tree formatting checks
2. **build**
   - backend restore/build
   - fake-caldav build
3. **frontend**
   - booking compile
   - admin compile
   - tailwind compilation
   - elm-review checks
   - elm tests
4. **test**
   - backend automated tests

All jobs must pass for a green build.

---

## 9. Deployment Constraints

- SQLite is baseline persistence; assume single-instance runtime semantics.
- Admin auth is cookie-session based; deployment must preserve cookie behavior.
- TLS should be used in non-development environments; secure cookie behavior expected outside dev.

---

## 10. Runtime Definition of Done

An implementation satisfies this spec when:

1. Startup sequence reliably initializes or fails clearly.
2. Optional integration partial-config behavior matches this spec.
3. Background sync behaves correctly under optional-source conditions and defined sync horizons.
4. Cancellation email behavior matches this spec.
5. Build artifacts are generated and served correctly.
6. CI verifies formatting, builds, frontend checks, and tests.
7. Runtime behavior remains consistent across restarts with persistent SQLite state.
