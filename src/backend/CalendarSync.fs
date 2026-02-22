module Michael.CalendarSync

open System
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open NodaTime
open Serilog
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
        let syncStart = now - Duration.FromDays(30)
        let syncEnd = now + Duration.FromDays(60)

        for (source, httpClient) in sourceClients do
            try
                let! result = syncSource httpClient source.Source hostTz syncStart syncEnd
                use conn = createConn ()

                let logSyncStatusError sourceId result =
                    match result with
                    | Ok() -> ()
                    | Error msg ->
                        Log.Warning("Failed to update sync status for source {SourceId}: {Error}", sourceId, msg)

                match result with
                | Ok events ->
                    match Database.replaceEventsForSource conn source.Source.Id events with
                    | Ok() ->
                        Database.updateSyncStatus conn source.Source.Id now "ok"
                        |> logSyncStatusError source.Source.Id

                        Database.recordSyncHistory conn source.Source.Id now "ok" None |> ignore
                    | Error msg ->
                        Database.updateSyncStatus conn source.Source.Id now $"error: {msg}"
                        |> logSyncStatusError source.Source.Id

                        Database.recordSyncHistory conn source.Source.Id now "error" (Some msg)
                        |> ignore
                | Error msg ->
                    Database.updateSyncStatus conn source.Source.Id now $"error: {msg}"
                    |> logSyncStatusError source.Source.Id

                    Database.recordSyncHistory conn source.Source.Id now "error" (Some msg)
                    |> ignore

                Database.pruneOldSyncHistory conn source.Source.Id 50
            with ex ->
                Log.Warning(ex, "Sync failed for source {SourceId}", source.Source.Id)

                try
                    use conn = createConn ()

                    Database.recordSyncHistory conn source.Source.Id now "error" (Some ex.Message)
                    |> ignore

                    Database.pruneOldSyncHistory conn source.Source.Id 50
                with _ ->
                    ()
    }

/// Full pipeline: generate ICS → PUT to CalDAV → store href in DB.
/// Fire-and-forget: the returned Task never faults. Failures are logged.
let writeBackBookingEvent
    (createConn: unit -> SqliteConnection)
    (client: HttpClient)
    (writeConfig: CalDavWriteBackConfig)
    (booking: Booking)
    (hostEmail: string)
    (videoLink: string option)
    : System.Threading.Tasks.Task<unit> =
    task {
        try
            let resourceUrl = $"{writeConfig.CalendarUrl.TrimEnd('/')}/{booking.Id}.ics"

            let icsContent = CalDav.buildCalDavEventIcs booking hostEmail videoLink
            let! result = CalDav.putEvent client resourceUrl icsContent

            match result with
            | Ok href ->
                use conn = createConn ()

                match Database.updateBookingCalDavEventHref conn booking.Id href with
                | Ok() ->
                    Log.Information("CalDAV write-back succeeded for booking {BookingId} at {Href}", booking.Id, href)
                | Error dbErr ->
                    Log.Warning(
                        "CalDAV PUT succeeded but DB update failed for booking {BookingId}: {Error}",
                        booking.Id,
                        dbErr
                    )
            | Error msg -> Log.Warning("CalDAV write-back failed for booking {BookingId}: {Error}", booking.Id, msg)
        with ex ->
            Log.Warning(ex, "Unhandled exception during CalDAV write-back for booking {BookingId}", booking.Id)
    }

/// Delete a CalDAV event for a cancelled booking.
/// Fire-and-forget: the returned Task never faults. Failures are logged.
let deleteWriteBackEvent (client: HttpClient) (booking: Booking) : System.Threading.Tasks.Task<unit> =
    task {
        match booking.CalDavEventHref with
        | None -> Log.Debug("No CalDAV event href for booking {BookingId}, skipping delete", booking.Id)
        | Some href ->
            try
                let! result = CalDav.deleteEvent client href

                match result with
                | Ok() -> Log.Information("CalDAV event deleted for booking {BookingId} at {Href}", booking.Id, href)
                | Error msg ->
                    Log.Warning("CalDAV event delete failed for booking {BookingId}: {Error}", booking.Id, msg)
            with ex ->
                Log.Warning(ex, "Unhandled exception deleting CalDAV event for booking {BookingId}", booking.Id)
    }

/// Get cached calendar events as NodaTime Intervals for blocking availability.
let getCachedBlockers (createConn: unit -> SqliteConnection) (rangeStart: Instant) (rangeEnd: Instant) : Interval list =
    use conn = createConn ()

    Database.getCachedEventsInRange conn rangeStart rangeEnd
    |> List.map (fun e -> Interval(e.StartInstant, e.EndInstant))

/// Start a background timer that syncs all sources every 10 minutes.
/// HttpClients are created via IHttpClientFactory per source.
/// Returns an IDisposable that disposes the timer and the created clients.
let startBackgroundSync
    (createConn: unit -> SqliteConnection)
    (httpClientFactory: IHttpClientFactory)
    (sources: CalDavSourceConfig list)
    (hostTz: DateTimeZone)
    (clock: IClock)
    : IDisposable =
    let syncLock = new SemaphoreSlim(1, 1)

    let sourceClients =
        sources
        |> List.map (fun s -> (s, createHttpClient httpClientFactory s.Username s.Password))

    let callback _ =
        if syncLock.Wait(0) then
            try
                try
                    (syncAllSources createConn sourceClients hostTz clock).GetAwaiter().GetResult()
                with ex ->
                    Log.Warning(ex, "Background sync error")
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
