module Michael.Domain

open System
open NodaTime

// ---------------------------------------------------------------------------
// Parser output types (mirrors the spike schema)
// ---------------------------------------------------------------------------

type AvailabilityWindow =
    { Start: OffsetDateTime
      End: OffsetDateTime
      Timezone: string option }

type ParseResult =
    { AvailabilityWindows: AvailabilityWindow list
      DurationMinutes: int option
      Title: string option
      Description: string option
      Name: string option
      Email: string option
      Phone: string option
      MissingFields: string list }

// ---------------------------------------------------------------------------
// Booking types
// ---------------------------------------------------------------------------

type BookingStatus =
    | Confirmed
    | Cancelled

type Booking =
    { Id: Guid
      ParticipantName: string
      ParticipantEmail: string
      ParticipantPhone: string option
      Title: string
      Description: string option
      StartTime: OffsetDateTime
      EndTime: OffsetDateTime
      DurationMinutes: int
      Timezone: string
      Status: BookingStatus
      CreatedAt: Instant }

// ---------------------------------------------------------------------------
// Availability types
// ---------------------------------------------------------------------------

type TimeSlot =
    { SlotStart: OffsetDateTime
      SlotEnd: OffsetDateTime }

type HostAvailabilitySlot =
    { Id: Guid
      DayOfWeek: IsoDayOfWeek
      StartTime: LocalTime
      EndTime: LocalTime
      Timezone: string }
