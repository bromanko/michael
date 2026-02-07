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
open Michael.HttpHelpers

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

type HashedPassword = { Hash: byte array; Salt: byte array }

let hashPassword (password: string) (salt: byte array) : byte array =
    Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32)

let hashPasswordAtStartup (password: string) : HashedPassword =
    let salt = Array.zeroCreate<byte> 16
    RandomNumberGenerator.Fill(salt)

    { Hash = hashPassword password salt
      Salt = salt }

let private verifyPassword (submitted: string) (stored: HashedPassword) : bool =
    let submittedHash = hashPassword submitted stored.Salt
    CryptographicOperations.FixedTimeEquals(ReadOnlySpan(submittedHash), ReadOnlySpan(stored.Hash))

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
    let env =
        ctx.RequestServices.GetService(typeof<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>)
        :?> Microsoft.AspNetCore.Hosting.IWebHostEnvironment

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

let handleLogin (createConn: unit -> SqliteConnection) (adminPassword: HashedPassword) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            match! tryReadJsonBody<LoginRequest> jsonOptions ctx with
            | Error msg -> return! badRequest jsonOptions msg ctx
            | Ok body when String.IsNullOrWhiteSpace(body.Password) ->
                return! badRequest jsonOptions "Password is required." ctx
            | Ok body when not (verifyPassword body.Password adminPassword) ->
                log().Warning("Failed login attempt")
                ctx.Response.StatusCode <- 401
                return! Response.ofJsonOptions jsonOptions {| Error = "Invalid password." |} ctx
            | Ok _ ->
                let now = clock.GetCurrentInstant()
                let token = generateToken ()

                let session: AdminSession =
                    { Token = token
                      CreatedAt = now
                      ExpiresAt = now.Plus(Duration.FromDays(sessionDurationDays)) }

                use conn = createConn ()
                deleteExpiredAdminSessions conn now |> ignore

                match insertAdminSession conn session with
                | Error msg ->
                    log().Error("Failed to insert admin session: {Error}", msg)
                    ctx.Response.StatusCode <- 500
                    return! Response.ofJsonOptions jsonOptions {| Error = "Internal server error." |} ctx
                | Ok() ->
                    setSessionCookie ctx token session.ExpiresAt
                    log().Information("Admin login successful")
                    return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
        }

let handleLogout (createConn: unit -> SqliteConnection) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            match getSessionToken ctx with
            | Some token ->
                use conn = createConn ()
                deleteAdminSession conn token |> ignore
            | None -> ()

            clearSessionCookie ctx
            return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
        }

type private SessionError =
    | NotAuthenticated
    | SessionExpired
    | SessionInvalid

let private validateSession
    (createConn: unit -> SqliteConnection)
    (clock: IClock)
    (ctx: HttpContext)
    : Result<AdminSession, SessionError> =
    match getSessionToken ctx with
    | None -> Error NotAuthenticated
    | Some token ->
        use conn = createConn ()
        let now = clock.GetCurrentInstant()

        match getAdminSession conn token with
        | Some session when session.ExpiresAt > now -> Ok session
        | Some _ ->
            deleteAdminSession conn token |> ignore
            clearSessionCookie ctx
            Error SessionExpired
        | None ->
            clearSessionCookie ctx
            Error SessionInvalid

let private handleSessionError (jsonOptions: JsonSerializerOptions) (error: SessionError) (ctx: HttpContext) =
    task {
        ctx.Response.StatusCode <- 401

        let msg =
            match error with
            | NotAuthenticated -> "Not authenticated."
            | SessionExpired -> "Session expired."
            | SessionInvalid -> "Invalid session."

        return! Response.ofJsonOptions jsonOptions {| Error = msg |} ctx
    }

let handleSessionCheck (createConn: unit -> SqliteConnection) (clock: IClock) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            match validateSession createConn clock ctx with
            | Ok _ -> return! Response.ofJsonOptions jsonOptions {| Ok = true |} ctx
            | Error err -> return! handleSessionError jsonOptions err ctx
        }

let requireAdminSession (createConn: unit -> SqliteConnection) (clock: IClock) (handler: HttpHandler) : HttpHandler =
    fun ctx ->
        task {
            let jsonOptions = getJsonOptions ctx

            match validateSession createConn clock ctx with
            | Ok _ -> return! handler ctx
            | Error err -> return! handleSessionError jsonOptions err ctx
        }
