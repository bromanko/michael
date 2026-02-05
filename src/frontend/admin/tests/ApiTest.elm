module ApiTest exposing (suite)

import Api
import Expect
import Json.Decode as Decode
import Test exposing (Test, describe, test)
import Types exposing (BookingStatus(..), CalDavProvider(..), DayOfWeek(..))


suite : Test
suite =
    describe "Api"
        [ bookingStatusDecoderTests
        , providerDecoderTests
        , dayOfWeekDecoderTests
        , dashboardStatsDecoderTests
        , calendarSourceDecoderTests
        , availabilitySlotDecoderTests
        ]


bookingStatusDecoderTests : Test
bookingStatusDecoderTests =
    describe "bookingStatusDecoder"
        [ test "decodes confirmed" <|
            \_ ->
                Decode.decodeString Api.bookingStatusDecoder "\"confirmed\""
                    |> Expect.equal (Ok Confirmed)
        , test "decodes cancelled" <|
            \_ ->
                Decode.decodeString Api.bookingStatusDecoder "\"cancelled\""
                    |> Expect.equal (Ok Cancelled)
        , test "fails on unknown status" <|
            \_ ->
                Decode.decodeString Api.bookingStatusDecoder "\"pending\""
                    |> Expect.err
        , test "fails on non-string" <|
            \_ ->
                Decode.decodeString Api.bookingStatusDecoder "123"
                    |> Expect.err
        ]


providerDecoderTests : Test
providerDecoderTests =
    describe "providerDecoder"
        [ test "decodes fastmail" <|
            \_ ->
                Decode.decodeString Api.providerDecoder "\"fastmail\""
                    |> Expect.equal (Ok Fastmail)
        , test "decodes icloud" <|
            \_ ->
                Decode.decodeString Api.providerDecoder "\"icloud\""
                    |> Expect.equal (Ok ICloud)
        , test "fails on unknown provider" <|
            \_ ->
                Decode.decodeString Api.providerDecoder "\"google\""
                    |> Expect.err
        ]


dayOfWeekDecoderTests : Test
dayOfWeekDecoderTests =
    describe "dayOfWeekDecoder"
        [ test "decodes 1 as Monday" <|
            \_ ->
                Decode.decodeString Api.dayOfWeekDecoder "1"
                    |> Expect.equal (Ok Monday)
        , test "decodes 7 as Sunday" <|
            \_ ->
                Decode.decodeString Api.dayOfWeekDecoder "7"
                    |> Expect.equal (Ok Sunday)
        , test "fails on 0" <|
            \_ ->
                Decode.decodeString Api.dayOfWeekDecoder "0"
                    |> Expect.err
        , test "fails on 8" <|
            \_ ->
                Decode.decodeString Api.dayOfWeekDecoder "8"
                    |> Expect.err
        ]


dashboardStatsDecoderTests : Test
dashboardStatsDecoderTests =
    describe "dashboardStatsDecoder"
        [ test "decodes full response" <|
            \_ ->
                let
                    json =
                        """{"upcomingCount": 5, "nextBookingTime": "2026-02-10T10:00:00Z", "nextBookingTitle": "Meeting"}"""
                in
                Decode.decodeString Api.dashboardStatsDecoder json
                    |> Result.map .upcomingCount
                    |> Expect.equal (Ok 5)
        , test "decodes response with null optionals" <|
            \_ ->
                let
                    json =
                        """{"upcomingCount": 0, "nextBookingTime": null, "nextBookingTitle": null}"""
                in
                Decode.decodeString Api.dashboardStatsDecoder json
                    |> Result.map .nextBookingTime
                    |> Expect.equal (Ok Nothing)
        , test "decodes response with missing optionals" <|
            \_ ->
                let
                    json =
                        """{"upcomingCount": 3}"""
                in
                Decode.decodeString Api.dashboardStatsDecoder json
                    |> Result.map .upcomingCount
                    |> Expect.equal (Ok 3)
        , test "fails on missing required field" <|
            \_ ->
                Decode.decodeString Api.dashboardStatsDecoder "{}"
                    |> Expect.err
        ]


calendarSourceDecoderTests : Test
calendarSourceDecoderTests =
    describe "calendarSourceDecoder"
        [ test "decodes full source" <|
            \_ ->
                let
                    json =
                        """{"id": "src-1", "provider": "fastmail", "baseUrl": "https://caldav.fastmail.com", "lastSyncedAt": "2026-02-01T12:00:00Z", "lastSyncResult": "ok"}"""
                in
                Decode.decodeString Api.calendarSourceDecoder json
                    |> Result.map .provider
                    |> Expect.equal (Ok Fastmail)
        , test "decodes source with null optionals" <|
            \_ ->
                let
                    json =
                        """{"id": "src-1", "provider": "icloud", "baseUrl": "https://caldav.icloud.com", "lastSyncedAt": null, "lastSyncResult": null}"""
                in
                Decode.decodeString Api.calendarSourceDecoder json
                    |> Result.map .lastSyncedAt
                    |> Expect.equal (Ok Nothing)
        ]


availabilitySlotDecoderTests : Test
availabilitySlotDecoderTests =
    describe "availabilitySlotDecoder"
        [ test "decodes valid slot" <|
            \_ ->
                let
                    json =
                        """{"id": "slot-1", "dayOfWeek": 1, "startTime": "09:00", "endTime": "17:00", "timezone": "America/New_York"}"""
                in
                Decode.decodeString Api.availabilitySlotDecoder json
                    |> Result.map .dayOfWeek
                    |> Expect.equal (Ok Monday)
        , test "fails on invalid dayOfWeek" <|
            \_ ->
                let
                    json =
                        """{"id": "slot-1", "dayOfWeek": 0, "startTime": "09:00", "endTime": "17:00", "timezone": "America/New_York"}"""
                in
                Decode.decodeString Api.availabilitySlotDecoder json
                    |> Expect.err
        ]
