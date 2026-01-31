module Michael.Program

open System
open System.Net.Http
open System.Text.Json
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open NodaTime
open NodaTime.Serialization.SystemTextJson
open Michael.Domain
open Michael.Database
open Michael.Handlers

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    let jsonOptions = JsonSerializerOptions(JsonSerializerDefaults.Web)
    jsonOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb) |> ignore

    builder.Services
        .AddSingleton<JsonSerializerOptions>(jsonOptions)
    |> ignore

    let wapp = builder.Build()

    // Database
    let dbPath =
        Environment.GetEnvironmentVariable("MICHAEL_DB_PATH")
        |> Option.ofObj
        |> Option.defaultWith (fun () ->
            failwith "MICHAEL_DB_PATH environment variable is required.")

    let createConn () = createConnection dbPath

    // Host timezone
    let hostTzId =
        Environment.GetEnvironmentVariable("MICHAEL_HOST_TIMEZONE")
        |> Option.ofObj
        |> Option.defaultValue "America/New_York"

    let hostTz =
        match DateTimeZoneProviders.Tzdb.GetZoneOrNull(hostTzId) with
        | null -> failwith $"Invalid timezone in MICHAEL_HOST_TIMEZONE: {hostTzId}"
        | tz -> tz

    // Initialize schema with a temporary connection
    use initConn = createConn ()
    initializeDatabase initConn hostTzId

    // CalDAV sources (optional)
    let calDavSources =
        [ let fmUser = Environment.GetEnvironmentVariable("MICHAEL_CALDAV_FASTMAIL_USER")
          let fmPass = Environment.GetEnvironmentVariable("MICHAEL_CALDAV_FASTMAIL_PASSWORD")

          if not (String.IsNullOrEmpty(fmUser)) && not (String.IsNullOrEmpty(fmPass)) then
              { CalDavSourceConfig.Source =
                    { Id = Guid.Parse("00000000-0000-0000-0000-000000000001")
                      Provider = Fastmail
                      BaseUrl = "https://caldav.fastmail.com/dav/calendars"
                      CalendarHomeUrl = None }
                Username = fmUser
                Password = fmPass }

          let icUser = Environment.GetEnvironmentVariable("MICHAEL_CALDAV_ICLOUD_USER")
          let icPass = Environment.GetEnvironmentVariable("MICHAEL_CALDAV_ICLOUD_PASSWORD")

          if not (String.IsNullOrEmpty(icUser)) && not (String.IsNullOrEmpty(icPass)) then
              { CalDavSourceConfig.Source =
                    { Id = Guid.Parse("00000000-0000-0000-0000-000000000002")
                      Provider = ICloud
                      BaseUrl = "https://caldav.icloud.com/"
                      CalendarHomeUrl = None }
                Username = icUser
                Password = icPass } ]

    if not calDavSources.IsEmpty then
        use sourceConn = createConn ()

        for source in calDavSources do
            Database.upsertCalendarSource sourceConn source.Source

    let clock = NodaTime.SystemClock.Instance :> IClock

    use syncTimer =
        if calDavSources.IsEmpty then
            { new System.IDisposable with member _.Dispose() = () }
        else
            CalendarSync.startBackgroundSync createConn calDavSources hostTz clock

    let getCachedBlockers = CalendarSync.getCachedBlockers createConn

    // Gemini API
    use httpClient = new HttpClient()

    let geminiApiKey =
        Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        |> Option.ofObj
        |> Option.defaultWith (fun () ->
            failwith "GEMINI_API_KEY environment variable is required.")

    let geminiConfig : GeminiClient.GeminiConfig =
        { ApiKey = geminiApiKey
          Model = GeminiClient.defaultModel }

    wapp.UseDefaultFiles() |> ignore
    wapp.UseStaticFiles() |> ignore

    wapp.UseFalco(
        [
            post "/api/parse" (handleParse httpClient geminiConfig)
            post "/api/slots" (handleSlots createConn getCachedBlockers)
            post "/api/book" (handleBook createConn)
        ]
    )
    |> ignore

    wapp.Run()
    0
