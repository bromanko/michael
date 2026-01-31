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
open Serilog
open Serilog.Events
open Michael.Database
open Michael.Handlers

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

            // Initialize schema with a temporary connection
            use initConn = createConn ()
            initializeDatabase initConn
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

            wapp.UseDefaultFiles() |> ignore
            wapp.UseStaticFiles() |> ignore
            wapp.UseSerilogRequestLogging() |> ignore
            wapp.UseRouting() |> ignore

            wapp.UseFalco(
                [ post "/api/parse" (handleParse httpClient geminiConfig)
                  post "/api/slots" (handleSlots createConn)
                  post "/api/book" (handleBook createConn) ]
            )
            |> ignore

            wapp.Run()
            0
        with ex ->
            Log.Fatal(ex, "Application terminated unexpectedly")
            1
    finally
        Log.CloseAndFlush()
