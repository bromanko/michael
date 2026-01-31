# Admin Dashboard — Implementation Plan

## Overview

The admin dashboard is the host-facing management interface for Michael. It is a
separate Elm single-page application served at `/admin/` that lets the host
(single user, self-hosted) manage their calendar sources, view and adjust
availability, manage bookings, configure scheduling settings, and monitor
calendar sync status.

Michael is a single-user, self-hosted tool. The admin dashboard is the only way
for the host to configure calendars, set availability rules, block off time, and
review bookings. Without it the host has no visibility into what Michael is doing
with their calendars.

---

## Features by Phase

### Phase 1 — Foundation (auth, skeleton, bookings list)

- Session-based authentication (see Authentication section)
- App shell: sidebar navigation, top bar, responsive layout
- **Bookings list**: view all bookings (upcoming and past), with status
  (confirmed / cancelled), participant info, time, and duration
- **Booking detail**: view full details of a single booking; cancel a booking
- **Dashboard home**: summary stats (upcoming bookings count, next booking)

### Phase 2 — Availability & calendar management

- **Calendar sources**: list connected CalDAV calendar sources with sync status
  (last synced, success/error)
- **Add calendar source**: form to add a new CalDAV source (provider, base URL,
  credentials)
- **Remove calendar source**: delete a calendar source and its cached events
- **Trigger sync**: manually re-sync a calendar source
- **Host availability rules**: view and edit weekly recurring availability
  windows (day of week, start time, end time, timezone)
- **Manual time blocks**: create, view, and delete one-off time blocks that
  override availability (block specific times)

### Phase 3 — Calendar view & settings

- **Merged calendar view**: week view showing calendar events from all sources,
  manual blocks, and booked Michael meetings overlaid together
- **Scheduling settings**: configure minimum scheduling notice (hours), booking
  window (how far out), default meeting duration
- **Booking cancellation email**: when host cancels via dashboard, send
  notification email to participant (requires SMTP integration)

### Phase 4 — Polish & advanced features

- **Sync health monitoring**: display sync errors, retry controls
- **Booking search/filter**: filter bookings by date range, status, participant
- **Video conferencing link config**: set default Zoom/Meet link for bookings
- **Export**: export bookings as CSV

---

## Architecture

### Elm app structure

The admin app follows the same patterns as the booking app but uses
`Browser.application` for client-side routing (multiple pages) instead of
`Browser.element` (single widget).

```
src/frontend/admin/
  elm.json
  src/
    Main.elm          -- Browser.application entry point, routing
    Types.elm         -- Shared types (Booking, CalendarSource, etc.)
    Route.elm         -- URL parsing and route types
    Session.elm       -- Auth session state, token management
    Api.elm           -- All HTTP requests to /api/admin/*
    Page/
      Dashboard.elm   -- Home page with summary stats
      Bookings.elm    -- Bookings list view
      BookingDetail.elm -- Single booking detail + cancel
      Calendars.elm   -- Calendar sources list + sync status
      CalendarAdd.elm -- Add new calendar source form
      Availability.elm -- Host availability rules editor
      TimeBlocks.elm  -- Manual time blocks manager
      Settings.elm    -- Scheduling settings editor
      Login.elm       -- Login page
    View/
      Layout.elm      -- Sidebar, top bar, page wrapper
      Components.elm  -- Reusable view helpers (buttons, tables, cards)
```

### Module conventions (matching booking app)

- **Types.elm** holds all shared type aliases and custom types. Page modules
  should not define types referenced across module boundaries.
- **Msg variants use past tense**: `BookingsCancellationConfirmed`,
  `CalendarSourceAdded`, `AvailabilitySlotSaved`, `SyncTriggered`.
- **Explicit imports** everywhere. No `exposing (..)` except `Msg(..)` in
  view modules.
- Each `Page/*.elm` module exports its own `Model`, `Msg`, `init`, `update`,
  and `view`. `Main.elm` composes them.

### State management

```
Main.Model =
    { session : Session          -- auth state (token, logged-in flag)
    , route : Route              -- current page route
    , page : PageModel           -- union of all page models
    , navOpen : Bool             -- mobile sidebar toggle
    }

type PageModel
    = DashboardPage Dashboard.Model
    | BookingsPage Bookings.Model
    | BookingDetailPage BookingDetail.Model
    | CalendarsPage Calendars.Model
    | CalendarAddPage CalendarAdd.Model
    | AvailabilityPage Availability.Model
    | TimeBlocksPage TimeBlocks.Model
    | SettingsPage Settings.Model
    | LoginPage Login.Model
    | NotFoundPage
```

### Routing

Use `elm/url` for URL parsing. Routes:

| Path                        | Page              |
|-----------------------------|-------------------|
| `/admin/`                   | Dashboard         |
| `/admin/bookings`           | Bookings list     |
| `/admin/bookings/:id`       | Booking detail    |
| `/admin/calendars`          | Calendar sources  |
| `/admin/calendars/add`      | Add calendar      |
| `/admin/availability`       | Availability rules|
| `/admin/time-blocks`        | Manual blocks     |
| `/admin/settings`           | Settings          |
| `/admin/login`              | Login             |

---

## Backend API

All admin endpoints live under `/api/admin/` and require authentication. The
booking API endpoints (`/api/parse`, `/api/slots`, `/api/book`) remain
unauthenticated for participants.

### Authentication endpoints

| Method | Path                  | Description                       |
|--------|-----------------------|-----------------------------------|
| POST   | `/api/admin/login`    | Authenticate with password        |
| POST   | `/api/admin/logout`   | Invalidate session                |
| GET    | `/api/admin/session`  | Check if current session is valid |

### Bookings endpoints

| Method | Path                         | Description                    |
|--------|------------------------------|--------------------------------|
| GET    | `/api/admin/bookings`        | List bookings (with pagination, filter by status) |
| GET    | `/api/admin/bookings/:id`    | Get single booking             |
| POST   | `/api/admin/bookings/:id/cancel` | Cancel a booking           |

### Calendar source endpoints

| Method | Path                                | Description                  |
|--------|-------------------------------------|------------------------------|
| GET    | `/api/admin/calendars`              | List calendar sources        |
| POST   | `/api/admin/calendars`              | Add a new calendar source    |
| DELETE | `/api/admin/calendars/:id`          | Remove a calendar source     |
| POST   | `/api/admin/calendars/:id/sync`     | Trigger manual sync          |

### Availability endpoints

| Method | Path                                    | Description                   |
|--------|-----------------------------------------|-------------------------------|
| GET    | `/api/admin/availability`               | Get host availability rules   |
| PUT    | `/api/admin/availability`               | Replace all availability rules|
| GET    | `/api/admin/time-blocks`                | List manual time blocks       |
| POST   | `/api/admin/time-blocks`                | Create a time block           |
| DELETE | `/api/admin/time-blocks/:id`            | Delete a time block           |

### Settings endpoints

| Method | Path                      | Description                     |
|--------|---------------------------|---------------------------------|
| GET    | `/api/admin/settings`     | Get scheduling settings         |
| PUT    | `/api/admin/settings`     | Update scheduling settings      |

### Merged calendar view endpoint

| Method | Path                        | Description                                  |
|--------|-----------------------------|----------------------------------------------|
| GET    | `/api/admin/calendar-view`  | Get merged events for a date range (query: `start`, `end`) |

### Backend module organization

New F# modules:

- **AdminAuth.fs** — session creation, validation, middleware
- **AdminHandlers.fs** — all `/api/admin/*` route handlers
- **AdminDtos.fs** — request/response DTOs for admin endpoints (kept separate
  from the booking DTOs in Handlers.fs)

Existing modules that need changes:

- **Database.fs** — new queries: list bookings with pagination, get booking by
  ID, update booking status, CRUD for time blocks, CRUD for scheduling settings,
  CRUD for calendar sources, session storage
- **Domain.fs** — new types: `ManualTimeBlock`, `SchedulingSettings`, `Session`
- **Program.fs** — register admin routes, configure auth middleware, serve
  admin static files

### New domain types (Domain.fs)

```fsharp
type ManualTimeBlock =
    { Id: Guid
      StartInstant: Instant
      EndInstant: Instant
      Reason: string option
      CreatedAt: Instant }

type SchedulingSettings =
    { MinNoticeHours: int           // default 6
      BookingWindowDays: int        // default 30
      DefaultDurationMinutes: int   // default 30
      VideoLink: string option }

type AdminSession =
    { Token: string
      CreatedAt: Instant
      ExpiresAt: Instant }
```

### New database tables

```sql
CREATE TABLE IF NOT EXISTS manual_time_blocks (
    id             TEXT PRIMARY KEY,
    start_instant  TEXT NOT NULL,
    end_instant    TEXT NOT NULL,
    start_epoch    INTEGER NOT NULL,
    end_epoch      INTEGER NOT NULL,
    reason         TEXT,
    created_at     TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS scheduling_settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS admin_sessions (
    token      TEXT PRIMARY KEY,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL
);
```

---

## Authentication

Michael is a self-hosted, single-user tool. The simplest secure approach:

### Phase 1: Password-based auth

- A single admin password is configured via environment variable
  (`MICHAEL_ADMIN_PASSWORD`). The app fails to start if this is not set.
- `POST /api/admin/login` accepts `{ "password": "..." }`, compares against
  the configured password (using constant-time comparison), and returns a
  session token.
- The session token is stored in the `admin_sessions` table with an expiry
  (e.g., 7 days). The token is returned as an `HttpOnly`, `Secure`, `SameSite=Strict`
  cookie named `michael_session`.
- All `/api/admin/*` endpoints (except `/api/admin/login`) check for a valid
  session cookie via middleware.
- `POST /api/admin/logout` deletes the session from the database and clears
  the cookie.
- The Elm app checks `GET /api/admin/session` on init. If the session is
  invalid, redirect to the login page.

### Future: Passkey or OIDC

The design doc mentions Passkey (WebAuthn) or OIDC as auth options. These can
be added later as an upgrade to the password-based approach. The session
infrastructure (cookie, middleware, session table) remains the same; only the
login flow changes.

### Configuration

Add to `Program.fs` startup:

```fsharp
let adminPassword =
    Environment.GetEnvironmentVariable("MICHAEL_ADMIN_PASSWORD")
    |> Option.ofObj
    |> Option.defaultWith (fun () ->
        failwith "MICHAEL_ADMIN_PASSWORD environment variable is required.")
```

Add to `flake.nix` dev shell env:

```nix
MICHAEL_ADMIN_PASSWORD = "dev-password";
```

---

## Styling

### Approach

The admin dashboard shares the existing Tailwind CSS setup and color palette
(sand, coral) from the booking app. A single `tailwind.config.js` covers both
apps.

### Changes to tailwind.config.js

Add the admin Elm source files to the `content` array:

```js
content: [
  "./src/frontend/booking/src/**/*.elm",
  "./src/frontend/admin/src/**/*.elm",
  "./src/backend/wwwroot/index.html",
  "./src/backend/wwwroot/admin/index.html",
],
```

### Admin CSS

Create `src/frontend/styles/admin.css` with the same Tailwind directives:

```css
@tailwind base;
@tailwind components;
@tailwind utilities;
```

Alternatively, since both apps use the same Tailwind config and directives,
a single CSS build could output one `styles.css` that covers both. The simpler
approach is one CSS file shared by both apps. The existing `booking.css` can be
renamed to `app.css` or left as-is, with both HTML files referencing the same
output.

**Recommended**: keep a single CSS build producing one `styles.css`. Update
`tailwind.config.js` content paths to include both Elm apps. No separate CSS
file needed for admin.

### Admin HTML shell

Create `src/backend/wwwroot/admin/index.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Michael Admin</title>
    <link rel="stylesheet" href="/styles.css" />
  </head>
  <body class="bg-sand-100 text-sand-900 min-h-screen">
    <div id="elm-app" class="min-h-screen"></div>
    <script src="/admin/admin.js"></script>
    <script>
      var app = Elm.Main.init({
        node: document.getElementById("elm-app"),
        flags: {
          timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        },
      });
    </script>
  </body>
</html>
```

### Design language

- Reuse the same sand/coral color palette
- Sidebar navigation with sand-800 background
- Content area with sand-50/sand-100 background
- Tables and cards for data display
- Same font stack (Styrene A, Tiempos Headline)

---

## Build & Dev

### Makefile additions

```makefile
admin:
	cd src/frontend/admin && elm make src/Main.elm --output=../../backend/wwwroot/admin/admin.js

all: frontend admin css backend
```

### Procfile additions

Add a watcher for the admin Elm app:

```
admin-elm: cd src/frontend/admin && elm make src/Main.elm --output=../../backend/wwwroot/admin/admin.js && inotifywait -m -r -e modify,create,delete src/ | while read -r; do sleep 0.3; elm make src/Main.elm --output=../../backend/wwwroot/admin/admin.js; done
```

### Backend static files

The backend already uses `UseStaticFiles()` and `UseDefaultFiles()` which
serves files from `wwwroot/`. The admin HTML file at
`wwwroot/admin/index.html` will be served at `/admin/` automatically via
`UseDefaultFiles`.

However, for client-side routing to work (e.g., `/admin/bookings` should serve
`admin/index.html`), a fallback route is needed. Add a catch-all handler in
`Program.fs`:

```fsharp
// After all API routes, add a fallback for admin SPA routing
wapp.MapFallback("/admin/{**path}", fun ctx ->
    ctx.Response.SendFileAsync(
        Path.Combine(wapp.Environment.WebRootPath, "admin", "index.html")))
```

### elm.json for admin

Create `src/frontend/admin/elm.json`:

```json
{
  "type": "application",
  "source-directories": ["src"],
  "elm-version": "0.19.1",
  "dependencies": {
    "direct": {
      "NoRedInk/elm-json-decode-pipeline": "1.0.1",
      "elm/browser": "1.0.2",
      "elm/core": "1.0.5",
      "elm/html": "1.0.0",
      "elm/http": "2.0.0",
      "elm/json": "1.1.3",
      "elm/url": "1.0.0",
      "elm/time": "1.0.0"
    },
    "indirect": {
      "elm/bytes": "1.0.8",
      "elm/file": "1.0.5",
      "elm/virtual-dom": "1.0.3"
    }
  },
  "test-dependencies": {
    "direct": {},
    "indirect": {}
  }
}
```

Note: the admin app needs `elm/url` as a direct dependency (for
`Browser.application` URL parsing) and `elm/time` for displaying timestamps.

---

## Data Flow

### How the admin interacts with existing systems

```
┌─────────────┐     HTTP/JSON      ┌──────────────┐
│  Admin Elm   │ ◄──────────────► │  F# Backend   │
│  (browser)   │                   │  (Falco)      │
└─────────────┘                   └───────┬───────┘
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    │                     │                     │
              ┌─────▼─────┐    ┌─────────▼────────┐   ┌───────▼──────┐
              │  SQLite    │    │  CalDAV servers   │   │  Fastmail    │
              │  Database  │    │  (Fastmail,       │   │  SMTP        │
              │            │    │   iCloud, etc.)   │   │  (email)     │
              └────────────┘    └──────────────────┘   └──────────────┘
```

1. **Bookings**: Admin fetches bookings from the database via
   `/api/admin/bookings`. Cancellation updates the booking status in the DB
   and (Phase 3) sends an email notification.

2. **Calendar sources**: Admin adds/removes CalDAV source configs in the DB.
   The background sync timer (`CalendarSync.startBackgroundSync`) picks up
   sources from the DB. Manual sync triggers an immediate sync for one source.

3. **Availability**: Host availability rules are stored in the
   `host_availability` table. The admin edits them. The `computeSlots` function
   in `Availability.fs` reads them when computing slots for participants.

4. **Manual time blocks**: Stored in `manual_time_blocks` table. When computing
   slots, they are loaded alongside calendar blockers and passed to
   `computeSlots` as additional blocker intervals.

5. **Calendar view**: The merged view endpoint queries `cached_events` (from
   CalDAV sync), `bookings`, and `manual_time_blocks` for the requested date
   range and returns them all for the frontend to render.

6. **Settings**: Stored as key-value pairs in `scheduling_settings`. Read at
   request time when computing available slots (min notice, booking window).

---

## Implementation Phases — Detailed

### Phase 1: Foundation

**Goal**: A working admin app with auth, navigation shell, and bookings
management.

**Deliverables**:

1. **Admin Elm app scaffold**
   - Create `src/frontend/admin/` directory and `elm.json`
   - `Main.elm` with `Browser.application`, routing, session check
   - `Route.elm` with URL parser
   - `Session.elm` for auth state
   - `View/Layout.elm` for sidebar + content wrapper
   - `Page/Login.elm` for password login form

2. **Auth backend**
   - `MICHAEL_ADMIN_PASSWORD` env var (fail-fast if missing)
   - `admin_sessions` table in `Database.fs`
   - `AdminAuth.fs` module: login handler, session validation middleware,
     logout handler, session check handler
   - Session cookie (`HttpOnly`, `Secure`, `SameSite=Strict`)

3. **Bookings API + pages**
   - `GET /api/admin/bookings` — list with pagination (`?page=1&pageSize=20&status=confirmed`)
   - `GET /api/admin/bookings/:id` — single booking
   - `POST /api/admin/bookings/:id/cancel` — cancel (update status)
   - Database queries: `listBookings`, `getBookingById`, `cancelBooking`
   - `Page/Bookings.elm` — table view with status badge, pagination
   - `Page/BookingDetail.elm` — detail card with cancel button

4. **Dashboard home**
   - `GET /api/admin/dashboard` — summary stats (upcoming count, next booking time)
   - `Page/Dashboard.elm` — simple stats display

5. **Build system**
   - `Makefile` target for admin Elm compilation
   - `Procfile` watcher for admin Elm source
   - `tailwind.config.js` content paths updated
   - `wwwroot/admin/index.html` created
   - SPA fallback route in `Program.fs`

**Backend files to create/modify**:
- Create: `src/backend/AdminAuth.fs`, `src/backend/AdminHandlers.fs`
- Modify: `src/backend/Domain.fs`, `src/backend/Database.fs`,
  `src/backend/Program.fs`, `src/backend/Michael.fsproj`

### Phase 2: Availability & calendar management

**Goal**: Host can manage their calendar sources and availability rules.

**Deliverables**:

1. **Calendar sources pages**
   - `Page/Calendars.elm` — list sources with name, provider, last sync time,
     status indicator (green/red/gray)
   - `Page/CalendarAdd.elm` — form: provider dropdown (Fastmail, iCloud),
     base URL, username, password
   - Delete confirmation modal
   - Manual sync button with loading state

2. **Calendar source API**
   - Existing `upsertCalendarSource` in Database.fs can be reused
   - New queries: `listCalendarSources`, `deleteCalendarSource`,
     `getCalendarSource`
   - Credential storage: for now, store credentials in the DB (encrypted at
     rest is a future improvement). CalDAV username/password go in a new
     `calendar_credentials` table, referenced by source ID.
   - Manual sync endpoint triggers `CalDav.syncSource` for a single source

3. **Availability rules pages**
   - `Page/Availability.elm` — grid editor showing weekly schedule (Mon-Fri
     with start/end time per day). Edit inline or via modal.
   - `PUT /api/admin/availability` replaces all rules (simpler than
     individual CRUD)

4. **Manual time blocks**
   - New `manual_time_blocks` table and domain type
   - `Page/TimeBlocks.elm` — list view + add form (date, start time,
     end time, optional reason)
   - Delete a time block
   - Integrate into `computeSlots`: load manual blocks as additional
     blocker intervals

**Database changes**:
- New table: `manual_time_blocks`
- New table: `calendar_credentials` (source_id, username, password)
- New queries for all CRUD operations

### Phase 3: Calendar view & settings

**Goal**: Unified calendar visualization and scheduling configuration.

**Deliverables**:

1. **Merged calendar view**
   - `GET /api/admin/calendar-view?start=...&end=...` returns:
     - Cached CalDAV events (from `cached_events`)
     - Bookings (from `bookings`)
     - Manual time blocks (from `manual_time_blocks`)
     - Host availability windows (expanded for the range)
   - Each event type gets a different color/style in the UI
   - Week view with day columns, time rows
   - Navigate between weeks

2. **Settings page**
   - `Page/Settings.elm` — form with:
     - Minimum scheduling notice (hours, default 6)
     - Booking window (days, default 30)
     - Default meeting duration (minutes, default 30)
     - Video conferencing link (optional URL)
   - `scheduling_settings` table (key-value)
   - Backend reads settings when computing slots, enforcing min notice and
     booking window cutoff

3. **Cancellation email**
   - When the host cancels a booking via the admin, send a cancellation
     email to the participant
   - Requires SMTP integration (Fastmail SMTP, already in the tech stack)
   - New module: `Email.fs` — send email via SMTP
   - Environment variables: `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`,
     `SMTP_PASSWORD`, `SMTP_FROM`

### Phase 4: Polish & advanced

**Goal**: Quality-of-life improvements.

**Deliverables**:

1. Booking search and date-range filtering
2. Sync health monitoring with error details and retry
3. Video conferencing link management
4. CSV export of bookings
5. Responsive mobile layout refinements

---

## Conventions Reminder

These conventions from `CLAUDE.md` apply to all implementation work:

### F#

- Use `task { }` for all async I/O, not `async { }`.
- Inject configuration — no reading env vars in library modules. All config
  reading in `Program.fs`.
- Use `Result<'T, string>` for operations that can fail. No `try/with` for
  control flow.

### Elm

- **Explicit imports** — never `exposing (..)`. Exception: `Msg(..)` in view
  modules.
- **Past-tense Msg variants**: `BookingCancelled`, `CalendarSourceAdded`,
  `AvailabilityRuleSaved`, `SessionChecked`.
- **`case` expressions on custom types**, not `==`.
- All shared types in `Types.elm`.

### General

- Keep code simple. Avoid over-engineering.
- Follow existing patterns in the codebase.
- Fail fast on missing configuration.
