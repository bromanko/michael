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

// ---------------------------------------------------------------------------
// CalDAV types
// ---------------------------------------------------------------------------

type CalDavProvider =
    | Fastmail
    | ICloud

type CalendarSource =
    { Id: Guid
      Provider: CalDavProvider
      BaseUrl: string
      CalendarHomeUrl: string option }

type CalendarSourceStatus =
    { Source: CalendarSource
      LastSyncedAt: Instant option
      LastSyncResult: string option }

type CalDavSourceConfig =
    { Source: CalendarSource
      Username: string
      Password: string }

type CachedEvent =
    { Id: Guid
      SourceId: Guid
      CalendarUrl: string
      Uid: string
      Summary: string
      StartInstant: Instant
      EndInstant: Instant
      IsAllDay: bool }

// ---------------------------------------------------------------------------
// Admin session types
// ---------------------------------------------------------------------------

type AdminSession =
    { Token: string
      CreatedAt: Instant
      ExpiresAt: Instant }
