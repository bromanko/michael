module Michael.RateLimiting

open System
open System.Collections.Concurrent
open System.Threading.RateLimiting
open Microsoft.AspNetCore.Http
open Serilog

// ---------------------------------------------------------------------------
// Per-IP rate limiting for public API endpoints
//
// Uses .NET's built-in TokenBucketRateLimiter per client IP. Each policy
// defines its own bucket parameters. Expired limiters are pruned
// periodically to prevent memory growth.
// ---------------------------------------------------------------------------

let private log () =
    Log.ForContext("SourceContext", "Michael.RateLimiting")

type RateLimitPolicy =
    {
        /// Maximum tokens in the bucket (burst capacity).
        TokenLimit: int
        /// Tokens added per replenishment period.
        TokensPerPeriod: int
        /// How often tokens are replenished.
        ReplenishmentPeriod: TimeSpan
    }

/// Shared state for a named rate limit policy.
type private PolicyState =
    { Limiters: ConcurrentDictionary<string, TokenBucketRateLimiter>
      Policy: RateLimitPolicy }

let private policies = ConcurrentDictionary<string, PolicyState>()

/// Register a named rate limit policy. Call once at startup per policy.
let registerPolicy (name: string) (policy: RateLimitPolicy) =
    policies.[name] <-
        { Limiters = ConcurrentDictionary<string, TokenBucketRateLimiter>()
          Policy = policy }

/// Extract client IP from the request (respects forwarded headers if configured).
let private getClientIp (ctx: HttpContext) : string =
    ctx.Connection.RemoteIpAddress
    |> Option.ofObj
    |> Option.map (fun ip -> ip.ToString())
    |> Option.defaultValue "unknown"

/// Prune limiters that haven't been used recently (idle > 10 minutes).
/// Called opportunistically to avoid unbounded memory growth.
let private pruneThreshold = TimeSpan.FromMinutes(10.0)
let private lastPruneTime = ConcurrentDictionary<string, DateTime>()

let private maybePrune (policyName: string) (state: PolicyState) =
    let now = DateTime.UtcNow
    let lastPrune = lastPruneTime.GetOrAdd(policyName, now)

    if now - lastPrune > pruneThreshold then
        lastPruneTime.[policyName] <- now

        for kvp in state.Limiters do
            let limiter = kvp.Value

            if limiter.GetStatistics().CurrentAvailablePermits = state.Policy.TokenLimit then
                // Bucket is full → client has been idle, safe to remove
                match state.Limiters.TryRemove(kvp.Key) with
                | true, removed -> removed.Dispose()
                | _ -> ()

/// Try to acquire a permit for the given policy and client IP.
/// Returns true if the request is allowed, false if rate-limited.
let tryAcquire (policyName: string) (ctx: HttpContext) : bool =
    match policies.TryGetValue(policyName) with
    | false, _ ->
        log().Warning("Rate limit policy '{PolicyName}' not registered", policyName)
        true // Fail open if policy not found
    | true, state ->
        let clientIp = getClientIp ctx

        // Prune BEFORE getting a limiter so we don't retrieve one that's
        // about to be disposed. A narrow race still exists when another
        // thread prunes concurrently, so we also guard AttemptAcquire below.
        maybePrune policyName state

        let limiter =
            state.Limiters.GetOrAdd(
                clientIp,
                fun _ ->
                    new TokenBucketRateLimiter(
                        TokenBucketRateLimiterOptions(
                            TokenLimit = state.Policy.TokenLimit,
                            TokensPerPeriod = state.Policy.TokensPerPeriod,
                            ReplenishmentPeriod = state.Policy.ReplenishmentPeriod,
                            QueueLimit = 0,
                            AutoReplenishment = true
                        )
                    )
            )

        try
            use lease = limiter.AttemptAcquire(1)

            if lease.IsAcquired then
                true
            else
                log().Information("Rate limited {ClientIp} on policy '{PolicyName}'", clientIp, policyName)
                false
        with :? ObjectDisposedException ->
            // The limiter was pruned and disposed by a concurrent thread
            // between GetOrAdd and AttemptAcquire. Remove the stale entry
            // so the next request gets a fresh limiter, and fail open.
            state.Limiters.TryRemove(clientIp) |> ignore
            true
