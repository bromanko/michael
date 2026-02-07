module Michael.Tests.AdminTests

open System
open Expecto
open NodaTime
open NodaTime.Text
open Microsoft.Data.Sqlite
open Michael.Domain
open Michael.Database
open Michael.AdminAuth

let private migrationsDir =
    System.IO.Path.Combine(System.AppContext.BaseDirectory, "migrations")

let private withMemoryDb f =
    use conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()

    match initializeDatabase conn migrationsDir NodaTime.SystemClock.Instance with
    | Error msg -> failtestf "initializeDatabase failed: %s" msg
    | Ok() -> ()

    f conn

let private makeBooking (startOdt: string) (endOdt: string) (status: BookingStatus) =
    let pattern = OffsetDateTimePattern.ExtendedIso

    { Id = Guid.NewGuid()
      ParticipantName = "Alice"
      ParticipantEmail = "alice@example.com"
      ParticipantPhone = None
      Title = "Test meeting"
      Description = None
      StartTime = pattern.Parse(startOdt).Value
      EndTime = pattern.Parse(endOdt).Value
      DurationMinutes = 30
      Timezone = "America/New_York"
      Status = status
      CreatedAt = Instant.FromUtc(2026, 1, 1, 0, 0) }

[<Tests>]
let passwordTests =
    testList "Password hashing" [
        test "hashPasswordAtStartup produces correct length hash and salt" {
            let hashed = hashPasswordAtStartup "mysecret"
            Expect.equal hashed.Hash.Length 32 "hash is 32 bytes"
            Expect.equal hashed.Salt.Length 16 "salt is 16 bytes"
        }

        test "same password with same salt produces same hash" {
            let salt = Array.zeroCreate<byte> 16
            let hash1 = hashPassword "mysecret" salt
            let hash2 = hashPassword "mysecret" salt
            Expect.equal hash1 hash2 "deterministic hashing"
        }

        test "different passwords produce different hashes" {
            let salt = Array.zeroCreate<byte> 16
            let hash1 = hashPassword "password1" salt
            let hash2 = hashPassword "password2" salt
            Expect.notEqual hash1 hash2 "different passwords differ"
        }

        test "different salts produce different hashes" {
            let salt1 = Array.zeroCreate<byte> 16
            let salt2 = Array.init 16 (fun _ -> 1uy)
            let hash1 = hashPassword "mysecret" salt1
            let hash2 = hashPassword "mysecret" salt2
            Expect.notEqual hash1 hash2 "different salts differ"
        }

        test "hashPasswordAtStartup produces unique salts" {
            let hashed1 = hashPasswordAtStartup "mysecret"
            let hashed2 = hashPasswordAtStartup "mysecret"
            Expect.notEqual hashed1.Salt hashed2.Salt "salts are unique"
        }
    ]

[<Tests>]
let adminSessionDbTests =
    testList "Admin session database" [
        test "insertAdminSession and getAdminSession round-trip" {
            withMemoryDb (fun conn ->
                let now = Instant.FromUtc(2026, 2, 3, 12, 0)

                let session: AdminSession =
                    { Token = "test-token-123"
                      CreatedAt = now
                      ExpiresAt = now.Plus(Duration.FromDays(7)) }

                let result = insertAdminSession conn session
                Expect.isOk result "insert should succeed"

                let retrieved = getAdminSession conn "test-token-123"
                Expect.isSome retrieved "session should be found"
                Expect.equal retrieved.Value.Token "test-token-123" "token matches"
                Expect.isTrue (retrieved.Value.CreatedAt.Equals(now)) "createdAt matches"
            )
        }

        test "getAdminSession returns None for unknown token" {
            withMemoryDb (fun conn ->
                let retrieved = getAdminSession conn "nonexistent-token"
                Expect.isNone retrieved "should not find unknown token"
            )
        }

        test "deleteAdminSession removes session" {
            withMemoryDb (fun conn ->
                let now = Instant.FromUtc(2026, 2, 3, 12, 0)

                let session: AdminSession =
                    { Token = "delete-me"
                      CreatedAt = now
                      ExpiresAt = now.Plus(Duration.FromDays(7)) }

                insertAdminSession conn session |> ignore

                let result = deleteAdminSession conn "delete-me"
                Expect.isOk result "delete should succeed"

                let retrieved = getAdminSession conn "delete-me"
                Expect.isNone retrieved "session should be gone"
            )
        }

        test "deleteExpiredAdminSessions removes only expired sessions" {
            withMemoryDb (fun conn ->
                let now = Instant.FromUtc(2026, 2, 3, 12, 0)

                let expired: AdminSession =
                    { Token = "expired-token"
                      CreatedAt = now.Minus(Duration.FromDays(14))
                      ExpiresAt = now.Minus(Duration.FromDays(7)) }

                let valid: AdminSession =
                    { Token = "valid-token"
                      CreatedAt = now
                      ExpiresAt = now.Plus(Duration.FromDays(7)) }

                insertAdminSession conn expired |> ignore
                insertAdminSession conn valid |> ignore

                let result = deleteExpiredAdminSessions conn now
                Expect.isOk result "delete should succeed"

                Expect.isNone (getAdminSession conn "expired-token") "expired session removed"
                Expect.isSome (getAdminSession conn "valid-token") "valid session preserved"
            )
        }
    ]

[<Tests>]
let adminBookingDbTests =
    testList "Admin booking queries" [
        test "getBookingById returns booking" {
            withMemoryDb (fun conn ->
                let booking = makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed
                insertBooking conn booking |> ignore

                let retrieved = getBookingById conn booking.Id
                Expect.isSome retrieved "booking found"
                Expect.equal retrieved.Value.Title "Test meeting" "title matches"
            )
        }

        test "getBookingById returns None for unknown id" {
            withMemoryDb (fun conn ->
                let retrieved = getBookingById conn (Guid.NewGuid())
                Expect.isNone retrieved "should not find unknown id"
            )
        }

        test "cancelBooking changes status to cancelled" {
            withMemoryDb (fun conn ->
                let booking = makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed
                insertBooking conn booking |> ignore

                let result = cancelBooking conn booking.Id
                Expect.isOk result "cancel should succeed"

                let retrieved = getBookingById conn booking.Id
                Expect.isSome retrieved "booking still exists"
                Expect.equal retrieved.Value.Status Cancelled "status is cancelled"
            )
        }

        test "cancelBooking returns error for unknown id" {
            withMemoryDb (fun conn ->
                let result = cancelBooking conn (Guid.NewGuid())
                Expect.isError result "should fail for unknown id"
            )
        }

        test "cancelBooking returns error for already cancelled booking" {
            withMemoryDb (fun conn ->
                let booking = makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed
                insertBooking conn booking |> ignore

                cancelBooking conn booking.Id |> ignore
                let result = cancelBooking conn booking.Id
                Expect.isError result "should fail for already cancelled"
            )
        }

        test "listBookings returns paginated results" {
            withMemoryDb (fun conn ->
                for i in 1..5 do
                    let booking =
                        { makeBooking $"2026-02-{i + 1:D2}T10:00:00-05:00" $"2026-02-{i + 1:D2}T10:30:00-05:00" Confirmed with
                            Id = Guid.NewGuid()
                            Title = $"Meeting {i}" }

                    insertBooking conn booking |> ignore

                let (bookings, totalCount) = listBookings conn 1 3 None
                Expect.equal totalCount 5 "total count is 5"
                Expect.hasLength bookings 3 "page size is 3"

                let (page2, _) = listBookings conn 2 3 None
                Expect.hasLength page2 2 "second page has remainder"
            )
        }

        test "listBookings filters by status" {
            withMemoryDb (fun conn ->
                let confirmed = makeBooking "2026-02-03T10:00:00-05:00" "2026-02-03T10:30:00-05:00" Confirmed
                let cancelled = { makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Cancelled with Id = Guid.NewGuid() }
                insertBooking conn confirmed |> ignore
                insertBooking conn cancelled |> ignore

                let (confirmedList, confirmedCount) = listBookings conn 1 20 (Some "confirmed")
                Expect.equal confirmedCount 1 "one confirmed"
                Expect.hasLength confirmedList 1 "one confirmed booking"

                let (cancelledList, cancelledCount) = listBookings conn 1 20 (Some "cancelled")
                Expect.equal cancelledCount 1 "one cancelled"
                Expect.hasLength cancelledList 1 "one cancelled booking"
            )
        }

        test "getUpcomingBookingsCount counts only future confirmed bookings" {
            withMemoryDb (fun conn ->
                let now = Instant.FromUtc(2026, 2, 3, 15, 0)

                // Future confirmed
                let future = makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Confirmed
                // Past confirmed
                let past = { makeBooking "2026-02-02T10:00:00-05:00" "2026-02-02T10:30:00-05:00" Confirmed with Id = Guid.NewGuid() }
                // Future cancelled
                let cancelled = { makeBooking "2026-02-05T10:00:00-05:00" "2026-02-05T10:30:00-05:00" Cancelled with Id = Guid.NewGuid() }

                insertBooking conn future |> ignore
                insertBooking conn past |> ignore
                insertBooking conn cancelled |> ignore

                let count = getUpcomingBookingsCount conn now
                Expect.equal count 1 "only one future confirmed booking"
            )
        }

        test "getNextBooking returns earliest future confirmed booking" {
            withMemoryDb (fun conn ->
                let now = Instant.FromUtc(2026, 2, 3, 15, 0)

                let later = { makeBooking "2026-02-05T10:00:00-05:00" "2026-02-05T10:30:00-05:00" Confirmed with Id = Guid.NewGuid(); Title = "Later" }
                let sooner = { makeBooking "2026-02-04T10:00:00-05:00" "2026-02-04T10:30:00-05:00" Confirmed with Id = Guid.NewGuid(); Title = "Sooner" }

                insertBooking conn later |> ignore
                insertBooking conn sooner |> ignore

                let next = getNextBooking conn now
                Expect.isSome next "should find next booking"
                Expect.equal next.Value.Title "Sooner" "returns earliest"
            )
        }

        test "getNextBooking returns None when no future bookings" {
            withMemoryDb (fun conn ->
                let now = Instant.FromUtc(2026, 2, 10, 15, 0)
                let past = makeBooking "2026-02-02T10:00:00-05:00" "2026-02-02T10:30:00-05:00" Confirmed
                insertBooking conn past |> ignore

                let next = getNextBooking conn now
                Expect.isNone next "no future bookings"
            )
        }
    ]
