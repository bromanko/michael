module FakeCalDav.Scenario

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open NodaTime

// ---------------------------------------------------------------------------
// Scenario types — loaded from JSON
// ---------------------------------------------------------------------------

type EventDef =
    { [<JsonPropertyName("summary")>]
      Summary: string
      [<JsonPropertyName("dayOffset")>]
      DayOffset: int
      [<JsonPropertyName("startTime")>]
      StartTime: string
      [<JsonPropertyName("durationMinutes")>]
      DurationMinutes: int
      [<JsonPropertyName("isAllDay")>]
      IsAllDay: bool
      [<JsonPropertyName("uid")>]
      Uid: string option }

type CalendarDef =
    { [<JsonPropertyName("name")>]
      Name: string
      [<JsonPropertyName("slug")>]
      Slug: string
      [<JsonPropertyName("events")>]
      Events: EventDef list }

type ScenarioDef =
    { [<JsonPropertyName("description")>]
      Description: string
      [<JsonPropertyName("calendars")>]
      Calendars: CalendarDef list }

// ---------------------------------------------------------------------------
// Resolved types — times computed relative to "now"
// ---------------------------------------------------------------------------

type ResolvedEvent =
    { Uid: string
      Summary: string
      Start: ZonedDateTime
      End: ZonedDateTime
      IsAllDay: bool }

type ResolvedCalendar =
    { Name: string
      Slug: string
      Events: ResolvedEvent list }

type ResolvedScenario =
    { Description: string
      Calendars: ResolvedCalendar list }

// ---------------------------------------------------------------------------
// Loading and resolving
// ---------------------------------------------------------------------------

let private parseTimeOfDay (s: string) =
    let parts = s.Split(':')
    LocalTime(int parts.[0], int parts.[1])

let resolveEvent (today: LocalDate) (tz: DateTimeZone) (evt: EventDef) : ResolvedEvent =
    let uid = evt.Uid |> Option.defaultWith (fun () -> Guid.NewGuid().ToString())

    if evt.IsAllDay then
        let date = today.PlusDays(evt.DayOffset)
        let startZoned = tz.AtStartOfDay(date)
        let endZoned = tz.AtStartOfDay(date.PlusDays(1))

        { Uid = uid
          Summary = evt.Summary
          Start = startZoned
          End = endZoned
          IsAllDay = true }
    else
        let date = today.PlusDays(evt.DayOffset)
        let time = parseTimeOfDay evt.StartTime
        let localStart = date.At(time)

        let startZoned = tz.AtStrictly(localStart)

        let localEnd = localStart.PlusMinutes(int64 evt.DurationMinutes)
        let endZoned = tz.AtStrictly(localEnd)

        { Uid = uid
          Summary = evt.Summary
          Start = startZoned
          End = endZoned
          IsAllDay = false }

/// Advance to the next weekday (Monday–Friday). If `date` is already
/// a weekday it is returned unchanged.
let private nextWeekday (date: LocalDate) =
    match date.DayOfWeek with
    | IsoDayOfWeek.Saturday -> date.PlusDays(2)
    | IsoDayOfWeek.Sunday -> date.PlusDays(1)
    | _ -> date

let resolveScenario (clock: IClock) (tz: DateTimeZone) (scenario: ScenarioDef) : ResolvedScenario =
    let today = clock.GetCurrentInstant().InZone(tz).Date |> nextWeekday

    { Description = scenario.Description
      Calendars =
        scenario.Calendars
        |> List.map (fun cal ->
            { Name = cal.Name
              Slug = cal.Slug
              Events = cal.Events |> List.map (resolveEvent today tz) }) }

let loadScenario (path: string) : ScenarioDef =
    let json = File.ReadAllText(path)

    let options = JsonSerializerOptions(JsonSerializerDefaults.Web)
    options.PropertyNameCaseInsensitive <- true

    JsonSerializer.Deserialize<ScenarioDef>(json, options)
