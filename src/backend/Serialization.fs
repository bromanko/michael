module Michael.Serialization

open System.Text.Json
open System.Text.Json.Serialization
open NodaTime
open NodaTime.Serialization.SystemTextJson

/// Build the canonical JsonSerializerOptions for the application.
/// Uses Web defaults (camelCase, case-insensitive) with NodaTime and
/// FSharp.SystemTextJson converters. Called from Program.fs at startup
/// and from test helpers to keep both in sync.
let buildJsonOptions () =
    let options = JsonSerializerOptions(JsonSerializerDefaults.Web)
    options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb) |> ignore
    options.Converters.Add(JsonFSharpConverter())
    options
