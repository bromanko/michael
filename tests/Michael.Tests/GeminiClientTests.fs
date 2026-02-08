module Michael.Tests.GeminiClientTests

open Expecto
open NodaTime
open Michael.GeminiClient

[<Tests>]
let buildSystemPromptTests =
    testList
        "buildSystemPrompt"
        [ test "includes correct day of week" {
              // 2026-02-03 is a Tuesday
              let tz = DateTimeZoneProviders.Tzdb.["America/New_York"]
              let dt = LocalDateTime(2026, 2, 3, 10, 0).InZoneLeniently(tz)
              let prompt = buildSystemPrompt dt
              Expect.stringContains prompt "Tuesday" "should contain Tuesday"
          }

          test "includes timezone" {
              let tz = DateTimeZoneProviders.Tzdb.["America/Chicago"]
              let dt = LocalDateTime(2026, 2, 3, 10, 0).InZoneLeniently(tz)
              let prompt = buildSystemPrompt dt
              Expect.stringContains prompt "America/Chicago" "should contain timezone"
          }

          test "includes reference date" {
              let tz = DateTimeZoneProviders.Tzdb.["America/New_York"]
              let dt = LocalDateTime(2026, 2, 3, 10, 0).InZoneLeniently(tz)
              let prompt = buildSystemPrompt dt
              Expect.stringContains prompt "2026-02-03" "should contain ISO date"
          } ]

[<Tests>]
let parseResponseJsonTests =
    testList
        "parseResponseJson"
        [ test "parses valid JSON response" {
              let json =
                  """{
                "availability_windows": [
                    {
                        "start": "2026-02-03T09:00:00-05:00",
                        "end": "2026-02-03T17:00:00-05:00",
                        "timezone": "America/New_York"
                    }
                ],
                "duration_minutes": 30,
                "title": "Team standup",
                "description": "Interpreted as 9am-5pm ET",
                "name": "Alice",
                "email": "alice@example.com",
                "phone": null,
                "missing_fields": []
            }"""

              let result = parseResponseJson json
              Expect.isOk result "should parse successfully"
              let parsed = Result.defaultWith (fun _ -> failwith "unreachable") result
              Expect.hasLength parsed.AvailabilityWindows 1 "one window"
              Expect.equal parsed.DurationMinutes (Some 30) "duration"
              Expect.equal parsed.Title (Some "Team standup") "title"
              Expect.equal parsed.Name (Some "Alice") "name"
              Expect.equal parsed.Email (Some "alice@example.com") "email"
              Expect.isNone parsed.Phone "phone should be None"
              Expect.hasLength parsed.MissingFields 0 "no missing fields"
          }

          test "malformed JSON returns Error" {
              let result = parseResponseJson "not json"
              Expect.isError result "should return Error for invalid JSON"
          }

          test "strips markdown code fences" {
              let json =
                  """```json
{
    "availability_windows": [],
    "duration_minutes": 60,
    "title": "Coffee chat",
    "description": null,
    "name": null,
    "email": null,
    "phone": null,
    "missing_fields": ["availability", "name", "email"]
}
```"""

              let result = parseResponseJson json
              Expect.isOk result "should parse successfully"
              let parsed = Result.defaultWith (fun _ -> failwith "unreachable") result
              Expect.equal parsed.DurationMinutes (Some 60) "duration parsed through fences"
              Expect.equal parsed.Title (Some "Coffee chat") "title parsed through fences"
              Expect.hasLength parsed.MissingFields 3 "3 missing fields"
          } ]
