module Michael.Program

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
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
open Michael.RateLimiting

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

            // Limit request body size to prevent memory exhaustion from
            // oversized payloads. 256 KB is generous for our JSON APIs.
            builder.WebHost.ConfigureKestrel(fun options -> options.Limits.MaxRequestBodySize <- 256L * 1024L)
            |> ignore

            // Replace default logging with Serilog
            builder.Host.UseSerilog() |> ignore

            let jsonOptions = JsonSerializerOptions(JsonSerializerDefaults.Web)
            jsonOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb) |> ignore

            builder.Services.AddSingleton<JsonSerializerOptions>(jsonOptions) |> ignore

            let trustedProxyIps =
                Environment.GetEnvironmentVariable("MICHAEL_TRUSTED_PROXIES")
                |> Option.ofObj
                |> Option.map (fun raw ->
                    raw.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                    |> Array.map (fun ipText ->
                        match IPAddress.TryParse(ipText) with
                        | true, ip -> ip
                        | _ -> failwith $"Invalid IP in MICHAEL_TRUSTED_PROXIES: '{ipText}'"))
                |> Option.defaultValue [||]

            let forwardedHeadersEnabled = trustedProxyIps.Length > 0

            if forwardedHeadersEnabled then
                builder.Services.Configure<ForwardedHeadersOptions>(fun (options: ForwardedHeadersOptions) ->
                    options.ForwardedHeaders <- ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto

                    trustedProxyIps |> Array.iter options.KnownProxies.Add)
                |> ignore

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

            // Initialize schema via Atlas migrations
            let migrationsDir = Path.Combine(AppContext.BaseDirectory, "migrations")

            use initConn = createConn ()

            match initializeDatabase initConn migrationsDir SystemClock.Instance with
            | Error msg -> failwith $"Database migration failed: {msg}"
            | Ok() -> ()

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
            // Required: MICHAEL_SMTP_HOST, MICHAEL_SMTP_PORT, MICHAEL_SMTP_FROM
            // Optional: MICHAEL_SMTP_USERNAME, MICHAEL_SMTP_PASSWORD (omit for
            //           servers that don't require auth, e.g. Mailpit in dev)
            // Optional: MICHAEL_SMTP_TLS (default "starttls")
            //           Values: starttls, sslon (implicit TLS), none/false (no encryption)
            // Optional: MICHAEL_SMTP_FROM_NAME (default "Michael")
            let smtpConfig: SmtpConfig option =
                let getEnv name =
                    Environment.GetEnvironmentVariable(name) |> Option.ofObj

                match buildSmtpConfig getEnv with
                | Ok(Some config) ->
                    Log.Information(
                        "SMTP configured: {Host}:{Port} (TLS={TlsMode})",
                        config.Host,
                        config.Port,
                        config.TlsMode
                    )

                    Some config
                | Ok None ->
                    Log.Information("SMTP not configured (email notifications disabled)")
                    None
                | Error msg -> failwith msg

            // Notification config: bundles SMTP + host email + public URL.
            // Required when SMTP is configured; ignored otherwise.
            let notificationConfig: NotificationConfig option =
                match smtpConfig with
                | None -> None
                | Some smtp ->
                    let publicUrl =
                        Environment.GetEnvironmentVariable("MICHAEL_PUBLIC_URL") |> Option.ofObj

                    let hostEmail =
                        Environment.GetEnvironmentVariable("MICHAEL_HOST_EMAIL") |> Option.ofObj

                    let hostName =
                        Environment.GetEnvironmentVariable("MICHAEL_HOST_NAME") |> Option.ofObj

                    match buildNotificationConfig smtp publicUrl hostEmail hostName with
                    | Error msg -> failwith msg
                    | Ok config ->
                        Log.Information(
                            "Notification config: PublicUrl={PublicUrl}, HostEmail={HostEmail}, HostName={HostName}",
                            config.PublicUrl,
                            config.HostEmail,
                            config.HostName
                        )

                        Some config

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

            let clock: IClock = SystemClock.Instance
            let alwaysSecureCsrfCookie = environment <> "Development"

            let csrfSigningKeyRaw =
                Environment.GetEnvironmentVariable("MICHAEL_CSRF_SIGNING_KEY")
                |> Option.ofObj
                |> Option.defaultWith (fun () -> failwith "MICHAEL_CSRF_SIGNING_KEY environment variable is required.")

            if csrfSigningKeyRaw.Length < 32 then
                failwith "MICHAEL_CSRF_SIGNING_KEY must be at least 32 characters long."

            let csrfLifetimeMinutes =
                Environment.GetEnvironmentVariable("MICHAEL_CSRF_TOKEN_LIFETIME_MINUTES")
                |> Option.ofObj
                |> Option.map (fun raw ->
                    match Int32.TryParse(raw) with
                    | true, minutes when minutes > 0 -> minutes
                    | _ -> failwith "MICHAEL_CSRF_TOKEN_LIFETIME_MINUTES must be a positive integer when provided.")
                |> Option.defaultValue 120

            let csrfConfig: CsrfConfig =
                { SigningKey = System.Text.Encoding.UTF8.GetBytes(csrfSigningKeyRaw)
                  Lifetime = Duration.FromMinutes(int64 csrfLifetimeMinutes)
                  AlwaysSecureCookie = alwaysSecureCsrfCookie }

            // Start background CalDAV sync
            let syncDisposable =
                if calDavSources.IsEmpty then
                    None
                else
                    Some(startBackgroundSync createConn calDavSources hostTz clock)

            // Manual sync trigger for admin API
            let triggerSyncForSource (sourceId: Guid) =
                task {
                    let sourceConfig = calDavSources |> List.tryFind (fun s -> s.Source.Id = sourceId)

                    match sourceConfig with
                    | None -> return Error "Calendar source not configured."
                    | Some config ->
                        let now = clock.GetCurrentInstant()
                        let syncEnd = now + Duration.FromDays(60)
                        use httpClient = createHttpClient config.Username config.Password
                        let! result = syncSource httpClient config.Source hostTz now syncEnd
                        use conn = createConn ()

                        match result with
                        | Ok events ->
                            match replaceEventsForSource conn config.Source.Id events with
                            | Ok() ->
                                updateSyncStatus conn config.Source.Id now "ok" |> ignore
                                recordSyncHistory conn config.Source.Id now "ok" None |> ignore
                                pruneOldSyncHistory conn config.Source.Id 50
                                return Ok()
                            | Error msg ->
                                updateSyncStatus conn config.Source.Id now $"error: {msg}" |> ignore
                                recordSyncHistory conn config.Source.Id now "error" (Some msg) |> ignore
                                pruneOldSyncHistory conn config.Source.Id 50
                                return Error msg
                        | Error msg ->
                            updateSyncStatus conn config.Source.Id now $"error: {msg}" |> ignore
                            recordSyncHistory conn config.Source.Id now "error" (Some msg) |> ignore
                            pruneOldSyncHistory conn config.Source.Id 50
                            return Error msg
                }

            // Rate limit policies — per-client-IP token buckets.
            // "parse" is tighter because each call invokes the LLM.
            registerPolicy
                "parse"
                { TokenLimit = 10
                  TokensPerPeriod = 5
                  ReplenishmentPeriod = TimeSpan.FromMinutes(1.0) }

            registerPolicy
                "book"
                { TokenLimit = 10
                  TokensPerPeriod = 5
                  ReplenishmentPeriod = TimeSpan.FromMinutes(1.0) }

            registerPolicy
                "slots"
                { TokenLimit = 30
                  TokensPerPeriod = 15
                  ReplenishmentPeriod = TimeSpan.FromMinutes(1.0) }

            if forwardedHeadersEnabled then
                wapp.UseForwardedHeaders() |> ignore

            wapp.UseDefaultFiles() |> ignore
            wapp.UseStaticFiles() |> ignore
            wapp.UseSerilogRequestLogging() |> ignore
            wapp.UseRouting() |> ignore

            let requireAdmin = requireAdminSession createConn clock
            let requireCsrf = requireCsrfToken csrfConfig clock
            let rateLimit = requireRateLimit

            let getVideoLink () =
                use conn = createConn ()
                (getSchedulingSettings conn).VideoLink

            wapp.UseFalco(
                [ // Booking API (public)
                  get "/api/csrf-token" (handleCsrfToken csrfConfig clock)
                  post "/api/parse" (requireCsrf (rateLimit "parse" (handleParse httpClient geminiConfig clock)))
                  post "/api/slots" (requireCsrf (rateLimit "slots" (handleSlots createConn hostTz clock)))
                  post
                      "/api/book"
                      (requireCsrf (
                          rateLimit
                              "book"
                              (handleBook
                                  createConn
                                  hostTz
                                  clock
                                  notificationConfig
                                  getVideoLink
                                  sendBookingConfirmationEmail)
                      ))

                  // Admin auth (no session required)
                  post "/api/admin/login" (handleLogin createConn adminPassword clock)
                  post "/api/admin/logout" (handleLogout createConn)
                  get "/api/admin/session" (handleSessionCheck createConn clock)

                  // Admin API (session required)
                  get "/api/admin/bookings/{id}" (requireAdmin (handleGetBooking createConn))
                  get "/api/admin/bookings" (requireAdmin (handleListBookings createConn))
                  post
                      "/api/admin/bookings/{id}/cancel"
                      (requireAdmin (
                          handleCancelBooking createConn clock notificationConfig sendBookingCancellationEmail
                      ))
                  get "/api/admin/dashboard" (requireAdmin (handleDashboard createConn clock))

                  // Calendar sources
                  get "/api/admin/calendars" (requireAdmin (handleListCalendarSources createConn))
                  get "/api/admin/calendars/{id}/history" (requireAdmin (handleGetSyncHistory createConn))
                  post
                      "/api/admin/calendars/{id}/sync"
                      (requireAdmin (handleTriggerSync createConn triggerSyncForSource))

                  // Availability
                  get "/api/admin/availability" (requireAdmin (handleGetAvailability createConn hostTimezone))
                  put "/api/admin/availability" (requireAdmin (handlePutAvailability createConn hostTimezone))

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
