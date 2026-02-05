module Michael.Program

open System
open System.Net.Http
open System.Text.Json
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open NodaTime
open NodaTime.Serialization.SystemTextJson
open Serilog
open Serilog.Events
open Michael.Domain
open Michael.Database
open Michael.CalDav
open Michael.CalendarSync
open Michael.Handlers
open Michael.AdminAuth
open Michael.AdminHandlers
open Michael.Email

[<EntryPoint>]
let main args =
    let environment =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        |> Option.ofObj
        |> Option.defaultValue "Production"

    let loggerConfig =
        LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()

    let loggerConfig =
        if environment = "Development" then
            loggerConfig.WriteTo.Console()
        else
            loggerConfig.WriteTo.Console(Serilog.Formatting.Compact.CompactJsonFormatter())

    // Configure Serilog early so startup errors are captured
    Log.Logger <- loggerConfig.CreateLogger()

    try
        try
            Log.Information("Starting Michael")

            let builder = WebApplication.CreateBuilder(args)

            // Replace default logging with Serilog
            builder.Host.UseSerilog() |> ignore

            let jsonOptions = JsonSerializerOptions(JsonSerializerDefaults.Web)
            jsonOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb) |> ignore

            builder.Services.AddSingleton<JsonSerializerOptions>(jsonOptions) |> ignore

            let wapp = builder.Build()

            // Database
            let dbPath =
                Environment.GetEnvironmentVariable("MICHAEL_DB_PATH")
                |> Option.ofObj
                |> Option.defaultValue "michael.db"

            let createConn () = createConnection dbPath

            let hostTimezone =
                Environment.GetEnvironmentVariable("MICHAEL_HOST_TIMEZONE")
                |> Option.ofObj
                |> Option.defaultWith (fun () -> failwith "MICHAEL_HOST_TIMEZONE environment variable is required.")

            // Initialize schema with a temporary connection
            use initConn = createConn ()
            initializeDatabase initConn hostTimezone
            Log.Information("Database initialized at {DbPath}", dbPath)

            // Gemini API
            let httpClient = new HttpClient()

            let geminiApiKey =
                Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                |> Option.ofObj
                |> Option.defaultWith (fun () -> failwith "GEMINI_API_KEY environment variable is required.")

            let geminiConfig: GeminiClient.GeminiConfig =
                { ApiKey = geminiApiKey
                  Model = GeminiClient.defaultModel }

            let adminPassword =
                Environment.GetEnvironmentVariable("MICHAEL_ADMIN_PASSWORD")
                |> Option.ofObj
                |> Option.defaultWith (fun () -> failwith "MICHAEL_ADMIN_PASSWORD environment variable is required.")
                |> hashPasswordAtStartup

            // SMTP configuration (optional — for sending email notifications)
            let smtpConfig: SmtpConfig option =
                let host = Environment.GetEnvironmentVariable("MICHAEL_SMTP_HOST") |> Option.ofObj
                let port = Environment.GetEnvironmentVariable("MICHAEL_SMTP_PORT") |> Option.ofObj

                let username =
                    Environment.GetEnvironmentVariable("MICHAEL_SMTP_USERNAME") |> Option.ofObj

                let password =
                    Environment.GetEnvironmentVariable("MICHAEL_SMTP_PASSWORD") |> Option.ofObj

                let fromAddress =
                    Environment.GetEnvironmentVariable("MICHAEL_SMTP_FROM") |> Option.ofObj

                let fromName =
                    Environment.GetEnvironmentVariable("MICHAEL_SMTP_FROM_NAME") |> Option.ofObj

                match host, port, username, password, fromAddress with
                | Some h, Some p, Some u, Some pw, Some from ->
                    match Int32.TryParse(p) with
                    | true, portNum ->
                        Log.Information("SMTP configured: {Host}:{Port}", h, portNum)

                        Some
                            { Host = h
                              Port = portNum
                              Username = u
                              Password = pw
                              FromAddress = from
                              FromName = fromName |> Option.defaultValue "Michael" }
                    | _ ->
                        Log.Warning("Invalid SMTP port: {Port}", p)
                        None
                | _ ->
                    Log.Information("SMTP not configured (email notifications disabled)")
                    None

            // CalDAV sources (optional — configured via env vars)
            // Generate a deterministic GUID from a key so the same CalDAV source
            // always gets the same ID across restarts, matching the DB record.
            // Uses first 16 bytes of SHA256 hash. Changing the key format will
            // orphan existing records (they'd need to be deleted and re-synced).
            let deterministicSourceId (key: string) =
                Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)).[0..15])

            let calDavSources =
                let fastmailUrl =
                    Environment.GetEnvironmentVariable("MICHAEL_CALDAV_FASTMAIL_URL")
                    |> Option.ofObj

                let fastmailUser =
                    Environment.GetEnvironmentVariable("MICHAEL_CALDAV_FASTMAIL_USERNAME")
                    |> Option.ofObj

                let fastmailPass =
                    Environment.GetEnvironmentVariable("MICHAEL_CALDAV_FASTMAIL_PASSWORD")
                    |> Option.ofObj

                let icloudUrl =
                    Environment.GetEnvironmentVariable("MICHAEL_CALDAV_ICLOUD_URL") |> Option.ofObj

                let icloudUser =
                    Environment.GetEnvironmentVariable("MICHAEL_CALDAV_ICLOUD_USERNAME")
                    |> Option.ofObj

                let icloudPass =
                    Environment.GetEnvironmentVariable("MICHAEL_CALDAV_ICLOUD_PASSWORD")
                    |> Option.ofObj

                [ match fastmailUrl, fastmailUser, fastmailPass with
                  | Some url, Some user, Some pass ->
                      { Source =
                          { Id = deterministicSourceId $"fastmail:{url}"
                            Provider = Fastmail
                            BaseUrl = url
                            CalendarHomeUrl = None }
                        Username = user
                        Password = pass }
                  | _ -> ()
                  match icloudUrl, icloudUser, icloudPass with
                  | Some url, Some user, Some pass ->
                      { Source =
                          { Id = deterministicSourceId $"icloud:{url}"
                            Provider = ICloud
                            BaseUrl = url
                            CalendarHomeUrl = None }
                        Username = user
                        Password = pass }
                  | _ -> () ]

            // Register CalDAV sources in the database
            for source in calDavSources do
                use regConn = createConn ()
                upsertCalendarSource regConn source.Source

            if calDavSources.IsEmpty then
                Log.Information("No CalDAV sources configured")
            else
                Log.Information("Registered {Count} CalDAV source(s)", calDavSources.Length)

            let hostTz = DateTimeZoneProviders.Tzdb.[hostTimezone]

            // Start background CalDAV sync
            let syncDisposable =
                if calDavSources.IsEmpty then
                    None
                else
                    Some(startBackgroundSync createConn calDavSources hostTz SystemClock.Instance)

            // Manual sync trigger for admin API
            let triggerSyncForSource (sourceId: Guid) =
                task {
                    let sourceConfig = calDavSources |> List.tryFind (fun s -> s.Source.Id = sourceId)

                    match sourceConfig with
                    | None -> return Error "Calendar source not configured."
                    | Some config ->
                        let now = SystemClock.Instance.GetCurrentInstant()
                        let syncEnd = now + Duration.FromDays(60)
                        use httpClient = createHttpClient config.Username config.Password
                        let! result = syncSource httpClient config.Source hostTz now syncEnd
                        use conn = createConn ()

                        match result with
                        | Ok events ->
                            match replaceEventsForSource conn config.Source.Id events with
                            | Ok() ->
                                updateSyncStatus conn config.Source.Id now "ok" |> ignore
                                return Ok()
                            | Error msg ->
                                updateSyncStatus conn config.Source.Id now $"error: {msg}" |> ignore
                                return Error msg
                        | Error msg ->
                            updateSyncStatus conn config.Source.Id now $"error: {msg}" |> ignore
                            return Error msg
                }

            wapp.UseDefaultFiles() |> ignore
            wapp.UseStaticFiles() |> ignore
            wapp.UseSerilogRequestLogging() |> ignore
            wapp.UseRouting() |> ignore

            let requireAdmin = requireAdminSession createConn

            wapp.UseFalco(
                [ // Booking API (public)
                  post "/api/parse" (handleParse httpClient geminiConfig)
                  post "/api/slots" (handleSlots createConn)
                  post "/api/book" (handleBook createConn)

                  // Admin auth (no session required)
                  post "/api/admin/login" (handleLogin createConn adminPassword)
                  post "/api/admin/logout" (handleLogout createConn)
                  get "/api/admin/session" (handleSessionCheck createConn)

                  // Admin API (session required)
                  get "/api/admin/bookings/{id}" (requireAdmin (handleGetBooking createConn))
                  get "/api/admin/bookings" (requireAdmin (handleListBookings createConn))
                  post "/api/admin/bookings/{id}/cancel" (requireAdmin (handleCancelBooking createConn smtpConfig))
                  get "/api/admin/dashboard" (requireAdmin (handleDashboard createConn))

                  // Calendar sources
                  get "/api/admin/calendars" (requireAdmin (handleListCalendarSources createConn))
                  post
                      "/api/admin/calendars/{id}/sync"
                      (requireAdmin (handleTriggerSync createConn triggerSyncForSource))

                  // Availability
                  get "/api/admin/availability" (requireAdmin (handleGetAvailability createConn))
                  put "/api/admin/availability" (requireAdmin (handlePutAvailability createConn))

                  // Settings
                  get "/api/admin/settings" (requireAdmin (handleGetSettings createConn))
                  put "/api/admin/settings" (requireAdmin (handlePutSettings createConn))

                  // Calendar view
                  get "/api/admin/calendar-view" (requireAdmin (handleCalendarView createConn hostTimezone)) ]
            )
            |> ignore

            // SPA fallback for admin client-side routing
            wapp.MapFallback(
                "/admin/{**path}",
                RequestDelegate(fun ctx ->
                    let filePath =
                        System.IO.Path.Combine(wapp.Environment.WebRootPath, "admin", "index.html")

                    ctx.Response.ContentType <- "text/html"
                    ctx.Response.SendFileAsync(filePath))
            )
            |> ignore

            wapp.Run()

            syncDisposable |> Option.iter (fun d -> d.Dispose())

            0
        with ex ->
            Log.Fatal(ex, "Application terminated unexpectedly")
            1
    finally
        Log.CloseAndFlush()
