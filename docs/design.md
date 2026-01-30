# Michael — Product Design Document

Michael (*my cal*) is a personal scheduling tool — a Calendly alternative with a
fundamentally different user experience focused on natural, flexible availability
input.

## Core Concept

Unlike Calendly, where the host defines rigid time slots and guests pick one,
Michael flips the interaction. Participants express their availability naturally
— via free text or even a screenshot of their calendar — and the system finds
the overlap with the host's availability. The participant then picks from the
overlapping slots, confirms the booking details, and the meeting is finalized.

---

## 1. Participant Experience (Booking Page)

The public-facing booking page is an Elm application with a conversational,
chat-like interface. Rather than a rigid step-by-step form, the participant
provides information naturally and the system extracts what it can, prompting
for anything missing.

### Information Collection

The system collects the following from the participant, in any order:

- **Availability** — when they're free (see input methods below)
- **Meeting duration** — how long they need (15 min – 1 hour)
- **Meeting title** (required) and **description** (optional)
- **Contact info** — name (required), email (required), phone (optional)

The participant can provide multiple pieces in a single message (e.g., "I'm
free next Friday afternoon for a 30-min chat about the project redesign. My
email is jane@example.com"). The system extracts what it can and prompts for
the rest.

### Availability Input Methods

- **Free text** — Natural language input like "I'm free next Friday afternoon"
  or "anytime Tuesday or Wednesday before noon." Parsed into structured
  availability windows by an AI model. Inspired by Fantastical's natural
  language input.
- **Screenshot upload** — Paste or upload a screenshot of their calendar
  directly into Michael. The system parses it server-side via an AI vision
  model to extract availability.
- **External screenshot prompt** — For privacy-conscious participants who don't
  want to share a calendar screenshot with the host. Michael provides a
  ready-made prompt they can feed to their own AI (e.g., ChatGPT) along with
  their screenshot. They paste the structured result back into the text input.

All methods produce structured data describing the participant's availability
windows.

### Parsed Availability Confirmation

After the system parses the participant's availability, it shows the
interpreted result (e.g., "I understood you're available Tuesday 12:00–17:00
and Thursday 9:00–12:00 — is that right?"). The participant confirms or
corrects before proceeding to slot selection.

### Slot Selection

Once availability is confirmed, the system computes the overlap with the
host's availability and presents matching slots. If there is no overlap, the
system explains the problem and asks the participant to try different times.

### Booking Confirmation

Before finalizing, the participant sees a summary of the booking (time, date,
duration, title, timezone) and explicitly confirms. This prevents accidental
bookings.

### Time Zones

- Auto-detect the participant's time zone via browser
- Participant can override the detected time zone in the UI
- All times displayed in the participant's selected time zone

### Agent-Friendly

The booking page uses semantic HTML structured well enough that AI agents
(e.g., ChatGPT) can parse and interact with the booking flow. No separate API
— the HTML is the interface for both humans and agents.

### Proxy Booking

The host can use the booking flow on behalf of someone else (e.g., booking for a
colleague who shared their availability verbally).

---

## 2. Host Availability

How the host defines when they're available.

### Calendar Integration

Connect multiple calendars from different providers. Events from connected
calendars block availability automatically. All free time across connected
calendars is considered bookable (no recurring availability windows — the
calendars are the source of truth).

Supported providers:
- **Fastmail** (CalDAV) — primary personal calendar
- **Google Calendar** — for Google Calendar invites
- **Apple iCloud** (CalDAV) — iCloud calendar
- Additional providers as needed (work calendar TBD)

CalDAV support covers Fastmail, iCloud, and many other providers. Google Calendar
requires its own API integration.

### Manual Overrides

- Block specific times that aren't on any calendar
- Overrides can only block time, not open otherwise-blocked time

### Minimum Scheduling Notice

- Configurable minimum time before a meeting can be booked
- Default: 6 hours

### Admin View

A dashboard showing:
- Merged calendar view across all connected calendars
- Visual blocks for ingested calendar events
- Visual blocks for manual overrides
- Detailed info for booked Michael meetings

---

## 3. Bookings

### Confirmation Flow

- Participant selects a slot → reviews booking summary → confirms
- Send calendar invites (`.ics`) to both parties
- Email confirmations on booking

### Cancellation

- Either side (host or participant) can cancel
- Notifications sent on cancellation to the other party
- No dedicated rescheduling flow — cancel and rebook

### Booking Window

- Configurable cap on how far in the future participants can book
- Default: 1 month out

### Abuse Prevention

- Aggressive rate limiting per IP and per endpoint
- No email verification required (keeps the flow simple for a personal tool)

---

## 4. Meetings

### Video Conferencing

- Pre-configure a Zoom or Google Meet link into the booking
- Open question: auto-generate a unique link per meeting, or use a single
  persistent personal meeting link?

### Meeting Details

Participant provides:
- Title (required)
- Description (optional)

---

## 5. Infrastructure & Tech Stack

| Layer              | Choice                                      |
|--------------------|---------------------------------------------|
| VCS                | Jujutsu (jj), backed by GitHub              |
| Monorepo           | Yes                                         |
| Backend            | F# with Falco                               |
| Client App         | Elm (main interactive app)                  |
| Booking Page       | Elm                                         |
| Static Pages       | Server-rendered HTML + vanilla JS           |
| Styling            | Tailwind CSS                                |
| Database           | SQLite (single-instance deployment)         |
| AI                 | TBD — model selection per feature           |
| Rate Limiting      | Aggressive, to control AI/API costs         |
| Task Management    | Ticket (simple task management)             |
| Email              | Fastmail SMTP                               |
| Auth (admin)       | Passkey (WebAuthn) or OIDC (GitHub/Fastmail) |
| Deployment         | TBD (likely Fly.io or Cloudflare)           |

Note: SQLite constrains deployment to a single instance. If multi-instance is
needed later, LiteFS or Turso are options.

---

## 6. AI Integration Points

| Feature                       | AI Role                                      |
|-------------------------------|----------------------------------------------|
| Conversational input parsing  | Extract availability, duration, contact info, meeting details from natural language |
| Screenshot upload             | Vision model parsing → structured time windows|
| External screenshot prompt    | Provide prompt for user to run externally     |

Rate limiting is critical across all AI-powered features to avoid runaway costs.

---

## 7. Component Systems

Based on the above, Michael needs the following major systems:

1. **Calendar Sync Engine** — CalDAV and Google Calendar polling/webhooks,
   event normalization, availability computation
2. **Availability Resolver** — Merge calendars + manual overrides + minimum
   notice into a unified availability window set
3. **Conversational Input Parser** — AI-powered extraction of availability,
   duration, contact info, and meeting details from natural language and images
4. **Booking Engine** — Overlap computation, slot presentation, booking
   creation, `.ics` generation, race condition handling
5. **Notification System** — Email confirmations, cancellation notices via
   Fastmail SMTP
6. **Admin Dashboard** — Elm app for calendar view, manual overrides, booking
   management
7. **Booking Page** — Elm app with conversational interface for the
   participant-facing booking flow
8. **Auth System** — Passkey/OIDC for admin access
9. **Video Conferencing Integration** — Zoom/Google Meet link management
10. **Rate Limiter** — Per-IP and per-endpoint rate limiting, especially
    for AI-powered endpoints
