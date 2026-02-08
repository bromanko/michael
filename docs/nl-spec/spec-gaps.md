# Michael NL Spec Gaps and Open Questions

This document summarizes gaps, ambiguities, and inconsistencies in `docs/nl-spec/` that should be clarified by the spec writer.

## 1) High-impact gaps (clarify first)

### 1.1 Scheduling settings are not applied in slot algorithm
**Where:**
- `michael-data-and-algorithms-spec.md` (slot algorithm)
- `michael-api-spec.md` (`/api/slots`, `/api/book`)
- `michael-product-spec.md` / `michael-frontend-spec.md` (settings exist)

**Issue:**
`minNoticeHours` and `bookingWindowDays` are defined and editable, but the slot algorithm does not require enforcing them.

**Open questions:**
- Must `/api/slots` filter out slots that violate min notice / booking window?
- Must `/api/book` revalidate these constraints at booking time?

---

### 1.2 Booking-time revalidation/race semantics are unspecified
**Where:**
- `michael-api-spec.md` (`POST /api/book`)
- `michael-data-and-algorithms-spec.md`

**Issue:**
No normative requirement states whether selected slot must be re-checked against latest blockers/confirmed bookings at booking time.

**Open questions:**
- Is stale-slot booking allowed?
- If not allowed, what status code/payload should be returned (e.g., 409 conflict)?

---

### 1.3 Admin endpoint response schemas are underspecified
**Where:**
- `michael-api-spec.md` section 4 (protected admin APIs)

**Issue:**
Many endpoints are listed but response JSON structures are not fully specified.

**Open questions:**
- Exact response contracts for:
  - `GET /api/admin/bookings`
  - `GET /api/admin/bookings/{id}`
  - `GET /api/admin/calendars`
  - `GET /api/admin/calendars/{id}/history`
  - `POST /api/admin/calendars/{id}/sync`
  - `GET /api/admin/availability`
  - `GET /api/admin/settings`
  - `GET /api/admin/dashboard`
- Required/optional fields, nullability, pagination envelope shape, and date formats.

---

### 1.4 Error payload contract is missing
**Where:**
- `michael-api-spec.md` section 1 (general API rules)

**Issue:**
Status codes are defined, but standardized error body shape is not.

**Open questions:**
- Should all failures return a common structure (e.g., `{ error, code?, details? }`)?
- Should validation errors include per-field details?

---

## 2) Cross-spec inconsistencies and ambiguities

### 2.1 Bookings status filter mismatch (`all`)
**Where:**
- API: `status=confirmed|cancelled` (`michael-api-spec.md`)
- Frontend: status filter includes `all` (`michael-frontend-spec.md`)

**Issue:**
Frontend expects `all`, API does not define it.

**Open questions:**
- Is `all` represented by omitted query param, explicit `status=all`, or both?

---

### 2.2 Calendar-view timezone optional vs required unclear
**Where:**
- `GET /api/admin/calendar-view?start=<iso-instant>&end=<iso-instant>&tz=<iana>` (`michael-api-spec.md`)
- Validation says "valid timezone if provided"

**Issue:**
`tz` appears required by route shape but optional by validation wording.

**Open questions:**
- Is `tz` required?
- If omitted, what timezone should be used (host timezone, UTC, browser, etc.)?

---

### 2.3 Duration constraints are inconsistent
**Where:**
- `/api/slots` and `/api/book`: duration `> 0` (`michael-api-spec.md`)
- Settings: `defaultDurationMinutes` in `[5, 480]` (`michael-api-spec.md`)
- Frontend mentions acceptable range but does not define it (`michael-frontend-spec.md`)

**Issue:**
No single global duration policy.

**Open questions:**
- What is the authoritative allowed range for requested booking duration?
- Must custom duration align with any increment (e.g., 5 min)?

---

### 2.4 Split specs vs unified spec on cancellation emails
**Where:**
- Unified spec section 9 defines cancellation email behavior
- Split runtime spec only says SMTP optional integration

**Issue:**
Precedence says split specs are authoritative input, so email-on-cancel behavior is not fully normative there.

**Open questions:**
- Should cancellation email behavior be copied into split specs as a required behavior?

---

## 3) Runtime/operational gaps

### 3.1 Partial optional integration config behavior unclear
**Where:**
- `michael-runtime-and-delivery-spec.md`

**Issue:**
SMTP is "all-or-nothing" and CalDAV sources are independent optionals, but behavior for partially provided source config is not explicit.

**Open questions:**
- For partial Fastmail/iCloud config, should startup fail fast, warn and disable that source, or ignore?
- Should invalid optional integration config ever be a startup error?

---

### 3.2 Background sync range only specified in unified spec
**Where:**
- Unified spec section 7.3 defines range (background/manual)
- Split runtime/data specs do not

**Issue:**
Core sync behavior depends on range horizon but split specs do not pin it.

**Open questions:**
- What exact sync window must be used for background and manual sync?

---

### 3.3 Session/cookie contract not fully pinned in split specs
**Where:**
- Split API + data/runtime specs

**Issue:**
Some cookie/session details are present, but full compatibility contract is incomplete.

**Open questions:**
- Cookie name?
- Exact SameSite/Secure policy by environment?
- Exact expiry semantics and cleanup triggers?

---

### 3.4 Migration bookkeeping compatibility is vague
**Where:**
- `michael-data-and-algorithms-spec.md`

**Issue:**
Requires `atlas_schema_revisions`-compatible tracking but does not define compatibility level.

**Open questions:**
- Which exact columns/constraints are required for compatibility?
- Is strict schema identity required or behavior-level compatibility sufficient?

---

## 4) Suggested resolution format for spec writer

For each open question above, define:
1. **Normative rule** (must/should language)
2. **Endpoint/schema impact** (exact fields/status codes)
3. **Validation semantics**
4. **Failure behavior** (status + payload)
5. **Cross-file update list** so split specs remain consistent under `ORDER.md`

This will reduce implementation drift and make the NL specs directly executable by coding agents.
