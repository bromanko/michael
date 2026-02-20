module Michael.Tests.TestHelpers

open System
open System.IO
open System.Threading
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open Michael.Database
open Michael.Email

/// Fixed cancellation token for use in fixtures that just need a
/// structurally valid token and don't care about its specific value.
/// Using a constant keeps those tests deterministic and their output
/// readable without relying on the RNG.
let fixedCancellationToken =
    "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890"

/// Atomic sequence counter backing makeFakeCancellationToken.
/// Starts at 0 and increments on every call, so each invocation returns a
/// distinct value without any CSPRNG allocation.
let mutable private tokenSeq = 0L

/// Generate a fake cancellation token matching the production format
/// (64-character uppercase hex string). Use this only when the test
/// genuinely requires a freshly-generated or unique value — for example,
/// the token format and uniqueness tests, or fixtures that insert multiple
/// bookings into the same DB (unique index on cancellation_token).
/// All other fixtures should use fixedCancellationToken instead.
///
/// Uses an atomic counter rather than a CSPRNG so there are no crypto
/// allocations in test code. The counter produces digit-only strings, which
/// are a valid subset of the uppercase hex alphabet the format tests expect.
let makeFakeCancellationToken () =
    let n = Interlocked.Increment(&tokenSeq)
    sprintf "%064d" n

/// Shared SMTP configuration used across email and handler tests.
/// A single definition here prevents the two modules from silently
/// diverging when the SmtpConfig shape changes.
let testSmtpConfig: SmtpConfig =
    { Host = "mail.example.com"
      Port = 587
      Username = None
      Password = None
      TlsMode = StartTls
      FromAddress = "cal@example.com"
      FromName = "Michael" }

/// Shared NotificationConfig used across email and handler tests.
let testNotificationConfig: NotificationConfig =
    { Smtp = testSmtpConfig
      HostEmail = "host@example.com"
      HostName = "Brian"
      PublicUrl = "https://cal.example.com" }

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
