module Michael.CalendarSync

open System
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open NodaTime
open Michael.Domain
open Michael.CalDav

/// Sync all configured calendar sources to the local cache.
let syncAllSources
    (createConn: unit -> SqliteConnection)
    (sourceClients: (CalDavSourceConfig * HttpClient) list)
    (hostTz: DateTimeZone)
    (clock: IClock)
    =
    task {
        let now = clock.GetCurrentInstant()
        let syncEnd = now + Duration.FromDays(60)

        for (source, httpClient) in sourceClients do
            try
                let! result = syncSource httpClient source.Source hostTz now syncEnd
                use conn = createConn ()

                match result with
                | Ok events ->
                    Database.replaceEventsForSource conn source.Source.Id events
                    Database.updateSyncStatus conn source.Source.Id now "ok"
                | Error msg -> Database.updateSyncStatus conn source.Source.Id now $"error: {msg}"
            with ex ->
                eprintfn "Sync failed for source %s: %s" (source.Source.Id.ToString()) ex.Message
    }

/// Get cached calendar events as NodaTime Intervals for blocking availability.
let getCachedBlockers (createConn: unit -> SqliteConnection) (rangeStart: Instant) (rangeEnd: Instant) : Interval list =
    use conn = createConn ()

    Database.getCachedEventsInRange conn rangeStart rangeEnd
    |> List.map (fun e -> Interval(e.StartInstant, e.EndInstant))

/// Start a background timer that syncs all sources every 10 minutes.
/// Returns an IDisposable that disposes both the timer and the HttpClients.
let startBackgroundSync
    (createConn: unit -> SqliteConnection)
    (sources: CalDavSourceConfig list)
    (hostTz: DateTimeZone)
    (clock: IClock)
    : IDisposable =
    let syncLock = new SemaphoreSlim(1, 1)

    let sourceClients =
        sources |> List.map (fun s -> (s, createHttpClient s.Username s.Password))

    let callback _ =
        if syncLock.Wait(0) then
            try
                try
                    (syncAllSources createConn sourceClients hostTz clock).GetAwaiter().GetResult()
                with ex ->
                    eprintfn "Background sync error: %s" ex.Message
            finally
                syncLock.Release() |> ignore

    let interval = TimeSpan.FromMinutes(10.0)
    let timer = new Timer(TimerCallback(callback), null, TimeSpan.Zero, interval)

    { new IDisposable with
        member _.Dispose() =
            timer.Dispose()

            for (_, client) in sourceClients do
                client.Dispose()

            syncLock.Dispose() }
