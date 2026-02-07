module FakeCalDav.IcsGenerator

open System
open System.Text
open FakeCalDav.Scenario

/// Format a ZonedDateTime as an ICS UTC timestamp (yyyyMMddTHHmmssZ).
let private toIcsUtc (zdt: NodaTime.ZonedDateTime) =
    let utc = zdt.ToInstant().ToDateTimeUtc()
    utc.ToString("yyyyMMdd'T'HHmmss'Z'")

/// Format a LocalDate as an ICS date value (yyyyMMdd).
let private toIcsDate (zdt: NodaTime.ZonedDateTime) =
    let d = zdt.Date
    sprintf "%04d%02d%02d" d.Year d.Month d.Day

/// Generate a VCALENDAR string containing a single VEVENT.
let generateIcs (evt: ResolvedEvent) : string =
    let sb = StringBuilder()
    sb.AppendLine("BEGIN:VCALENDAR") |> ignore
    sb.AppendLine("VERSION:2.0") |> ignore
    sb.AppendLine("PRODID:-//FakeCalDav//EN") |> ignore
    sb.AppendLine("BEGIN:VEVENT") |> ignore
    sb.AppendLine($"UID:{evt.Uid}") |> ignore

    if evt.IsAllDay then
        sb.AppendLine($"DTSTART;VALUE=DATE:{toIcsDate evt.Start}") |> ignore
        sb.AppendLine($"DTEND;VALUE=DATE:{toIcsDate evt.End}") |> ignore
    else
        sb.AppendLine($"DTSTART:{toIcsUtc evt.Start}") |> ignore
        sb.AppendLine($"DTEND:{toIcsUtc evt.End}") |> ignore

    sb.AppendLine($"SUMMARY:{evt.Summary}") |> ignore
    sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}") |> ignore
    sb.AppendLine("END:VEVENT") |> ignore
    sb.AppendLine("END:VCALENDAR") |> ignore
    sb.ToString()
