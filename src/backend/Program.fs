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
open Michael.Database
open Michael.Handlers
open Michael.AdminAuth
open Michael.AdminHandlers

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
                |> Option.defaultWith (fun () ->
                    failwith "MICHAEL_ADMIN_PASSWORD environment variable is required.")

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
                  post "/api/admin/bookings/{id}/cancel" (requireAdmin (handleCancelBooking createConn))
                  get "/api/admin/dashboard" (requireAdmin (handleDashboard createConn)) ]
            )
            |> ignore

            // SPA fallback for admin client-side routing
            wapp.MapFallback(
                "/admin/{**path}",
                RequestDelegate(fun ctx ->
                    let filePath =
                        System.IO.Path.Combine(wapp.Environment.WebRootPath, "admin", "index.html")

                    ctx.Response.ContentType <- "text/html"
                    ctx.Response.SendFileAsync(filePath)))
            |> ignore

            wapp.Run()
            0
        with ex ->
            Log.Fatal(ex, "Application terminated unexpectedly")
            1
    finally
        Log.CloseAndFlush()
