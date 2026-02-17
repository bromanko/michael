import { describe, test, expect, beforeAll } from "vitest";
import type { TestContext } from "vitest";
import {
  fetchCsrfToken,
  postWithCsrf,
  CsrfContext,
  mapWithConcurrency,
  PROBE_CONCURRENCY,
} from "../helpers/api-client";
import type { ErrorResponse, SlotsResponse } from "../helpers/api-types";
import { utcOffsetFor } from "../helpers/tz";

// ---------------------------------------------------------------------------
// /api/slots — Slot Computation
//
// Covers EARS requirements: SLT-001..SLT-007, SLT-010..SLT-013,
//                           VAL-001, VAL-002, VAL-004
// ---------------------------------------------------------------------------

/** Must match server config `MinNoticeHours`. */
const ASSUMED_MIN_NOTICE_HOURS = 6;
/** Must match server config `BookingWindowDays`. */
const ASSUMED_BOOKING_WINDOW_DAYS = 30;

/** First day offset (from today) when probing for clean windows.
 *  Starts far enough out to avoid notice-period and existing booking
 *  interference. */
const PROBE_START_DAY = 5;
/** Number of consecutive days to probe. */
const PROBE_DAY_COUNT = 21;

/** Resolve a future weekday date: advance `daysOut` from today, skip to
 *  Monday if the result lands on a weekend. Returns the adjusted Date,
 *  formatted date parts, and the DST-aware UTC offset for `tz` — all
 *  computed once so callers avoid redundant work. */
function resolveFutureWeekday(
  daysOut: number,
  tz: string,
): { date: Date; yyyy: string; mm: string; dd: string; offset: string } {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysOut);

  // Skip to Monday if it lands on a weekend
  const day = d.getUTCDay();
  if (day === 0)
    d.setUTCDate(d.getUTCDate() + 1); // Sunday → Monday
  else if (day === 6) d.setUTCDate(d.getUTCDate() + 2); // Saturday → Monday

  return {
    date: d,
    yyyy: String(d.getUTCFullYear()),
    mm: String(d.getUTCMonth() + 1).padStart(2, "0"),
    dd: String(d.getUTCDate()).padStart(2, "0"),
    offset: utcOffsetFor(d, tz),
  };
}

/** Return an ISO offset-datetime string for a weekday N days from now at the
 *  given hour in the given IANA timezone. Skips to the next Monday if the
 *  computed date falls on a weekend. */
function futureWeekday(
  daysOut: number,
  hour: number,
  minute: number,
  tz: string,
): string {
  const { yyyy, mm, dd, offset } = resolveFutureWeekday(daysOut, tz);
  const hh = String(hour).padStart(2, "0");
  const min = String(minute).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}T${hh}:${min}:00${offset}`;
}

/** Build an availability window for a future weekday. Resolves the date
 *  and offset once, then formats both start and end from the same values. */
function weekdayWindow(
  daysOut: number,
  startHour: number,
  endHour: number,
  tz: string,
) {
  const { yyyy, mm, dd, offset } = resolveFutureWeekday(daysOut, tz);
  const sh = String(startHour).padStart(2, "0");
  const eh = String(endHour).padStart(2, "0");
  return {
    start: `${yyyy}-${mm}-${dd}T${sh}:00:00${offset}`,
    end: `${yyyy}-${mm}-${dd}T${eh}:00:00${offset}`,
  };
}

/** Compute the offset string for a date N days from now in the given IANA
 *  timezone, skipping weekends. */
function futureWeekdayOffset(daysOut: number, tz: string): string {
  return resolveFutureWeekday(daysOut, tz).offset;
}

describe("/api/slots", () => {
  let csrf: CsrfContext;

  beforeAll(async () => {
    csrf = await fetchCsrfToken();
  });

  function postSlots(body: unknown): Promise<Response> {
    return postWithCsrf("/api/slots", body, csrf);
  }

  // -------------------------------------------------------------------------
  // Validation (SLT-010..SLT-012, VAL-001, VAL-002, VAL-004)
  // -------------------------------------------------------------------------

  describe("validation", () => {
    // Shared windows for validation tests — computed once to avoid repeated
    // date arithmetic. The wide window (9–17) is needed for 480-min boundary.
    const validWindow = weekdayWindow(3, 12, 17, "America/New_York");
    const wideWindow = weekdayWindow(3, 9, 17, "America/New_York");

    test("rejects empty request body with 400", async () => {
      const resp = await postSlots({});
      expect(resp.status).toBe(400);
    });

    test("SLT-010: rejects empty availability windows with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("At least one availability window is required.");
    });

    test("SLT-011: rejects duration 0 with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [validWindow],
        durationMinutes: 0,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("SLT-011: rejects duration 500 with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [validWindow],
        durationMinutes: 500,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("VAL-004: rejects negative duration with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [validWindow],
        durationMinutes: -1,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("VAL-004: rejects duration 4 (below boundary)", async () => {
      const resp = await postSlots({
        availabilityWindows: [validWindow],
        durationMinutes: 4,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("VAL-004: accepts duration 5 (lower boundary)", async () => {
      const resp = await postSlots({
        availabilityWindows: [validWindow],
        durationMinutes: 5,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(Array.isArray(body.slots)).toBe(true);
    });

    test("VAL-004: accepts duration 480 (upper boundary)", async () => {
      const resp = await postSlots({
        availabilityWindows: [wideWindow],
        durationMinutes: 480,
        timezone: "America/New_York",
      });

      // May return empty (host availability may not cover 480 contiguous
      // minutes in the participant window), but should not 400.
      expect(resp.ok).toBe(true);
    });

    test("VAL-004: rejects duration 481 (above boundary)", async () => {
      const resp = await postSlots({
        availabilityWindows: [wideWindow],
        durationMinutes: 481,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("SLT-012: rejects malformed start datetime with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [
          { start: "not-a-date", end: "2026-02-17T17:00:00-05:00" },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("not-a-date");
      expect(body.error).toContain("Start");
    });

    test("SLT-012: rejects malformed end datetime with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [
          { start: "2026-02-17T12:00:00-05:00", end: "bad-end" },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("bad-end");
      expect(body.error).toContain("End");
    });

    test("SLT-012: rejects date-only string (no time component)", async () => {
      const resp = await postSlots({
        availabilityWindows: [
          { start: "2026-02-17", end: "2026-02-17T17:00:00-05:00" },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
    });

    test("SLT-012: rejects window with end before start", async () => {
      const resp = await postSlots({
        availabilityWindows: [
          {
            start: "2026-02-20T17:00:00-05:00",
            end: "2026-02-20T12:00:00-05:00",
          },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("End must be after");
    });

    test("SLT-012: rejects window with equal start and end", async () => {
      const resp = await postSlots({
        availabilityWindows: [
          {
            start: "2026-02-20T14:00:00-05:00",
            end: "2026-02-20T14:00:00-05:00",
          },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("End must be after");
    });

    test("VAL-002: rejects invalid timezone with 400", async () => {
      const resp = await postSlots({
        availabilityWindows: [validWindow],
        durationMinutes: 30,
        timezone: "Fake/Zone",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("Fake/Zone");
    });
  });

  // -------------------------------------------------------------------------
  // Slot computation (SLT-001, SLT-004, SLT-005, SLT-006, SLT-007, SLT-013)
  // -------------------------------------------------------------------------

  describe("computation", () => {
    test("SLT-001: returns overlapping slots for valid weekday window", async () => {
      // Use a weekday well in the future to avoid min-notice issues
      const w = weekdayWindow(5, 13, 15, "America/New_York");
      const resp = await postSlots({
        availabilityWindows: [w],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(body.slots.length).toBeGreaterThan(0);
    });

    test("SLT-004: divides window into correct number of slots", async () => {
      // Find a clean 2-hour weekday window with exactly 4 30-min slots
      // (no existing bookings interfering). Probe days concurrently.
      const windows = Array.from(
        { length: PROBE_DAY_COUNT },
        (_, i) => i + PROBE_START_DAY,
      ).map((daysOut) => ({
        daysOut,
        window: weekdayWindow(daysOut, 13, 15, "America/Los_Angeles"),
      }));

      const results = await mapWithConcurrency(
        windows,
        PROBE_CONCURRENCY,
        async ({ window: w }) => {
          const resp = await postSlots({
            availabilityWindows: [w],
            durationMinutes: 30,
            timezone: "America/Los_Angeles",
          });
          if (!resp.ok) return null;
          const body = (await resp.json()) as SlotsResponse;
          return body.slots.length === 4 ? body.slots : null;
        },
      );

      const matchingSlots = results.find((r) => r !== null);

      if (!matchingSlots) {
        expect.fail("No clean window found with exactly 4 slots");
        return;
      }

      // Verify each slot is exactly 30 minutes
      for (const slot of matchingSlots) {
        const start = new Date(slot.start).getTime();
        const end = new Date(slot.end).getTime();
        expect(end - start).toBe(30 * 60 * 1000);
      }

      // Verify slots are contiguous (each slot's end === next slot's start)
      // to confirm they fill the window rather than being a coincidental count
      for (let i = 0; i < matchingSlots.length - 1; i++) {
        const thisEnd = new Date(matchingSlots[i].end).getTime();
        const nextStart = new Date(matchingSlots[i + 1].start).getTime();
        expect(thisEnd).toBe(nextStart);
      }
    });

    test("SLT-004: 60-minute slots in 2-hour window yields 2 slots", async () => {
      // Find a clean 2-hour weekday window with exactly 2 60-min slots.
      // Probe days concurrently.
      const windows = Array.from(
        { length: PROBE_DAY_COUNT },
        (_, i) => i + PROBE_START_DAY,
      ).map((daysOut) => ({
        daysOut,
        window: weekdayWindow(daysOut, 13, 15, "America/Los_Angeles"),
      }));

      const results = await mapWithConcurrency(
        windows,
        PROBE_CONCURRENCY,
        async ({ window: w }) => {
          const resp = await postSlots({
            availabilityWindows: [w],
            durationMinutes: 60,
            timezone: "America/Los_Angeles",
          });
          if (!resp.ok) return null;
          const body = (await resp.json()) as SlotsResponse;
          return body.slots.length === 2 ? body.slots : null;
        },
      );

      const matchingSlots = results.find((r) => r !== null);

      if (!matchingSlots) {
        expect.fail("No clean window found with exactly 2 slots");
        return;
      }

      for (const slot of matchingSlots) {
        const start = new Date(slot.start).getTime();
        const end = new Date(slot.end).getTime();
        expect(end - start).toBe(60 * 60 * 1000);
      }

      // Verify slots are contiguous
      for (let i = 0; i < matchingSlots.length - 1; i++) {
        const thisEnd = new Date(matchingSlots[i].end).getTime();
        const nextStart = new Date(matchingSlots[i + 1].start).getTime();
        expect(thisEnd).toBe(nextStart);
      }
    });

    test("SLT-005: returns slots in participant's requested timezone", async () => {
      // Request in Chicago — host is in LA
      // Slots should carry the Chicago offset (DST-aware)
      const w = weekdayWindow(5, 9, 17, "America/Los_Angeles");
      const resp = await postSlots({
        availabilityWindows: [w],
        durationMinutes: 30,
        timezone: "America/Chicago",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;

      expect(body.slots.length).toBeGreaterThan(0);
      // Dynamically determine Chicago's expected offset for this date
      const expectedOffset = futureWeekdayOffset(5, "America/Chicago");
      const offsetPattern = new RegExp(
        `${expectedOffset.replace("+", "\\+")}$`,
      );
      for (const slot of body.slots) {
        expect(slot.start).toMatch(offsetPattern);
        expect(slot.end).toMatch(offsetPattern);
      }
    });

    test("SLT-006: excludes slots within minimum notice period", async (ctx: TestContext) => {
      // We test both sides of the notice boundary:
      //   1. A window entirely inside the notice period → 0 slots
      //   2. A window well beyond the notice period → >0 slots
      // The combination proves the minimum notice filter is active and
      // that the test isn't accidentally passing due to other factors
      // (e.g., no host availability).
      const h = 60 * 60 * 1000; // 1 hour in ms
      const now = new Date();

      // --- Inside notice period: entirely within ASSUMED_MIN_NOTICE_HOURS ---
      const insideStart = new Date(now.getTime() + 1 * h);
      const insideEnd = new Date(
        now.getTime() + (ASSUMED_MIN_NOTICE_HOURS - 1) * h,
      );

      // Check weekday in the host's timezone (America/Los_Angeles), not UTC.
      // Late Friday UTC can still be Friday in US timezones; using UTC day
      // would incorrectly skip or pass for the wrong reason.
      const hostTz = "America/Los_Angeles";
      const localDay = new Date(
        new Intl.DateTimeFormat("en-US", {
          timeZone: hostTz,
          year: "numeric",
          month: "2-digit",
          day: "2-digit",
        })
          .formatToParts(insideStart)
          .reduce(
            (acc, p) =>
              p.type === "year" || p.type === "month" || p.type === "day"
                ? acc + (acc ? "-" : "") + p.value
                : acc,
            "",
          ),
      ).getDay();

      if (localDay === 0 || localDay === 6) {
        ctx.skip(
          "Test window falls on weekend in host timezone — host has no availability",
        );
        return;
      }

      const fmt = (d: Date) => d.toISOString().replace("Z", "+00:00");
      const insideResp = await postSlots({
        availabilityWindows: [{ start: fmt(insideStart), end: fmt(insideEnd) }],
        durationMinutes: 30,
        timezone: "UTC",
      });

      expect(insideResp.ok).toBe(true);
      const insideBody = (await insideResp.json()) as SlotsResponse;
      expect(insideBody.slots.length).toBe(0);

      // --- Beyond notice period: use a weekday well past the notice window ---
      const w = weekdayWindow(5, 9, 17, "America/New_York");
      const beyondResp = await postSlots({
        availabilityWindows: [w],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(beyondResp.ok).toBe(true);
      const beyondBody = (await beyondResp.json()) as SlotsResponse;
      // Slots exist beyond the notice period, proving the filter is what
      // excluded the earlier window (not a lack of availability).
      expect(beyondBody.slots.length).toBeGreaterThan(0);
    });

    test("SLT-007: excludes slots beyond booking window", async () => {
      // Request slots well past the booking window
      const w = weekdayWindow(
        ASSUMED_BOOKING_WINDOW_DAYS * 2,
        9,
        17,
        "America/New_York",
      );
      const resp = await postSlots({
        availabilityWindows: [w],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(body.slots.length).toBe(0);
    });

    test("SLT-013: returns empty slots array for weekend (no host availability)", async () => {
      // Find the next Saturday
      const now = new Date();
      const daysUntilSaturday = (6 - now.getUTCDay() + 7) % 7 || 7;
      const sat = new Date(now);
      sat.setUTCDate(sat.getUTCDate() + daysUntilSaturday);

      const yyyy = sat.getUTCFullYear();
      const mm = String(sat.getUTCMonth() + 1).padStart(2, "0");
      const dd = String(sat.getUTCDate()).padStart(2, "0");
      const offset = utcOffsetFor(sat, "America/New_York");

      const resp = await postSlots({
        availabilityWindows: [
          {
            start: `${yyyy}-${mm}-${dd}T09:00:00${offset}`,
            end: `${yyyy}-${mm}-${dd}T17:00:00${offset}`,
          },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(body.slots.length).toBe(0);
    });

    test("SLT-013: returns empty slots for Sunday", async () => {
      const now = new Date();
      const daysUntilSunday = (7 - now.getUTCDay()) % 7 || 7;
      const sun = new Date(now);
      sun.setUTCDate(sun.getUTCDate() + daysUntilSunday);

      const yyyy = sun.getUTCFullYear();
      const mm = String(sun.getUTCMonth() + 1).padStart(2, "0");
      const dd = String(sun.getUTCDate()).padStart(2, "0");
      const offset = utcOffsetFor(sun, "America/New_York");

      const resp = await postSlots({
        availabilityWindows: [
          {
            start: `${yyyy}-${mm}-${dd}T09:00:00${offset}`,
            end: `${yyyy}-${mm}-${dd}T17:00:00${offset}`,
          },
        ],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(body.slots.length).toBe(0);
    });

    test("SLT-013: returns empty slots for weekday outside host hours", async () => {
      // Request a window entirely before host business hours (2am-5am)
      // on a weekday — should return no slots even though it's not a weekend
      const w = weekdayWindow(5, 2, 5, "America/New_York");
      const resp = await postSlots({
        availabilityWindows: [w],
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(body.slots.length).toBe(0);
    });

    test("SLT-001: returns slots across multiple availability windows", async () => {
      // Two separate weekday windows
      const w1 = weekdayWindow(5, 10, 11, "America/Los_Angeles");
      const w2 = weekdayWindow(6, 14, 15, "America/Los_Angeles");

      const resp = await postSlots({
        availabilityWindows: [w1, w2],
        durationMinutes: 30,
        timezone: "America/Los_Angeles",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;

      // Should have slots from both windows
      expect(body.slots.length).toBeGreaterThanOrEqual(2);
    });

    test("SLT-001: overlapping availability windows do not produce duplicate slots", async () => {
      // Two windows that overlap by 2 hours (12:00-14:00)
      const w1 = weekdayWindow(7, 10, 14, "America/Los_Angeles");
      const w2 = weekdayWindow(7, 12, 16, "America/Los_Angeles");

      // Also request the union range as a single window for comparison
      const wUnion = weekdayWindow(7, 10, 16, "America/Los_Angeles");

      const [overlapResp, unionResp] = await Promise.all([
        postSlots({
          availabilityWindows: [w1, w2],
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        }),
        postSlots({
          availabilityWindows: [wUnion],
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        }),
      ]);

      expect(overlapResp.ok).toBe(true);
      expect(unionResp.ok).toBe(true);

      const overlapBody = (await overlapResp.json()) as SlotsResponse;
      const unionBody = (await unionResp.json()) as SlotsResponse;

      // The overlapping request should produce the same number of slots
      // as the equivalent single-window request — no duplicates
      expect(overlapBody.slots.length).toBe(unionBody.slots.length);

      // Verify no duplicate start times
      const starts = overlapBody.slots.map((s) => s.start);
      const uniqueStarts = new Set(starts);
      expect(uniqueStarts.size).toBe(starts.length);
    });

    test("SLT-001: handles many availability windows without error", async () => {
      // Build 20+ windows across different future weekdays to exercise
      // payload size and processing with a large input.
      const windows = Array.from({ length: 20 }, (_, i) =>
        weekdayWindow(i + 3, 10, 12, "America/New_York"),
      );

      const resp = await postSlots({
        availabilityWindows: windows,
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.ok).toBe(true);
      const body = (await resp.json()) as SlotsResponse;
      expect(Array.isArray(body.slots)).toBe(true);
    });

    test("rejects too many availability windows with 400", async () => {
      // Server enforces a max of 50 availability windows
      const windows = Array.from({ length: 51 }, (_, i) =>
        weekdayWindow(i + 3, 10, 12, "America/New_York"),
      );

      const resp = await postSlots({
        availabilityWindows: windows,
        durationMinutes: 30,
        timezone: "America/New_York",
      });

      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("Too many availability windows");
    });
  });
});
