module FakeCalDav.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open NodaTime
open FakeCalDav.Scenario
open FakeCalDav.Handlers

[<EntryPoint>]
let main args =
    let scenarioPath =
        Environment.GetEnvironmentVariable("FAKE_CALDAV_SCENARIO")
        |> Option.ofObj
        |> Option.defaultWith (fun () -> failwith "FAKE_CALDAV_SCENARIO environment variable is required.")

    let timezone =
        Environment.GetEnvironmentVariable("FAKE_CALDAV_TIMEZONE")
        |> Option.ofObj
        |> Option.defaultValue "America/Los_Angeles"

    let port =
        Environment.GetEnvironmentVariable("FAKE_CALDAV_PORT")
        |> Option.ofObj
        |> Option.defaultValue "9876"

    let tz = DateTimeZoneProviders.Tzdb.[timezone]
    let clock: IClock = SystemClock.Instance

    let scenarioDef = loadScenario scenarioPath
    let scenario = resolveScenario clock tz scenarioDef

    printfn $"FakeCalDav starting"
    printfn $"  Scenario: {scenarioPath} — {scenario.Description}"
    printfn $"  Timezone: {timezone}"
    printfn $"  Port:     {port}"

    for cal in scenario.Calendars do
        printfn $"  Calendar: {cal.Name} ({cal.Slug}) — {cal.Events.Length} event(s)"

        for evt in cal.Events do
            let timeStr =
                if evt.IsAllDay then
                    $"all-day {evt.Start.Date}"
                else
                    $"{evt.Start} → {evt.End}"

            printfn $"    • {evt.Summary} [{timeStr}]"

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseUrls($"http://localhost:{port}") |> ignore
    let app = builder.Build()

    app.Run(RequestDelegate(fun ctx -> handleRequest scenario ctx))

    app.Run()

    0
