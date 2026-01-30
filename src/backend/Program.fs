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
open Michael.Database
open Michael.Handlers

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

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
    wapp.UseRouting() |> ignore

    wapp.UseFalco(
        [ post "/api/parse" (handleParse httpClient geminiConfig)
          post "/api/slots" (handleSlots createConn)
          post "/api/book" (handleBook createConn) ]
    )
    |> ignore

    wapp.Run()
    0
