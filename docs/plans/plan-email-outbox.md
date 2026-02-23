# Email Outbox with Retry for Reliable Delivery

This ExecPlan is a living document. The sections Progress, Surprises & Discoveries,
Decision Log, and Outcomes & Retrospective must be kept up to date as work proceeds.

## Purpose / Big Picture

Today, booking confirmation and cancellation emails are sent inline during request handling. If SMTP is temporarily unavailable, the booking/cancellation still succeeds, but the email is lost.

After this change, Michael keeps the current fast path (send inline first) and adds a durable fallback: on inline send failure, the app writes a fully-rendered email record to `email_outbox`. A background worker polls every 30 seconds and retries pending entries with exponential backoff. This guarantees that transient SMTP failures do not permanently drop emails.

Hosts can observe and manage queue health from the admin UI: dashboard stats include pending/failed counts, and a dedicated email queue page lists entries with retry/dismiss actions.

## Progress

- [x] (2026-02-22 23:55Z) Reviewed and corrected plan consistency issues (delivery model, retry semantics, milestone clarity, API surface).
- [ ] (2026-02-22 23:55Z) Milestone 1: Add migration and outbox domain/data types.
- [ ] (2026-02-22 23:55Z) Milestone 2: Implement outbox DB operations and retry math.
- [ ] (2026-02-22 23:55Z) Milestone 3: Implement background outbox worker.
- [ ] (2026-02-22 23:55Z) Milestone 4: Wire handlers to enqueue on inline-send failure.
- [ ] (2026-02-22 23:55Z) Milestone 5: Add admin outbox APIs and Elm UI.
- [ ] (2026-02-22 23:55Z) Milestone 6: Add/adjust tests.
- [ ] (2026-02-22 23:55Z) Milestone 7: Final validation, CI, and documentation cleanup.

## Surprises & Discoveries

- Observation: The previous draft mixed two incompatible designs ("always queue" vs "inline first, queue on failure").
  Evidence: Purpose section and Milestone 4 made contradictory claims.

- Observation: Retry behavior was internally inconsistent in attempt counting and timeline.
  Evidence: Different sections implied different meanings of `attempts` and different total retry windows.

## Decision Log

- Decision: Keep **inline-first send** and use outbox as fallback on failure.
  Rationale: Preserves current low-latency happy path while adding durability for SMTP outages. Also minimizes behavior change to existing handlers/tests.
  Date: 2026-02-22

- Decision: Store fully-rendered email payload in outbox (`to`, `subject`, `body`, optional `bcc`, optional ICS content/method).
  Rationale: Makes retries independent of booking/template lookup and freezes content at time of original action.
  Date: 2026-02-22

- Decision: `attempts` counts **worker send attempts already performed** for that outbox row. It does **not** include the failed inline attempt that created the row.
  Rationale: Keeps row-local retry semantics simple and queryable.
  Date: 2026-02-22

- Decision: Default retry budget is `max_attempts = 5` worker attempts per row with exponential backoff `30s * 2^attempts`.
  Rationale: Covers common transient outages (~15.5 minutes total) without infinite retries.
  Date: 2026-02-22

- Decision: Manual retry of a failed row resets `attempts = 0`, keeps `max_attempts = 5`, sets `status = pending`, `next_retry_at = now`, and clears `last_error`.
  Rationale: This is the simplest mental model for hosts: retry means "start over with a fresh 5-attempt budget." It also simplifies backend logic and UI expectations.
  Date: 2026-02-22

- Decision: Remove dedicated `/api/admin/outbox/summary`; dashboard counts come from existing `/api/admin/dashboard` response.
  Rationale: Single source of truth for dashboard card; avoids redundant API surfaces.
  Date: 2026-02-22

- Decision: Introduce shared payload builders in `Email.fs` and use them for both inline send and outbox enqueue.
  Rationale: Prevents drift between send-path content and queued content.
  Date: 2026-02-22

## Outcomes & Retrospective

(To be filled at major milestones and at completion.)

## Context and Orientation

Relevant backend files:

- `src/backend/Email.fs`: SMTP config, message building, booking/cancellation email composition.
- `src/backend/Handlers.fs`: public booking handler and confirmation notification flow.
- `src/backend/AdminHandlers.fs`: admin cancellation flow and dashboard handler.
- `src/backend/Database.fs`: SQLite access patterns and error-handling conventions.
- `src/backend/CalendarSync.fs`: existing timer + semaphore background worker pattern.
- `src/backend/Program.fs`: startup wiring, route registration, worker lifecycle.
- `src/backend/Migrations.fs` and `src/backend/migrations/`: migration discovery/checksum/apply.
- `src/backend/Michael.fsproj`: F# compile order.

Relevant frontend files:

- `src/frontend/admin/src/Types.elm`: route and DTO-facing type aliases.
- `src/frontend/admin/src/Api.elm`: JSON decoders and admin HTTP calls.
- `src/frontend/admin/src/Page/Dashboard.elm`: dashboard cards.
- `src/frontend/admin/src/Route.elm`, `src/frontend/admin/src/Main.elm`, `src/frontend/admin/src/View/Layout.elm`: routing/page wiring/navigation.

Current code uses inline send (`sendBookingConfirmationEmail` / `sendBookingCancellationEmail`) and swallows email failures to keep booking/cancellation successful. This plan keeps that contract and adds persistence + retries.

## Plan of Work

### Milestone 1: Migration and Outbox Types

Add `src/backend/migrations/20260223100000_add_email_outbox.sql`:

    CREATE TABLE email_outbox (
        id            TEXT PRIMARY KEY,
        to_address    TEXT NOT NULL,
        to_name       TEXT NOT NULL,
        subject       TEXT NOT NULL,
        body          TEXT NOT NULL,
        bcc           TEXT,
        ics_content   TEXT,
        ics_method    TEXT,
        status        TEXT NOT NULL DEFAULT 'pending',
        attempts      INTEGER NOT NULL DEFAULT 0,
        max_attempts  INTEGER NOT NULL DEFAULT 5,
        next_retry_at TEXT NOT NULL,
        last_error    TEXT,
        created_at    TEXT NOT NULL
    );

    CREATE INDEX idx_email_outbox_pending
        ON email_outbox (next_retry_at)
        WHERE status = 'pending';

Regenerate checksum:

    atlas migrate hash --dir "file://src/backend/migrations"

Create `src/backend/EmailOutbox.fs` (`module Michael.EmailOutbox`) with:

- `OutboxStatus = Pending | Failed`
- `OutboxEntry` record matching table schema
- status conversion helpers (`pending`/`failed`)

Update `src/backend/Michael.fsproj` compile order: add `EmailOutbox.fs` **after** `Email.fs` and **before** `RateLimiting.fs`.

### Milestone 2: Outbox DB Operations and Retry Semantics

In `src/backend/EmailOutbox.fs`, implement:

- local DB exception wrappers mirroring `Database.fs` conventions
- `computeNextRetry : Instant -> int -> Instant`
  - formula: `now + 30s * 2^attempts`
- `createOutboxEntryFromPayload`
  - inputs: `id`, `payload`, `now`
  - defaults: `Status=Pending`, `Attempts=0`, `MaxAttempts=5`, `CreatedAt=now`, `LastError=None`, `NextRetryAt=computeNextRetry now 0`
- DB functions:
  - `insertOutboxEntry`
  - `getPendingEntries` (`status='pending' AND next_retry_at <= now`, oldest first)
  - `markSent` (delete row)
  - `markRetry` (set `attempts = attempts + 1`, `next_retry_at`, `last_error`)
  - `markFailed` (set `status='failed'`, `attempts = attempts + 1`, `last_error`)
  - `listOutboxEntries` (newest first)
  - `getOutboxCounts` (pending/failed counts)
  - `retryOutboxEntry` (failed only; set `status='pending'`, `attempts=0`, `max_attempts=5`, `next_retry_at=now`, `last_error=NULL`)
  - `dismissOutboxEntry` (delete failed row)

### Milestone 3: Background Outbox Worker

In `src/backend/EmailOutbox.fs`, add:

    startOutboxWorker :
        createConn:(unit -> SqliteConnection)
        -> smtpConfig:SmtpConfig
        -> clock:IClock
        -> IDisposable

Behavior per tick (30 seconds):

1. Enter `SemaphoreSlim(1,1)` non-blocking to prevent overlap.
2. Query due pending rows.
3. For each row, reconstruct optional ICS and call `Email.sendEmail`.
4. On success: `markSent`.
5. On failure:
   - if `entry.Attempts + 1 >= entry.MaxAttempts` => `markFailed`
   - else `markRetry` with `computeNextRetry now entry.Attempts`
6. Log info/warn/error appropriately.
7. Dispose timer + semaphore in returned `IDisposable`.

### Milestone 4: Inline Send + Outbox Fallback Wiring

Make this flow deterministic and shared.

#### 4.1 Add shared payload model/builders in `src/backend/Email.fs`

Add:

- `type EmailPayload = { ToAddress; ToName; Subject; Body; Bcc; IcsAttachment }`
- `buildConfirmationPayload : NotificationConfig -> Booking -> string option -> EmailPayload`
- `buildCancellationPayload : NotificationConfig -> Booking -> bool -> Instant -> EmailPayload`
- `sendPayload : SmtpConfig -> EmailPayload -> Task<Result<unit,string>>`

Refactor `sendBookingConfirmationEmail` and `sendBookingCancellationEmail` to call payload builders + `sendPayload`.

#### 4.2 Update booking confirmation flow (`src/backend/Handlers.fs`)

Update `sendConfirmationNotification` signature:

    sendConfirmationNotification :
        sendFn:(NotificationConfig -> Booking -> string option -> Task<Result<unit, string>>)
        -> enqueueFn:(EmailPayload -> Result<unit, string>)
        -> notificationConfig:NotificationConfig option
        -> booking:Booking
        -> videoLink:string option
        -> Task<unit>

Behavior:

- If no notification config: debug log and return.
- Otherwise call `sendFn` (inline send).
- On `Ok`: info log.
- On `Error` or thrown/faulted exception:
  - build payload via `buildConfirmationPayload`
  - call `enqueueFn payload`
  - log warning on enqueue success, error on enqueue failure
- Never fault the returned task.

Update `handleBook` signature to accept/pass `enqueueFn`, preserving fire-and-forget behavior.

#### 4.3 Update cancellation flow (`src/backend/AdminHandlers.fs`)

Update `handleCancelBooking` signature to include `enqueueFn:(EmailPayload -> Result<unit,string>)`.

Behavior after successful cancellation:

- Try inline cancellation send via existing `sendFn`.
- On failure or exception, build payload with `buildCancellationPayload` and enqueue.
- Keep HTTP response success semantics unchanged.

#### 4.4 Program wiring (`src/backend/Program.fs`)

Add production `enqueueFn` closure:

- open DB connection
- create outbox row from payload and `clock.GetCurrentInstant()`
- insert row

Wire `enqueueFn` into `handleBook` and `handleCancelBooking`.

Start outbox worker when SMTP is configured:

    let outboxDisposable =
        notificationConfig
        |> Option.map (fun nc -> startOutboxWorker createConn nc.Smtp clock)

Dispose on shutdown with existing disposables.

### Milestone 5: Admin API + Elm UI

#### Backend (`src/backend/AdminHandlers.fs`)

Add DTO:

- `OutboxEntryDto` (id, toAddress, subject, status, attempts, maxAttempts, nextRetryAt, lastError, createdAt)

Add handlers:

- `handleListOutboxEntries : createConn -> HttpHandler` (`GET /api/admin/outbox`)
- `handleRetryOutboxEntry : createConn -> clock -> HttpHandler` (`POST /api/admin/outbox/{id}/retry`)
- `handleDismissOutboxEntry : createConn -> HttpHandler` (`DELETE /api/admin/outbox/{id}`)

Update `DashboardStatsResponse` to include:

- `PendingEmailCount : int`
- `FailedEmailCount : int`

Update `handleDashboard` to fill those counts from outbox.

#### Routes (`src/backend/Program.fs`)

Add behind `requireAdmin`:

- `GET /api/admin/outbox`
- `POST /api/admin/outbox/{id}/retry`
- `DELETE /api/admin/outbox/{id}`

Do **not** add a separate outbox summary endpoint.

#### Elm (`src/frontend/admin/src`)

- `Types.elm`:
  - add `OutboxEntryStatus = OutboxPending | OutboxFailed`
  - add `OutboxEntry` alias
  - extend `DashboardStats` with `pendingEmailCount` and `failedEmailCount`
  - add `EmailQueue` to `Route`
- `Api.elm`:
  - extend `dashboardStatsDecoder` for new counts
  - add decoders/functions for outbox list/retry/dismiss
- `Page/Dashboard.elm`:
  - add Email Queue card with muted healthy state and emphasized warning/error states
  - link to `/admin/email-queue`
- add `Page/EmailQueue.elm`:
  - load list on init
  - table of outbox entries
  - actions for failed rows: Retry, Dismiss
  - past-tense Msg names (`OutboxEntriesReceived`, `RetryClicked`, `RetryCompleted`, etc.)
- `Route.elm`, `Main.elm`, `View/Layout.elm`:
  - wire route/page/sidebar link

### Milestone 6: Tests

Backend tests:

- New `tests/Michael.Tests/EmailOutboxTests.fs`:
  - insert/query/update/delete outbox operations
  - pending filtering by status/time
  - `computeNextRetry` schedule
  - `retryOutboxEntry` semantics (`attempts` resets to 0, `max_attempts` resets to 5)
- Update `tests/Michael.Tests/HandlerTests.fs`:
  - new `enqueueFn` parameter coverage
  - verify enqueue called on send error/exception
  - preserve fire-and-forget behavior for `handleBook`
- Update `tests/Michael.Tests/AdminTests.fs`:
  - cancellation enqueue fallback behavior
  - outbox API handlers (list/retry/dismiss)
  - dashboard response includes email counts

Frontend tests:

- Decoder tests for outbox status/entry and updated dashboard stats.

Project file updates:

- Add new test file(s) to `tests/Michael.Tests/Michael.Tests.fsproj`.

### Milestone 7: Final Validation and Cleanup

- Confirm outbox worker lifecycle/disposal in `Program.fs`.
- Confirm logs use consistent `SourceContext` (`Michael.EmailOutbox`).
- Verify UI states (healthy/pending/failed/mixed).
- Run full local CI and update plan sections.

## Concrete Steps

Run from repo root `/home/bromanko.linux/Code/michael`.

1. Build backend:

       dotnet build src/backend/

2. Run backend tests:

       dotnet run --project tests/Michael.Tests/

3. Build frontend apps (or run project’s frontend CI step if scripted).

4. Run full CI (required):

       selfci check

5. After migration file creation:

       atlas migrate hash --dir "file://src/backend/migrations"

## Validation and Acceptance

Acceptance requires all of the following:

1. `dotnet run --project tests/Michael.Tests/` passes.
2. `selfci check` passes.
3. Happy path: with SMTP healthy, booking confirmation sends immediately and no outbox row remains.
4. SMTP failure path: with SMTP down, booking still returns HTTP 200 and creates pending outbox row.
5. Worker retry path: attempts increment and `next_retry_at` follows 30s, 60s, 120s, 240s, 480s delays; after 5 worker failures row becomes `failed`.
6. Recovery path: restoring SMTP causes pending row delivery and deletion.
7. Cancellation path: failed cancellation emails are enqueued and later delivered by worker.
8. Dashboard shows pending/failed counts from `/api/admin/dashboard`.
9. `/admin/email-queue` lists entries and supports retry/dismiss for failed rows; Retry resets the row to `pending` with `attempts=0`, `max_attempts=5`, and `next_retry_at=now`.
10. `handleBook` remains non-blocking with respect to email sending.

## Idempotence and Recovery

- Migration application is idempotent via `atlas_schema_revisions` tracking.
- Outbox processing is naturally restart-safe: pending rows remain durable until sent/deleted or failed.
- On app restart, worker resumes from persisted queue state.
- Manual retry is safe and explicit; dismiss removes known-unrecoverable failures.

## Artifacts and Notes

Expected state after inline failure at time `T`:

    status='pending', attempts=0, max_attempts=5, next_retry_at=T+30s

Retry schedule example (`attempts` before each worker attempt):

    Worker attempt #1 at T+30s  (attempts=0) fails -> attempts=1, next=T+60s
    Worker attempt #2 at T+90s  (attempts=1) fails -> attempts=2, next=T+120s
    Worker attempt #3 at T+210s (attempts=2) fails -> attempts=3, next=T+240s
    Worker attempt #4 at T+450s (attempts=3) fails -> attempts=4, next=T+480s
    Worker attempt #5 at T+930s (attempts=4) fails -> status='failed', attempts=5

Total retry window after enqueue: about 15.5 minutes (plus timer jitter).

## Interfaces and Dependencies

No new NuGet dependencies are required.

### New/updated backend interfaces

In `src/backend/Email.fs`:

    type EmailPayload =
        { ToAddress: string
          ToName: string
          Subject: string
          Body: string
          Bcc: string option
          IcsAttachment: IcsAttachment option }

    val buildConfirmationPayload:
        NotificationConfig -> Booking -> string option -> EmailPayload

    val buildCancellationPayload:
        NotificationConfig -> Booking -> bool -> Instant -> EmailPayload

    val sendPayload:
        SmtpConfig -> EmailPayload -> Task<Result<unit, string>>

In `src/backend/EmailOutbox.fs`:

    type OutboxStatus = Pending | Failed

    type OutboxEntry =
        { Id: Guid
          ToAddress: string
          ToName: string
          Subject: string
          Body: string
          Bcc: string option
          IcsContent: string option
          IcsMethod: string option
          Status: OutboxStatus
          Attempts: int
          MaxAttempts: int
          NextRetryAt: Instant
          LastError: string option
          CreatedAt: Instant }

    val createOutboxEntryFromPayload:
        id:Guid -> payload:EmailPayload -> now:Instant -> OutboxEntry

    val computeNextRetry: now:Instant -> attempts:int -> Instant

    val insertOutboxEntry: conn:SqliteConnection -> entry:OutboxEntry -> Result<unit, string>
    val getPendingEntries: conn:SqliteConnection -> now:Instant -> OutboxEntry list
    val markSent: conn:SqliteConnection -> id:Guid -> Result<unit, string>
    val markRetry: conn:SqliteConnection -> id:Guid -> nextRetryAt:Instant -> error:string -> Result<unit, string>
    val markFailed: conn:SqliteConnection -> id:Guid -> error:string -> Result<unit, string>
    val listOutboxEntries: conn:SqliteConnection -> OutboxEntry list
    val getOutboxCounts: conn:SqliteConnection -> pending:int * failed:int
    val retryOutboxEntry: conn:SqliteConnection -> id:Guid -> now:Instant -> Result<unit, string>
    val dismissOutboxEntry: conn:SqliteConnection -> id:Guid -> Result<unit, string>

    val startOutboxWorker:
        createConn:(unit -> SqliteConnection)
        -> smtpConfig:SmtpConfig
        -> clock:IClock
        -> IDisposable

In `src/backend/Handlers.fs`:

    val sendConfirmationNotification:
        sendFn:(NotificationConfig -> Booking -> string option -> Task<Result<unit, string>>)
        -> enqueueFn:(EmailPayload -> Result<unit, string>)
        -> notificationConfig:NotificationConfig option
        -> booking:Booking
        -> videoLink:string option
        -> Task<unit>

In `src/backend/AdminHandlers.fs`:

    val handleCancelBooking:
        createConn:(unit -> SqliteConnection)
        -> clock:IClock
        -> notificationConfig:NotificationConfig option
        -> sendFn:(NotificationConfig -> Booking -> bool -> Instant -> Task<Result<unit, string>>)
        -> enqueueFn:(EmailPayload -> Result<unit, string>)
        -> deleteCalDavFn:(Booking -> Task<unit>)
        -> HttpHandler

### New/updated admin routes

    GET    /api/admin/outbox
    POST   /api/admin/outbox/{id}/retry
    DELETE /api/admin/outbox/{id}

Dashboard remains:

    GET    /api/admin/dashboard   // now includes pendingEmailCount, failedEmailCount

## Revision Note (2026-02-22 23:55Z)

This revision resolves plan-review findings by making the architecture unambiguous (inline send first, outbox fallback on failure), defining retry/attempt semantics precisely, removing conflicting API designs (no separate outbox summary endpoint), making Milestone 4 prescriptive with one implementation path, adding explicit SQL statement terminators in the migration, and introducing shared email payload builders to prevent content drift between inline sends and queued retries.
