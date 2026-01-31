module Michael.AdminAuth

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Data.Sqlite
open NodaTime
open Serilog
open Michael.Domain
open Michael.Database

let private log () =
    Log.ForContext("SourceContext", "Michael.AdminAuth")

let private sessionCookieName = "michael_session"
let private sessionDurationDays = 7

// ---------------------------------------------------------------------------
// Request DTOs
// ---------------------------------------------------------------------------

[<CLIMutable>]
type LoginRequest = { Password: string }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private constantTimeCompare (a: string) (b: string) : bool =
    let aBytes = Encoding.UTF8.GetBytes(a)
    let bBytes = Encoding.UTF8.GetBytes(b)
    CryptographicOperations.FixedTimeEquals(ReadOnlySpan(aBytes), ReadOnlySpan(bBytes))

let private generateToken () : string =
    let bytes = Array.zeroCreate<byte> 32
    RandomNumberGenerator.Fill(bytes)
    Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')

let private setSessionCookie (ctx: HttpContext) (token: string) (expires: Instant) =
    let cookieOptions = CookieOptions()
    cookieOptions.HttpOnly <- true
    cookieOptions.SameSite <- SameSiteMode.Strict
    cookieOptions.Path <- "/api/admin"
    cookieOptions.Expires <- DateTimeOffset(expires.ToDateTimeUtc())

    // Only set Secure in non-development environments
    let env = ctx.RequestServices.GetService(typeof<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>) :?> Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    cookieOptions.Secure <- not (env.EnvironmentName = "Development")

    ctx.Response.Cookies.Append(sessionCookieName, token, cookieOptions)

let private clearSessionCookie (ctx: HttpContext) =
    let cookieOptions = CookieOptions()
    cookieOptions.HttpOnly <- true
    cookieOptions.SameSite <- SameSiteMode.Strict
    cookieOptions.Path <- "/api/admin"
    ctx.Response.Cookies.Delete(sessionCookieName, cookieOptions)

let private getSessionToken (ctx: HttpContext) : string option =
    match ctx.Request.Cookies.TryGetValue(sessionCookieName) with
    | true, value when not (String.IsNullOrEmpty(value)) -> Some value
    | _ -> None

// ---------------------------------------------------------------------------
// Handlers
// ---------------------------------------------------------------------------

let handleLogin (createConn: unit -> SqliteConnection) (adminPassword: string) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            try
                let! body = ctx.Request.ReadFromJsonAsync<LoginRequest>(jsonOptions)

                if Object.ReferenceEquals(body, null) || String.IsNullOrWhiteSpace(body.Password) then
                    ctx.Response.StatusCode <- 400
                    return! Response.ofJsonOptions jsonOptions {| Error = "Password is required." |} ctx
                elif not (constantTimeCompare body.Password adminPassword) then
                    log().Warning("Failed login attempt")
                    ctx.Response.StatusCode <- 401
                    return! Response.ofJsonOptions jsonOptions {| Error = "Invalid password." |} ctx
                else
                    let now = SystemClock.Instance.GetCurrentInstant()
                    let token = generateToken ()

                    let session: AdminSession =
                        { Token = token
                          CreatedAt = now
                          ExpiresAt = now.Plus(Duration.FromDays(sessionDurationDays)) }

                    use conn = createConn ()
                    deleteExpiredAdminSessions conn now
                    insertAdminSession conn session
                    setSessionCookie ctx token session.ExpiresAt

                    log().Information("Admin login successful")
                    return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
            with :? JsonException ->
                ctx.Response.StatusCode <- 400
                return! Response.ofJsonOptions jsonOptions {| Error = "Invalid request body." |} ctx
        }

let handleLogout (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            match getSessionToken ctx with
            | Some token ->
                use conn = createConn ()
                deleteAdminSession conn token
            | None -> ()

            clearSessionCookie ctx
            return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
        }

let handleSessionCheck (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            match getSessionToken ctx with
            | Some token ->
                use conn = createConn ()
                let now = SystemClock.Instance.GetCurrentInstant()

                match getAdminSession conn token with
                | Some session when session.ExpiresAt > now ->
                    return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
                | Some _ ->
                    // Session expired
                    deleteAdminSession conn token
                    clearSessionCookie ctx
                    ctx.Response.StatusCode <- 401
                    return! Response.ofJsonOptions jsonOptions {| Error = "Session expired." |} ctx
                | None ->
                    clearSessionCookie ctx
                    ctx.Response.StatusCode <- 401
                    return! Response.ofJsonOptions jsonOptions {| Error = "Invalid session." |} ctx
            | None ->
                ctx.Response.StatusCode <- 401
                return! Response.ofJsonOptions jsonOptions {| Error = "Not authenticated." |} ctx
        }

let requireAdminSession (createConn: unit -> SqliteConnection) (handler: HttpHandler) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions =
                ctx.RequestServices.GetService(typeof<JsonSerializerOptions>) :?> JsonSerializerOptions

            match getSessionToken ctx with
            | Some token ->
                use conn = createConn ()
                let now = SystemClock.Instance.GetCurrentInstant()

                match getAdminSession conn token with
                | Some session when session.ExpiresAt > now ->
                    return! handler ctx
                | Some _ ->
                    deleteAdminSession conn token
                    clearSessionCookie ctx
                    ctx.Response.StatusCode <- 401
                    return! Response.ofJsonOptions jsonOptions {| Error = "Session expired." |} ctx
                | None ->
                    clearSessionCookie ctx
                    ctx.Response.StatusCode <- 401
                    return! Response.ofJsonOptions jsonOptions {| Error = "Invalid session." |} ctx
            | None ->
                ctx.Response.StatusCode <- 401
                return! Response.ofJsonOptions jsonOptions {| Error = "Not authenticated." |} ctx
        }
