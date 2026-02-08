# Michael API Specification (NL Spec)

Normative HTTP contract for public and admin APIs.

---

## 1. General API Rules

- Transport: HTTP JSON.
- JSON field names: `camelCase`.
- Datetimes:
  - offset datetimes: ISO-8601 strings with offset (for booking windows/slots/bookings)
  - instants: ISO-8601 instant strings (for calendar-view range query and sync/history timestamps)

Status code classes:

- Validation failures: HTTP 400.
- Unauthorized admin access: HTTP 401.
- Resource not found: HTTP 404.
- Booking conflict/stale slot: HTTP 409.
- Internal failures: HTTP 500 with sanitized message.

### 1.1 Error payload contract (normative)

All non-2xx JSON responses MUST include at least:

```json
{ "error": "Human-readable message" }
```

Optional extension fields are allowed:

- `code` (stable machine-friendly error code)
- `details` (object/array with extra context)

Implementations MAY omit `code/details`, but MUST include `error`.

---

## 2. Public Booking API

### 2.1 `POST /api/parse`

#### Request

```json
{
  "message": "I can do next Tuesday afternoon",
  "timezone": "America/New_York",
  "previousMessages": []
}
```

#### Validation

- `message` required and non-empty.
- `timezone` required and valid IANA timezone.

#### Behavior

- Concatenate prior messages plus current message.
- Parse with LLM extraction pipeline.
- Return structured parse result and system summary.

#### Response

```json
{
  "parseResult": {
    "availabilityWindows": [],
    "durationMinutes": null,
    "title": null,
    "description": null,
    "name": null,
    "email": null,
    "phone": null,
    "missingFields": []
  },
  "systemMessage": "..."
}
```

---

### 2.2 `POST /api/slots`

#### Request

```json
{
  "availabilityWindows": [
    {
      "start": "2026-02-10T12:00:00-05:00",
      "end": "2026-02-10T17:00:00-05:00",
      "timezone": "America/New_York"
    }
  ],
  "durationMinutes": 30,
  "timezone": "America/New_York"
}
```

#### Validation

- non-empty `availabilityWindows`
- `durationMinutes` in `[5, 480]`
- valid timezone
- each window start/end parseable ISO offset datetime

#### Behavior

- Compute slot candidates from participant windows + host weekly availability - blockers.
- Enforce scheduling settings during slot generation:
  - `minNoticeHours`: drop slots starting before `now + minNoticeHours`
  - `bookingWindowDays`: drop slots starting after `now + bookingWindowDays`

#### Response

```json
{
  "slots": [
    {
      "start": "2026-02-10T13:00:00-05:00",
      "end": "2026-02-10T13:30:00-05:00"
    }
  ]
}
```

---

### 2.3 `POST /api/book`

#### Request

```json
{
  "name": "Jane",
  "email": "jane@example.com",
  "phone": null,
  "title": "Project kickoff",
  "description": null,
  "slot": {
    "start": "2026-02-10T13:00:00-05:00",
    "end": "2026-02-10T13:30:00-05:00"
  },
  "durationMinutes": 30,
  "timezone": "America/New_York"
}
```

#### Validation

- required: `name`, `email`, `title`, `slot.start`, `slot.end`, `durationMinutes`, `timezone`
- email format must be minimally valid (`local@domain` and domain contains dot, not trailing dot)
- `durationMinutes` in `[5, 480]`
- timezone valid

#### Booking-time revalidation (normative)

Before persisting, backend MUST revalidate the selected slot against current state:

1. Slot still intersects host availability.
2. Slot does not overlap any confirmed booking.
3. Slot does not overlap cached calendar blockers.
4. Slot still satisfies scheduling settings (`minNoticeHours`, `bookingWindowDays`).

If any check fails, backend MUST reject with `409` and an error payload.

Preferred machine code:

```json
{ "error": "Selected slot is no longer available.", "code": "slot_unavailable" }
```

#### Success response

```json
{
  "bookingId": "<guid>",
  "confirmed": true
}
```

---

## 3. Admin Authentication API

### 3.1 `POST /api/admin/login`

Request:

```json
{ "password": "..." }
```

Behavior:

- validates password against startup-hashed configured password
- creates session token and DB record
- sets HttpOnly cookie for `/api/admin`

Success response:

```json
{ "ok": true }
```

Invalid password -> `401`.

### 3.2 `POST /api/admin/logout`

- deletes existing session if present
- clears cookie
- returns `{ "ok": true }`

### 3.3 `GET /api/admin/session`

- returns `{ "ok": true }` when valid session
- returns `401` with error when missing/invalid/expired

### 3.4 Session cookie contract (normative)

- Cookie name: `michael_session`
- Path: `/api/admin`
- HttpOnly: `true`
- SameSite: `Strict`
- Expiration: 7 days from login
- Secure policy:
  - development environment: `Secure=false`
  - non-development: `Secure=true`

---

## 4. Admin Protected API

All endpoints below require valid admin session.

### 4.1 Bookings

#### `GET /api/admin/bookings?page=<int>&pageSize=<int>&status=<value>`

`status` semantics:

- `confirmed` => only confirmed bookings
- `cancelled` => only cancelled bookings
- omitted/empty/`all` => all bookings
- unknown value => treat as all bookings

Response:

```json
{
  "bookings": [
    {
      "id": "<guid>",
      "participantName": "Jane",
      "participantEmail": "jane@example.com",
      "participantPhone": null,
      "title": "Project kickoff",
      "description": null,
      "startTime": "2026-02-10T13:00:00-05:00",
      "endTime": "2026-02-10T13:30:00-05:00",
      "durationMinutes": 30,
      "timezone": "America/New_York",
      "status": "confirmed",
      "createdAt": "2026-02-08T19:00:00Z"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 20
}
```

#### `GET /api/admin/bookings/{id}`

Response shape: single booking object (same fields as above).

#### `POST /api/admin/bookings/{id}/cancel`

Success response:

```json
{ "ok": true }
```

Behavior:

- If booking exists and is confirmed, set status to cancelled.
- If SMTP enabled, attempt cancellation email asynchronously/inline; email failure MUST NOT fail cancellation response.

---

### 4.2 Dashboard

#### `GET /api/admin/dashboard`

Response:

```json
{
  "upcomingCount": 3,
  "nextBookingTime": "2026-02-10T13:00:00-05:00",
  "nextBookingTitle": "Project kickoff"
}
```

`nextBookingTime` and `nextBookingTitle` may be `null` when no upcoming bookings.

---

### 4.3 Calendar sources and sync

#### `GET /api/admin/calendars`

Response:

```json
{
  "sources": [
    {
      "id": "<guid>",
      "provider": "fastmail",
      "baseUrl": "https://...",
      "lastSyncedAt": "2026-02-08T19:00:00Z",
      "lastSyncResult": "ok"
    }
  ]
}
```

#### `GET /api/admin/calendars/{id}/history?limit=1..50`

Response:

```json
{
  "history": [
    {
      "id": "<guid>",
      "sourceId": "<guid>",
      "syncedAt": "2026-02-08T19:00:00Z",
      "status": "ok",
      "errorMessage": null
    }
  ]
}
```

#### `POST /api/admin/calendars/{id}/sync`

Success response:

```json
{ "ok": true }
```

---

### 4.4 Host availability

#### `GET /api/admin/availability`

Response:

```json
{
  "slots": [
    { "id": "<guid>", "dayOfWeek": 1, "startTime": "09:00", "endTime": "17:00" }
  ],
  "timezone": "America/Los_Angeles"
}
```

#### `PUT /api/admin/availability`

Body:

```json
{
  "slots": [
    { "dayOfWeek": 1, "startTime": "09:00", "endTime": "17:00" }
  ]
}
```

Validation:

- at least one slot
- dayOfWeek in 1..7
- `HH:MM` time format
- `startTime < endTime`

Success response: same shape as `GET /api/admin/availability`.

---

### 4.5 Scheduling settings

#### `GET /api/admin/settings`

Response:

```json
{
  "minNoticeHours": 6,
  "bookingWindowDays": 30,
  "defaultDurationMinutes": 30,
  "videoLink": null
}
```

#### `PUT /api/admin/settings`

Body:

```json
{
  "minNoticeHours": 6,
  "bookingWindowDays": 30,
  "defaultDurationMinutes": 30,
  "videoLink": null
}
```

Validation:

- minNoticeHours >= 0
- bookingWindowDays >= 1
- defaultDurationMinutes in [5, 480]

Success response: same shape as `GET /api/admin/settings`.

---

### 4.6 Calendar view

#### `GET /api/admin/calendar-view?start=<iso-instant>&end=<iso-instant>&tz=<iana>`

`tz` semantics:

- optional
- if omitted, use host timezone

Validation:

- valid `start` and `end` instants
- `start < end`
- valid timezone if provided

Response:

```json
{
  "events": [
    {
      "id": "...",
      "title": "...",
      "start": "2026-02-10T13:00:00",
      "end": "2026-02-10T14:00:00",
      "isAllDay": false,
      "eventType": "calendar"
    }
  ]
}
```

`eventType` must be one of `calendar | booking | availability`.

---

## 5. Static Routes

- `/` serves booking SPA
- `/admin/*` serves admin SPA (fallback to admin index)

---

## 6. Compatibility Contract

Any reimplementation must preserve:

1. Endpoint paths and HTTP methods.
2. Request/response JSON structure and field names.
3. Validation semantics and status-code classes.
4. Session/cookie behavior for admin authentication.
5. Booking-time revalidation and `409` conflict semantics.
