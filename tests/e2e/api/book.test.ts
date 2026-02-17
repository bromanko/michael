import { describe, test, expect, beforeAll } from "vitest";
import type { TestContext } from "vitest";
import {
  fetchCsrfToken,
  postWithCsrf,
  isDestructiveSkipped,
  CsrfContext,
  mapWithConcurrency,
  PROBE_CONCURRENCY,
} from "../helpers/api-client";
import type {
  ErrorResponse,
  ErrorCodeResponse,
  SlotsResponse,
  BookingResponse,
} from "../helpers/api-types";
import { utcOffsetFor } from "../helpers/tz";

// ---------------------------------------------------------------------------
// /api/book — Booking Creation
//
// Covers EARS requirements: BKC-001, BKC-002, BKC-010..BKC-016
//                           VAL-001, VAL-002, VAL-003, VAL-004
// ---------------------------------------------------------------------------

/** First day offset when probing for an available slot. */
const SLOT_PROBE_START_DAY = 3;
/** Last day offset (inclusive) when probing for an available slot. */
const SLOT_PROBE_END_DAY = 14;

/** First day offset for exclusion / conflict probes (further out to
 *  avoid interference from bookings created by other tests). */
const CONFLICT_PROBE_START_DAY = 5;
/** Last day offset (inclusive) for conflict probes. */
const CONFLICT_PROBE_END_DAY = 20;

/** Fetch a real available slot from /api/slots for a future weekday.
 *  Probes candidate days concurrently to minimise wall-clock time. */
async function fetchAvailableSlot(
  csrf: CsrfContext,
  durationMinutes: number = 30,
): Promise<{ start: string; end: string } | null> {
  const candidates: {
    daysOut: number;
    payload: {
      availabilityWindows: Array<{ start: string; end: string }>;
      durationMinutes: number;
      timezone: string;
    };
  }[] = [];

  for (
    let daysOut = SLOT_PROBE_START_DAY;
    daysOut <= SLOT_PROBE_END_DAY;
    daysOut++
  ) {
    const d = new Date();
    d.setUTCDate(d.getUTCDate() + daysOut);
    const day = d.getUTCDay();
    if (day === 0 || day === 6) continue; // skip weekends

    const yyyy = d.getUTCFullYear();
    const mm = String(d.getUTCMonth() + 1).padStart(2, "0");
    const dd = String(d.getUTCDate()).padStart(2, "0");
    const offset = utcOffsetFor(d, "America/Los_Angeles");

    candidates.push({
      daysOut,
      payload: {
        availabilityWindows: [
          {
            start: `${yyyy}-${mm}-${dd}T09:00:00${offset}`,
            end: `${yyyy}-${mm}-${dd}T17:00:00${offset}`,
          },
        ],
        durationMinutes,
        timezone: "America/Los_Angeles",
      },
    });
  }

  // Probe with limited concurrency to avoid overwhelming the server
  const results = await mapWithConcurrency(
    candidates,
    PROBE_CONCURRENCY,
    async ({ daysOut, payload }) => {
      const resp = await postWithCsrf("/api/slots", payload, csrf);
      if (!resp.ok) return { daysOut, slot: null };
      const body = (await resp.json()) as SlotsResponse;
      // Pick last slot to avoid earlier-booked slots
      const slot =
        body.slots.length > 0 ? body.slots[body.slots.length - 1] : null;
      return { daysOut, slot };
    },
  );

  // Return the first hit (earliest day)
  const match = results.find((r) => r.slot !== null);
  return match?.slot ?? null;
}

/** Generate a unique email for this test run to avoid collisions. */
function uniqueEmail(): string {
  const ts = Date.now();
  const rand = Math.floor(Math.random() * 10000);
  return `e2e-${ts}-${rand}@test.example.com`;
}

describe("/api/book", () => {
  let csrf: CsrfContext;

  beforeAll(async () => {
    csrf = await fetchCsrfToken();
  });

  function postBook(body: unknown): Promise<Response> {
    return postWithCsrf("/api/book", body, csrf);
  }

  // -------------------------------------------------------------------------
  // Validation (BKC-010..BKC-015, VAL-001..VAL-004)
  // -------------------------------------------------------------------------

  describe("validation", () => {
    test("rejects empty request body with 400", async () => {
      const resp = await postBook({});
      expect(resp.status).toBe(400);
    });

    // Shared slot for validation tests (doesn't need to be real — validation
    // happens before slot-availability check)
    const validSlot = {
      start: "2026-02-20T13:00:00-05:00",
      end: "2026-02-20T13:30:00-05:00",
    };
    const base = {
      name: "Jane",
      email: "jane@test.example.com",
      title: "Test Meeting",
      slot: validSlot,
      durationMinutes: 30,
      timezone: "America/New_York",
    };

    test("BKC-010: rejects empty name with 400", async () => {
      const resp = await postBook({ ...base, name: "" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Name is required.");
    });

    test("BKC-010: rejects whitespace-only name with 400", async () => {
      const resp = await postBook({ ...base, name: "   " });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Name is required.");
    });

    test("BKC-011: rejects empty email with 400", async () => {
      const resp = await postBook({ ...base, email: "" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("BKC-011: rejects whitespace-only email with 400", async () => {
      const resp = await postBook({ ...base, email: "   " });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("BKC-011: rejects invalid email with 400", async () => {
      const resp = await postBook({ ...base, email: "bad-email" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("VAL-003: rejects email missing @", async () => {
      const resp = await postBook({ ...base, email: "aliceexample.com" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("VAL-003: rejects email with no domain dot", async () => {
      const resp = await postBook({ ...base, email: "alice@localhost" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("VAL-003: rejects email ending with dot", async () => {
      const resp = await postBook({ ...base, email: "alice@example." });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("VAL-003: rejects email with empty local part", async () => {
      const resp = await postBook({ ...base, email: "@example.com" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("VAL-003: rejects email with multiple @", async () => {
      const resp = await postBook({
        ...base,
        email: "alice@bob@example.com",
      });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("BKC-012: rejects empty title with 400", async () => {
      const resp = await postBook({ ...base, title: "" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Title is required.");
    });

    test("BKC-012: rejects whitespace-only title with 400", async () => {
      const resp = await postBook({ ...base, title: "   " });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Title is required.");
    });

    test("BKC-013: rejects duration below 5 with 400", async () => {
      const resp = await postBook({ ...base, durationMinutes: 2 });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("BKC-013: rejects duration above 480 with 400", async () => {
      const resp = await postBook({ ...base, durationMinutes: 500 });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("DurationMinutes must be between 5 and 480.");
    });

    test("VAL-004: rejects negative duration with 400", async () => {
      const resp = await postBook({ ...base, durationMinutes: -1 });
      expect(resp.status).toBe(400);
    });

    test("VAL-004: rejects duration 4 (below boundary)", async () => {
      const resp = await postBook({ ...base, durationMinutes: 4 });
      expect(resp.status).toBe(400);
    });

    test("VAL-004: rejects duration 481 (above boundary)", async () => {
      const resp = await postBook({ ...base, durationMinutes: 481 });
      expect(resp.status).toBe(400);
    });

    test("BKC-014/VAL-001: rejects null slot with 400", async () => {
      const resp = await postBook({ ...base, slot: null });
      expect(resp.status).toBe(400);
    });

    test("BKC-014/VAL-001: rejects missing slot with 400", async () => {
      const { slot: _, ...noSlot } = base;
      const resp = await postBook(noSlot);
      expect(resp.status).toBe(400);
    });

    test("BKC-014/VAL-001: rejects slot missing end with 400", async () => {
      const resp = await postBook({
        ...base,
        slot: { start: "2026-02-20T13:00:00-05:00" },
      });
      expect(resp.status).toBe(400);
    });

    test("BKC-014/VAL-001: rejects slot missing start with 400", async () => {
      const resp = await postBook({
        ...base,
        slot: { end: "2026-02-20T13:30:00-05:00" },
      });
      expect(resp.status).toBe(400);
    });

    test("BKC-014: rejects slot with end before start", async () => {
      const resp = await postBook({
        ...base,
        slot: {
          start: "2026-02-20T14:00:00-05:00",
          end: "2026-02-20T13:00:00-05:00",
        },
      });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Slot.End must be after Slot.Start.");
    });

    test("BKC-014: rejects slot with end equal to start", async () => {
      const resp = await postBook({
        ...base,
        slot: {
          start: "2026-02-20T13:00:00-05:00",
          end: "2026-02-20T13:00:00-05:00",
        },
      });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Slot.End must be after Slot.Start.");
    });

    test("BKC-015: rejects slot duration mismatch", async () => {
      const resp = await postBook({
        ...base,
        slot: {
          start: "2026-02-20T13:00:00-05:00",
          end: "2026-02-20T14:00:00-05:00", // 60 minutes
        },
        durationMinutes: 30, // mismatch
      });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Slot duration must match DurationMinutes.");
    });

    test("VAL-001: rejects malformed slot.start datetime", async () => {
      const resp = await postBook({
        ...base,
        slot: { start: "not-a-date", end: "2026-02-20T13:30:00-05:00" },
      });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("Slot.Start");
      expect(body.error).toContain("not-a-date");
    });

    test("VAL-001: rejects malformed slot.end datetime", async () => {
      const resp = await postBook({
        ...base,
        slot: { start: "2026-02-20T13:00:00-05:00", end: "bad-end" },
      });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("Slot.End");
      expect(body.error).toContain("bad-end");
    });

    test("VAL-002: rejects invalid timezone", async () => {
      const resp = await postBook({ ...base, timezone: "Fake/Zone" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toContain("Fake/Zone");
    });

    test("BKC-010: rejects missing name with 400", async () => {
      const { name: _, ...noName } = base;
      const resp = await postBook(noName);
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Name is required.");
    });

    test("BKC-011: rejects missing email with 400", async () => {
      const { email: _, ...noEmail } = base;
      const resp = await postBook(noEmail);
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("A valid email address is required.");
    });

    test("BKC-012: rejects missing title with 400", async () => {
      const { title: _, ...noTitle } = base;
      const resp = await postBook(noTitle);
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Title is required.");
    });

    test("BKC-013: rejects missing durationMinutes with 400", async () => {
      const { durationMinutes: _, ...noDuration } = base;
      const resp = await postBook(noDuration);
      expect(resp.status).toBe(400);
    });

    test("VAL-002: rejects missing timezone with 400", async () => {
      const { timezone: _, ...noTimezone } = base;
      const resp = await postBook(noTimezone);
      expect(resp.status).toBe(400);
    });

    test("rejects name containing only control characters with 400", async () => {
      const resp = await postBook({ ...base, name: "\r\n\t" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Name is required.");
    });

    test("rejects title containing only control characters with 400", async () => {
      const resp = await postBook({ ...base, title: "\r\n" });
      expect(resp.status).toBe(400);
      const body = (await resp.json()) as ErrorResponse;
      expect(body.error).toBe("Title is required.");
    });

    test("strips control characters from name before validation", async () => {
      // Name with embedded newlines should be sanitized, not rejected
      // (the visible characters still form a valid name)
      const resp = await postBook({ ...base, name: "Jane\r\nDoe" });
      // Should not get 400 for name — sanitized to "JaneDoe"
      // (may still get 400 for other reasons like slot unavailability,
      // but not for "Name is required.")
      if (resp.status === 400) {
        const body = (await resp.json()) as ErrorResponse;
        expect(body.error).not.toBe("Name is required.");
      }
    });

    test("accepts name at maximum length", async () => {
      const resp = await postBook({ ...base, name: "A".repeat(200) });
      // Should pass name validation (may fail on slot availability)
      if (resp.status === 400) {
        const body = (await resp.json()) as ErrorResponse;
        expect(body.error).not.toBe("Name is required.");
      }
    });
  });

  // -------------------------------------------------------------------------
  // Successful booking (BKC-001) — destructive (creates real bookings)
  // -------------------------------------------------------------------------

  describe("successful booking", () => {
    const destructive = test.skipIf(isDestructiveSkipped());

    destructive(
      "BKC-001: creates a confirmed booking and returns bookingId",
      async (ctx: TestContext) => {
        const slot = await fetchAvailableSlot(csrf);
        if (!slot) {
          ctx.skip("No available slots in the next 2 weeks");
          return;
        }

        const resp = await postBook({
          name: "E2E Booking Test",
          email: uniqueEmail(),
          title: "E2E Confirmed Booking",
          slot,
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        });

        expect(resp.status).toBe(200);
        const body = (await resp.json()) as BookingResponse;
        expect(body.confirmed).toBe(true);
        expect(typeof body.bookingId).toBe("string");
        // BookingId should be a valid GUID
        expect(body.bookingId).toMatch(
          /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
        );
        // Response shape must contain exactly these keys — no echoed PII
        // or unexpected fields.
        expect(Object.keys(body).sort()).toEqual(["bookingId", "confirmed"]);
      },
    );

    destructive(
      "BKC-001: accepts booking with optional phone number",
      async (ctx: TestContext) => {
        const slot = await fetchAvailableSlot(csrf);
        if (!slot) {
          ctx.skip("No available slots found");
          return;
        }

        const resp = await postBook({
          name: "Phone Booking",
          email: uniqueEmail(),
          phone: "555-867-5309",
          title: "Booking With Phone",
          slot,
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        });

        expect(resp.status).toBe(200);
        const body = (await resp.json()) as BookingResponse;
        expect(body.confirmed).toBe(true);
        expect(Object.keys(body).sort()).toEqual(["bookingId", "confirmed"]);
      },
    );

    destructive(
      "BKC-001: accepts booking with null phone",
      async (ctx: TestContext) => {
        const slot = await fetchAvailableSlot(csrf);
        if (!slot) {
          ctx.skip("No available slots found");
          return;
        }

        const resp = await postBook({
          name: "Null Phone",
          email: uniqueEmail(),
          phone: null,
          title: "Booking Null Phone",
          slot,
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        });

        expect(resp.status).toBe(200);
        const body = (await resp.json()) as BookingResponse;
        expect(body.confirmed).toBe(true);
        expect(Object.keys(body).sort()).toEqual(["bookingId", "confirmed"]);
      },
    );

    destructive(
      "BKC-001: accepts boundary duration 5",
      async (ctx: TestContext) => {
        const slot = await fetchAvailableSlot(csrf, 5);
        if (!slot) {
          ctx.skip("No available 5-min slots found");
          return;
        }

        const resp = await postBook({
          name: "Quick Chat",
          email: uniqueEmail(),
          title: "5 Minute Chat",
          slot,
          durationMinutes: 5,
          timezone: "America/Los_Angeles",
        });

        expect(resp.status).toBe(200);
        const body = (await resp.json()) as BookingResponse;
        expect(body.confirmed).toBe(true);
        expect(Object.keys(body).sort()).toEqual(["bookingId", "confirmed"]);
      },
    );
  });

  // -------------------------------------------------------------------------
  // Conflict detection (BKC-002, BKC-016)
  // -------------------------------------------------------------------------

  describe("conflict detection", () => {
    const destructive = test.skipIf(isDestructiveSkipped());

    destructive(
      "BKC-016: returns 409 when slot is already booked",
      async (ctx: TestContext) => {
        const slot = await fetchAvailableSlot(csrf);
        if (!slot) {
          ctx.skip("No available slots found");
          return;
        }

        // Book the slot
        const resp1 = await postBook({
          name: "First Booker",
          email: uniqueEmail(),
          title: "First Booking",
          slot,
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        });
        expect(resp1.status).toBe(200);

        // Try to book the same slot again
        const resp2 = await postBook({
          name: "Second Booker",
          email: uniqueEmail(),
          title: "Conflicting Booking",
          slot,
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        });

        expect(resp2.status).toBe(409);
        const body = (await resp2.json()) as ErrorCodeResponse;
        expect(body.code).toBe("slot_unavailable");
        expect(body.error).toBeTruthy();
      },
    );

    destructive(
      "SLT-002: booked slot is excluded from subsequent slot queries",
      async (ctx: TestContext) => {
        // Find a future weekday with available slots in a 2-hour window.
        // Probe all candidates concurrently to minimise wall-clock time.
        type WindowCandidate = { start: string; end: string };
        const windowCandidates: WindowCandidate[] = [];

        for (
          let daysOut = CONFLICT_PROBE_START_DAY;
          daysOut <= CONFLICT_PROBE_END_DAY;
          daysOut++
        ) {
          const d = new Date();
          d.setUTCDate(d.getUTCDate() + daysOut);
          const day = d.getUTCDay();
          if (day === 0 || day === 6) continue;

          const yyyy = d.getUTCFullYear();
          const mm = String(d.getUTCMonth() + 1).padStart(2, "0");
          const dd = String(d.getUTCDate()).padStart(2, "0");
          const offset = utcOffsetFor(d, "America/Los_Angeles");

          windowCandidates.push({
            start: `${yyyy}-${mm}-${dd}T13:00:00${offset}`,
            end: `${yyyy}-${mm}-${dd}T15:00:00${offset}`,
          });
        }

        const probeResults = await mapWithConcurrency(
          windowCandidates,
          PROBE_CONCURRENCY,
          async (candidate) => {
            const resp = await postWithCsrf(
              "/api/slots",
              {
                availabilityWindows: [candidate],
                durationMinutes: 30,
                timezone: "America/Los_Angeles",
              },
              csrf,
            );
            if (!resp.ok) return null;
            const body = (await resp.json()) as SlotsResponse;
            return body.slots.length >= 2 ? candidate : null;
          },
        );

        const testWindow = probeResults.find((w) => w !== null) ?? null;

        if (!testWindow) {
          ctx.skip("No window with 2+ slots found");
          return;
        }

        // Get available slots before booking
        const slotsResp1 = await postWithCsrf(
          "/api/slots",
          {
            availabilityWindows: [testWindow],
            durationMinutes: 30,
            timezone: "America/Los_Angeles",
          },
          csrf,
        );
        expect(slotsResp1.ok).toBe(true);
        const before = (await slotsResp1.json()) as SlotsResponse;

        if (before.slots.length === 0) {
          ctx.skip("No slots in window");
          return;
        }

        const slotToBook = before.slots[0];

        // Book one slot
        const bookResp = await postBook({
          name: "Slot Exclusion Test",
          email: uniqueEmail(),
          title: "Exclusion Test",
          slot: slotToBook,
          durationMinutes: 30,
          timezone: "America/Los_Angeles",
        });
        expect(bookResp.status).toBe(200);

        // Get slots again — the booked slot should be gone
        const slotsResp2 = await postWithCsrf(
          "/api/slots",
          {
            availabilityWindows: [testWindow],
            durationMinutes: 30,
            timezone: "America/Los_Angeles",
          },
          csrf,
        );
        expect(slotsResp2.ok).toBe(true);
        const after = (await slotsResp2.json()) as SlotsResponse;

        expect(after.slots.length).toBeLessThan(before.slots.length);

        // The specific booked slot should not appear
        const bookedStart = new Date(slotToBook.start).getTime();
        const stillPresent = after.slots.some(
          (s) => new Date(s.start).getTime() === bookedStart,
        );
        expect(stillPresent).toBe(false);
      },
    );
  });
});
