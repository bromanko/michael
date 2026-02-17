// ---------------------------------------------------------------------------
// Shared response DTOs for E2E API tests
//
// Centralises the type assertions used across test files so a response
// shape change only needs to be updated in one place.
// ---------------------------------------------------------------------------

/** Standard error envelope returned by all 4xx/5xx responses. */
export interface ErrorResponse {
  error: string;
}

/** Extended error envelope with a machine-readable code (e.g. 409 conflict). */
export interface ErrorCodeResponse extends ErrorResponse {
  code: string;
}

/** GET /api/slots response. */
export interface SlotsResponse {
  slots: Array<{ start: string; end: string }>;
}

/** POST /api/book success response. */
export interface BookingResponse {
  bookingId: string;
  confirmed: boolean;
}

/** POST /api/parse success response. */
export interface ParseResponse {
  parseResult: {
    availabilityWindows: Array<{
      start: string;
      end: string;
      timezone: string;
    }>;
    missingFields: string[];
    durationMinutes: number | null;
    title: string | null;
    description: string | null;
    name: string | null;
    email: string | null;
    phone: string | null;
  };
  systemMessage: string;
}
