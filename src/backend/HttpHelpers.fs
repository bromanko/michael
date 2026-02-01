module Michael.HttpHelpers

open System
open System.Text.Json
open Falco
open Microsoft.AspNetCore.Http
open Serilog

let private log () =
    Log.ForContext("SourceContext", "Michael.HttpHelpers")

let badRequest (jsonOptions: JsonSerializerOptions) (message: string) (ctx: HttpContext) =
    task {
        ctx.Response.StatusCode <- 400
        return! Response.ofJsonOptions jsonOptions {| Error = message |} ctx
    }

let tryReadJsonBody<'T when 'T: not struct> (jsonOptions: JsonSerializerOptions) (ctx: HttpContext) =
    task {
        try
            let! body = ctx.Request.ReadFromJsonAsync<'T>(jsonOptions)

            if Object.ReferenceEquals(body, null) then
                return Error "Request body is required."
            else
                return Ok body
        with :? JsonException as ex ->
            log().Warning("Malformed JSON in request body: {Error}", ex.Message)
            return Error "Request body contains malformed JSON."
    }
