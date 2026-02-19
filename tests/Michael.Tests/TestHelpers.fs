module Michael.Tests.TestHelpers

open System
open System.IO
open System.Security.Cryptography
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open Michael.Database

/// Generate a fake cancellation token matching the production format
/// (64-character uppercase hex string from 32 random bytes).
let makeFakeCancellationToken () =
    Convert.ToHexString(RandomNumberGenerator.GetBytes(32))

/// Minimal IServiceProvider that returns null for every lookup.
/// Satisfies HttpContext.RequestServices in handler unit tests without a
/// full DI container. getJsonOptions falls back to default System.Text.Json
/// options when JsonSerializerOptions is not registered.
type NullServiceProvider() =
    interface IServiceProvider with
        member _.GetService(_: Type) = null

let private migrationsDir = Path.Combine(AppContext.BaseDirectory, "migrations")

/// Set up a shared-cache named in-memory SQLite database, initialized with
/// migrations and seed data, then call f with a createConn factory. The
/// anchor connection keeps the database alive for the duration of f.
///
/// Use this in handler-level tests where the handler calls createConn()
/// internally; a plain :memory: connection would be closed and discarded
/// between calls.
let withSharedMemoryDb (f: (unit -> SqliteConnection) -> unit) =
    let dbName = Guid.NewGuid().ToString("N")
    let connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared"
    use anchor = new SqliteConnection(connStr)
    anchor.Open()

    match initializeDatabase anchor migrationsDir SystemClock.Instance with
    | Error msg -> failwithf "initializeDatabase failed: %s" msg
    | Ok() -> ()

    let createConn () =
        let c = new SqliteConnection(connStr)
        c.Open()
        c

    f createConn

/// Create a DefaultHttpContext pre-configured for handler unit tests:
/// RequestServices points to a NullServiceProvider (so getJsonOptions
/// returns null and falls back to default JSON options) and the response
/// body is a writable MemoryStream.
let makeTestHttpContext () =
    let ctx = DefaultHttpContext()
    ctx.RequestServices <- NullServiceProvider()
    ctx.Response.Body <- new MemoryStream()
    ctx
