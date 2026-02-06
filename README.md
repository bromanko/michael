# Michael

A personal scheduling tool — *my cal*, if you will.

Michael is a self-hosted alternative to Calendly. Participants express their
availability naturally — via free text or a calendar screenshot — and Michael
finds the overlap with the host's calendar. No rigid time slot grids; just a
conversational booking flow powered by AI.

See [docs/design.md](docs/design.md) for the full product design.

## What's Working

**Booking page** (Elm) — conversational, chat-like interface where participants
provide availability in natural language. Gemini parses the input, extracts
availability windows, and prompts for missing info. Participants confirm parsed
availability, pick a slot, and book.

**Admin dashboard** (Elm) — login, dashboard overview, booking list with
detail views, cancellation, calendar source management, host availability
configuration, merged calendar view, and settings.

**Backend** (F# / Falco) — full API for the booking flow (`/api/parse`,
`/api/slots`, `/api/book`) and admin operations (bookings, calendars,
availability, settings). Session-based password auth for the admin area.

**Calendar sync** — CalDAV integration with Fastmail and iCloud. Background
polling syncs external calendar events into a local cache so host availability
stays current.

**Email notifications** — booking confirmations and cancellation notices via
SMTP (Fastmail).

**Database** — SQLite with Atlas-managed migrations.

## Tech Stack

| Layer     | Choice                        |
|-----------|-------------------------------|
| Backend   | F# with Falco                 |
| Frontend  | Elm (booking page + admin)    |
| Styling   | Tailwind CSS                  |
| Database  | SQLite                        |
| AI        | Google Gemini                 |
| Email     | SMTP (Fastmail)               |
| VCS       | Jujutsu (jj)                  |

## Development

### Prerequisites

- [Nix](https://nixos.org/) with flakes enabled
- [direnv](https://direnv.net/) (recommended)

### Getting Started

If you have direnv installed, the dev shell activates automatically:

```sh
cd michael
direnv allow
```

Otherwise, enter the dev shell manually:

```sh
nix develop
```

### Running the Backend

Set the required environment variables and start the server:

```sh
export MICHAEL_HOST_TIMEZONE="America/New_York"
export GEMINI_API_KEY="your-key"
export MICHAEL_ADMIN_PASSWORD="your-password"

dotnet run --project src/backend
```

#### Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `MICHAEL_HOST_TIMEZONE` | Yes | IANA timezone for the host (e.g. `America/New_York`) |
| `GEMINI_API_KEY` | Yes | Google Gemini API key for natural language parsing |
| `MICHAEL_ADMIN_PASSWORD` | Yes | Password for the admin dashboard |
| `MICHAEL_DB_PATH` | No | SQLite database path (default: `michael.db`) |
| `MICHAEL_SMTP_HOST` | No | SMTP server host |
| `MICHAEL_SMTP_PORT` | No | SMTP server port |
| `MICHAEL_SMTP_USERNAME` | No | SMTP username |
| `MICHAEL_SMTP_PASSWORD` | No | SMTP password |
| `MICHAEL_SMTP_FROM` | No | Sender email address |
| `MICHAEL_SMTP_FROM_NAME` | No | Sender display name (default: `Michael`) |
| `MICHAEL_CALDAV_FASTMAIL_URL` | No | Fastmail CalDAV calendar URL |
| `MICHAEL_CALDAV_FASTMAIL_USERNAME` | No | Fastmail username |
| `MICHAEL_CALDAV_FASTMAIL_PASSWORD` | No | Fastmail app password |
| `MICHAEL_CALDAV_ICLOUD_URL` | No | iCloud CalDAV calendar URL |
| `MICHAEL_CALDAV_ICLOUD_USERNAME` | No | iCloud username |
| `MICHAEL_CALDAV_ICLOUD_PASSWORD` | No | iCloud app-specific password |

SMTP variables are all-or-nothing — if any are missing, email notifications are
disabled. CalDAV sources are configured independently; set all three variables
for a provider to enable it.

### Building the Frontend

```sh
# Booking page
cd src/frontend/booking && elm make src/Main.elm --output=../../backend/wwwroot/booking.js

# Admin dashboard
cd src/frontend/admin && elm make src/Main.elm --output=../../backend/wwwroot/admin.js

# Tailwind
tailwindcss -i src/frontend/styles/booking.css -o src/backend/wwwroot/booking-styles.css
```

### Running CI

This project uses [SelfCI](https://app.radicle.xyz/nodes/radicle.dpc.pw/rad%3Az2tDzYbAXxTQEKTGFVwiJPajkbeDU)
for local-first continuous integration — no remote servers needed.

```sh
selfci check             # lint, build, frontend, and tests in parallel
```

CI runs four parallel jobs: lint (treefmt), backend build (dotnet), frontend
build (Elm + Tailwind), and tests. Configuration lives in `.config/selfci/`.

## Project Structure

```
src/
  backend/            F# backend (Falco, SQLite, CalDAV, email)
  frontend/
    booking/          Elm — participant booking page
    admin/            Elm — host admin dashboard
    styles/           Tailwind CSS
docs/                 Product design
.config/selfci/       CI configuration
.tickets/             Task tickets
```
