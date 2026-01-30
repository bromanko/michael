# Booking Page Vertical Slice — Implementation Plan

## Goal

Build a working end-to-end booking flow: participant visits a page, types
natural language availability in a chat-like UI, backend parses via Gemini API,
shows confirmation, computes overlap with (stubbed) host availability,
participant selects a slot and confirms, booking is stored in SQLite.

**Deferred**: Calendar sync, email notifications, auth, admin dashboard,
screenshot parsing, rate limiting, deployment.

---

## Decisions

- **Stateless backend** — Elm frontend accumulates parsed fields across turns.
  Each `/api/parse` call sends all user messages concatenated. No server-side
  session management.
- **Donald** for DB access (already in project deps).
- **NodaTime** (v3.3.0) for all date/time/timezone handling. Use
  `NodaTime.Serialization.SystemTextJson` for JSON serialization. Use IANA
  timezone IDs throughout (matches Gemini output and browser
  `Intl.DateTimeFormat`).
- **Expecto** for unit testing, with a separate test project.
- **Makefile** at repo root for build targets (backend, frontend, CSS, test).
  Nix build can wrap it later.

---

## Phase 1: F# Backend Scaffolding

Create the project structure and get `dotnet run` serving a hello-world
response.

### Files

- `src/backend/Michael.fsproj` — .NET 9 web project
  - NuGet deps: `Falco`, `Donald`, `Microsoft.Data.Sqlite`, `FSharp.Core`,
    `NodaTime`, `NodaTime.Serialization.SystemTextJson`
  - Compile order: Domain.fs → Database.fs → Availability.fs →
    GeminiClient.fs → Handlers.fs → Program.fs
- `src/backend/Domain.fs` — Core types:
  - `AvailabilityWindow` — Start/End as `NodaTime.OffsetDateTime`,
    Timezone as `DateTimeZone option`
  - `ParseResult` (mirrors spike schema: windows, duration, title, desc,
    name, email, phone, missingFields)
  - `Booking` record, `BookingStatus` DU
  - `TimeSlot` record for overlap results
- `src/backend/Program.fs` — Falco app entry point, static files, routing.
  Configure `System.Text.Json` with NodaTime serializers.

### Verification

`cd src/backend && dotnet run` starts and responds on localhost.

---

## Phase 2: Test Project Setup

### Files

- `tests/Michael.Tests/Michael.Tests.fsproj` — .NET 9 console project
  - NuGet deps: `Expecto`, `NodaTime`, `NodaTime.Testing`
  - Project reference to `src/backend/Michael.fsproj`
  - Compile order: AvailabilityTests.fs → GeminiClientTests.fs →
    DatabaseTests.fs → Program.fs
- `tests/Michael.Tests/Program.fs` — Expecto test runner entry point
  (`[<EntryPoint>] let main argv = runTestsInAssemblyWithCLIArgs ...`)

Tests are written alongside each phase below. `NodaTime.Testing` provides
`FakeClock` for deterministic time-dependent tests.

### Verification

`cd tests/Michael.Tests && dotnet run` runs tests (initially empty, passes).

---

## Phase 3: SQLite Database Layer

### Schema

```sql
-- bookings table
CREATE TABLE IF NOT EXISTS bookings (
    id                TEXT PRIMARY KEY,
    participant_name  TEXT NOT NULL,
    participant_email TEXT NOT NULL,
    participant_phone TEXT,
    title             TEXT NOT NULL,
    description       TEXT,
    start_time        TEXT NOT NULL,  -- ISO-8601 with offset
    end_time          TEXT NOT NULL,
    duration_minutes  INTEGER NOT NULL,
    timezone          TEXT NOT NULL,  -- IANA timezone ID
    status            TEXT NOT NULL DEFAULT 'confirmed',
    created_at        TEXT NOT NULL DEFAULT (datetime('now'))
);

-- host_availability (stub: seeded with Mon-Fri 9-17 ET)
CREATE TABLE IF NOT EXISTS host_availability (
    id         TEXT PRIMARY KEY,
    day_of_week INTEGER NOT NULL,  -- 1=Mon..7=Sun (ISO)
    start_time TEXT NOT NULL,       -- HH:MM local
    end_time   TEXT NOT NULL,
    timezone   TEXT NOT NULL        -- IANA timezone ID
);
```

### Files

- `src/backend/Database.fs`
  - `initializeDatabase` — create tables, seed host availability if empty
  - `insertBooking` — store a confirmed booking
  - `getHostAvailability` — read host slots
  - `getBookingsInRange` — existing bookings for conflict detection
- `tests/Michael.Tests/DatabaseTests.fs` — tests using in-memory SQLite

### Verification

App starts, creates `michael.db`, tables exist. Database tests pass.

---

## Phase 4: Gemini API Integration

Port the spike's parser to F#.

### Files

- `src/backend/GeminiClient.fs`
  - `buildSystemPrompt` — port from `spike/parser.py` lines 108-221
    (reference datetime, date resolution rules, extraction schema).
    Use `NodaTime.ZonedDateTime` for the reference time to get correct
    day-of-week computation.
  - `parseInput` — POST to Gemini REST API
    (`generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent`),
    extract `candidates[0].content.parts[0].text`, strip markdown fences,
    deserialize into `ParseResult`
  - Config: `GEMINI_API_KEY` env var, `System.Net.Http.HttpClient`,
    `System.Text.Json`
- `tests/Michael.Tests/GeminiClientTests.fs` — unit tests for
  `buildSystemPrompt` (correct date/day-of-week rendering) and JSON
  response parsing (not live API calls)

### Verification

System prompt tests pass. Manual curl to `/api/parse` returns parsed result.

---

## Phase 5: API Endpoints & Availability Logic

### Routes

| Method | Path         | Purpose                              |
|--------|-------------|---------------------------------------|
| POST   | /api/parse  | Parse user message → ParseResult      |
| POST   | /api/slots  | Compute overlap slots                 |
| POST   | /api/book   | Confirm and store booking             |
| GET    | /           | Serve Elm SPA (index.html)            |

### Request/Response Shapes

**POST /api/parse**
```
→ { message, timezone, previousMessages[] }
← { parseResult: ParseResult, systemMessage: string }
```

**POST /api/slots**
```
→ { availabilityWindows[], durationMinutes, timezone }
← { slots: TimeSlot[] }
```

**POST /api/book**
```
→ { name, email, phone?, title, description?, slot, durationMinutes, timezone }
← { bookingId, confirmed }
```

### Files

- `src/backend/Availability.fs` — Slot computation using NodaTime:
  - Expand weekly host slots into concrete `Interval` ranges for a date
    range (handles DST transitions correctly via `DateTimeZone`)
  - Intersect participant windows with host windows
  - Subtract existing bookings
  - Chunk into `durationMinutes`-sized slots
- `src/backend/Handlers.fs` — Falco handlers for each endpoint
- `tests/Michael.Tests/AvailabilityTests.fs` — unit tests for slot
  computation (intersection, subtraction, chunking, DST edge cases).
  Use `NodaTime.Testing.FakeClock` for deterministic tests.

### Verification

Availability tests pass. All three endpoints respond correctly via curl.

---

## Phase 6: Static Assets & Build

### Files

- `src/backend/wwwroot/index.html` — HTML shell loading booking.js +
  styles.css
- `src/frontend/styles/booking.css` — Tailwind input CSS (@tailwind
  directives)
- `tailwind.config.js` — content paths: Elm sources + index.html
- `Makefile` — targets:
  - `make backend` — `dotnet build` in src/backend
  - `make frontend` — `elm make` → wwwroot/booking.js
  - `make css` — tailwindcss → wwwroot/styles.css
  - `make test` — `dotnet run` the test project
  - `make dev` — run backend, frontend, CSS with watch/live reload
  - `make clean` — remove build artifacts

### Verification

`make frontend && make css && make backend` produces a working app.
`make test` runs all unit tests.

---

## Phase 7: Elm Frontend

### Project Setup

- `src/frontend/booking/elm.json` — deps: elm/browser, elm/core, elm/html,
  elm/http, elm/json, elm/time, NoRedInk/elm-json-decode-pipeline
- Timezone detected via JS `Intl.DateTimeFormat().resolvedOptions().timeZone`,
  passed as Elm flags (no ports needed)

### Modules

- `src/frontend/booking/src/Main.elm` — `Browser.element` entry point
- `src/frontend/booking/src/Types.elm` — ParseResult, TimeSlot,
  BookingConfirmation, ChatMessage, ConversationPhase DU
- `src/frontend/booking/src/Api.elm` — HTTP requests, JSON
  encoders/decoders for all three endpoints
- `src/frontend/booking/src/Model.elm` — Model record,
  AccumulatedResult (merged across turns), init
- `src/frontend/booking/src/Update.elm` — Msg DU, update function,
  conversation state machine
- `src/frontend/booking/src/View.elm` — Chat UI with phase-dependent
  rendering

### Conversation Phases (state machine)

```
Chatting → ConfirmingParse → SelectingSlot → ConfirmingBooking → BookingComplete
    ↑           |
    └───────────┘  (user rejects parse → back to chatting)
```

- **Chatting**: message list + text input. Each send → POST /api/parse →
  merge result into AccumulatedResult. When missingFields is empty →
  transition to ConfirmingParse.
- **ConfirmingParse**: summary card of interpreted data. Confirm → POST
  /api/slots. Reject → back to Chatting.
- **SelectingSlot**: list of available time slots as buttons.
- **ConfirmingBooking**: final summary. Confirm → POST /api/book.
- **BookingComplete**: success message.

### Verification

Full flow works in browser: type message → see confirmation → pick slot →
confirm → booking stored.

---

## Implementation Order

1. Phase 1 — Backend scaffolding (get dotnet run working)
2. Phase 2 — Test project setup (get dotnet test working)
3. Phase 3 — Database layer (tables, seed data, tests)
4. Phase 4 — Gemini client (port system prompt, HTTP calls, tests)
5. Phase 5 — API endpoints + availability logic (tests)
6. Phase 6 — Build system (Makefile, index.html, Tailwind config)
7. Phase 7 — Elm frontend (types → API → model → update → view)
8. End-to-end testing and fixes

Phase 6 is done before Phase 7 so the build pipeline is in place before
writing Elm code (faster feedback loop).

---

## File List (22 files)

```
src/backend/Michael.fsproj
src/backend/Domain.fs
src/backend/Database.fs
src/backend/Availability.fs
src/backend/GeminiClient.fs
src/backend/Handlers.fs
src/backend/Program.fs
src/backend/wwwroot/index.html
tests/Michael.Tests/Michael.Tests.fsproj
tests/Michael.Tests/AvailabilityTests.fs
tests/Michael.Tests/GeminiClientTests.fs
tests/Michael.Tests/DatabaseTests.fs
tests/Michael.Tests/Program.fs
src/frontend/booking/elm.json
src/frontend/booking/src/Main.elm
src/frontend/booking/src/Types.elm
src/frontend/booking/src/Api.elm
src/frontend/booking/src/Model.elm
src/frontend/booking/src/Update.elm
src/frontend/booking/src/View.elm
src/frontend/styles/booking.css
tailwind.config.js
Makefile
```
