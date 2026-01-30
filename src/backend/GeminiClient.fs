module Michael.GeminiClient

open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks
open NodaTime
open NodaTime.Text
open Michael.Domain

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------

type GeminiConfig =
    { ApiKey: string
      Model: string }

let defaultModel = "gemini-3-flash-preview"

// ---------------------------------------------------------------------------
// System prompt (ported from spike/parser.py)
// ---------------------------------------------------------------------------

let private schemaJson =
    """{
  "availability_windows": [
    {
      "start": "ISO-8601 datetime string",
      "end": "ISO-8601 datetime string",
      "timezone": "IANA timezone or null"
    }
  ],
  "duration_minutes": "integer or null",
  "title": "string or null",
  "description": "string or null",
  "name": "string or null",
  "email": "string or null",
  "phone": "string or null",
  "missing_fields": ["list of strings from: availability, duration, title, name, email"]
}"""

let buildSystemPrompt (referenceDt: ZonedDateTime) : string =
    let refDate = referenceDt.Date
    let dayOfWeek = refDate.DayOfWeek
    let dayName = dayOfWeek.ToString()
    let dateStr = LocalDatePattern.Iso.Format(refDate)
    let timeStr = ZonedDateTimePattern.ExtendedFormatOnlyIso.Format(referenceDt)
    let tzId = referenceDt.Zone.Id

    $"""You are a scheduling assistant for a meeting booking tool. Your job is to
extract structured scheduling data from a participant's natural language input.

## Reference date/time

The current date and time is: {timeStr}
The current day of the week is: {dayName}
The participant's timezone is: {tzId}

Use this to resolve ALL relative date expressions. Here is how to resolve them:

- "today" = {dateStr} ({dayName})
- "tomorrow" = the next calendar day
- "next <weekday>" = the FIRST occurrence of that weekday AFTER today. For
  example, if today is {dayName} {refDate.ToString("MMMM d", null)}, then "next Monday" = the first Monday after today,
  "next Tuesday" = the first Tuesday after today, etc.
- "this <weekday>" = same as "next <weekday>" if that day hasn't occurred yet
  this week; otherwise the following week.
- "next week" = the full Monday-through-Friday of the week following the
  current one.

IMPORTANT: You MUST verify that the day-of-week you produce matches the
calendar date. Getting the day-of-week wrong is the most common error.

## Date resolution rules

- ALL dates in the output MUST be in the future relative to the reference
  date ({dateStr}).
- If the participant provides a date that appears to be in the past (e.g.,
  "Jan 20" when today is Jan 30), resolve it to the next future occurrence
  of that date pattern. Use your best judgment but NEVER return a past date.
- If the participant provides day-of-week names without dates (e.g., "Monday
  and Wednesday"), resolve to the NEXT occurrence of each after today.

## What to extract

From the participant's message, extract ALL of the following that are present:

1. **Availability windows** — when they are free. Convert every mentioned
   time range into an explicit start/end pair as ISO-8601 datetime strings.

   Time interpretation defaults:
   - "morning" = 09:00 to 12:00
   - "afternoon" = 12:00 to 17:00
   - "evening" = 17:00 to 20:00
   - "all day" or no time qualifier for a specific date = 09:00 to 17:00
   - "after <time>" (e.g., "after 3pm") = <time> to 17:00 (end of business)
   - "before <time>" (e.g., "before noon") = 09:00 to <time>

   Point-in-time expressions: If the participant says "I can meet at 2pm"
   or "2pm works", treat this as the START of an availability window, not a
   fixed 1-hour block. Use a 2-hour window starting at that time (e.g.,
   "at 2pm" = 14:00 to 16:00) unless context suggests otherwise.

   Timezone handling: If the participant mentions a specific timezone (e.g.,
   "2pm EST"), use that timezone for the offset and note it in the timezone
   field. Otherwise use the participant's default timezone ({tzId}).

2. **Duration** — the requested meeting length in minutes.

3. **Title** — a short title or topic for the meeting. Extract from topical
   phrases like "chat about X", "discuss Y", "re: Z" — use X/Y/Z as title.

4. **Description** — briefly describe how you interpreted the input, noting
   any assumptions you made (e.g., "Interpreted 'afternoon' as 12:00-17:00",
   "Resolved 'next Tuesday' to Feb 3"). This helps the participant confirm
   your interpretation. Leave null only if the input was completely
   unambiguous.

5. **Name** — the participant's name.

6. **Email** — the participant's email address.

7. **Phone** — the participant's phone number.

## Missing fields

After extraction, determine which REQUIRED fields are still missing. The
required fields are: availability, duration, title, name, email.
List each missing required field in the `missing_fields` array.
Phone is optional and should NOT appear in missing_fields.

## Output format

Respond with ONLY a JSON object matching this exact schema (no markdown
fencing, no commentary, no extra keys):

{schemaJson}

## Rules

- Return ONLY valid JSON. No markdown code fences. No explanation text.
- All datetime strings must be ISO-8601 with timezone offset
  (e.g., "2026-02-03T09:00:00-05:00").
- If the participant mentions an exception (e.g., "except Wednesday at noon"),
  split the window around the exception. For example, "10am to 3pm except
  noon" becomes two windows: 10:00-12:00 and 13:00-15:00.
- If structured/formatted text is pasted (e.g., "Available slots: ..."),
  parse it just like natural language — extract the same fields.
- NEVER return dates in the past relative to {dateStr}."""

// ---------------------------------------------------------------------------
// Response parsing
// ---------------------------------------------------------------------------

let private extractJson (text: string) : string =
    let trimmed = text.Trim()

    if trimmed.StartsWith("```") then
        let firstNewline = trimmed.IndexOf('\n')
        let withoutStart = trimmed.Substring(firstNewline + 1)

        if withoutStart.TrimEnd().EndsWith("```") then
            let idx = withoutStart.LastIndexOf("```")
            withoutStart.Substring(0, idx).Trim()
        else
            withoutStart.Trim()
    else
        trimmed

let parseResponseJson (rawModelOutput: string) : Result<ParseResult, string> =
    let json = extractJson rawModelOutput

    try
        use doc = JsonDocument.Parse(json)
        let root = doc.RootElement

        let getOptionalString (name: string) =
            match root.TryGetProperty(name) with
            | true, prop when prop.ValueKind <> JsonValueKind.Null -> Some(prop.GetString())
            | _ -> None

        let getOptionalInt (name: string) =
            match root.TryGetProperty(name) with
            | true, prop when prop.ValueKind <> JsonValueKind.Null -> Some(prop.GetInt32())
            | _ -> None

        let windows =
            match root.TryGetProperty("availability_windows") with
            | true, arr ->
                [ for item in arr.EnumerateArray() do
                      let start = item.GetProperty("start").GetString()
                      let end' = item.GetProperty("end").GetString()

                      let tz =
                          match item.TryGetProperty("timezone") with
                          | true, p when p.ValueKind <> JsonValueKind.Null -> Some(p.GetString())
                          | _ -> None

                      let pattern = OffsetDateTimePattern.ExtendedIso

                      { Start = pattern.Parse(start).Value
                        End = pattern.Parse(end').Value
                        Timezone = tz } ]
            | _ -> []

        let missingFields =
            match root.TryGetProperty("missing_fields") with
            | true, arr -> [ for item in arr.EnumerateArray() -> item.GetString() ]
            | _ -> []

        Ok
            { AvailabilityWindows = windows
              DurationMinutes = getOptionalInt "duration_minutes"
              Title = getOptionalString "title"
              Description = getOptionalString "description"
              Name = getOptionalString "name"
              Email = getOptionalString "email"
              Phone = getOptionalString "phone"
              MissingFields = missingFields }
    with ex ->
        Error $"Failed to parse Gemini response: {ex.Message}"

// ---------------------------------------------------------------------------
// Gemini API call
// ---------------------------------------------------------------------------

let parseInput
    (httpClient: HttpClient)
    (config: GeminiConfig)
    (userInput: string)
    (referenceDt: ZonedDateTime)
    : Task<Result<ParseResult, string>> =
    task {
        try
            let systemPrompt = buildSystemPrompt referenceDt

            let requestBody =
                $"""{{
  "system_instruction": {{
    "parts": [{{ "text": {JsonSerializer.Serialize(systemPrompt)} }}]
  }},
  "contents": [
    {{ "parts": [{{ "text": {JsonSerializer.Serialize(userInput)} }}] }}
  ],
  "generationConfig": {{
    "temperature": 0
  }}
}}"""

            let url =
                $"https://generativelanguage.googleapis.com/v1beta/models/{config.Model}:generateContent"

            use request = new HttpRequestMessage(HttpMethod.Post, url)
            request.Headers.Add("x-goog-api-key", config.ApiKey)
            request.Content <- new StringContent(requestBody, Encoding.UTF8, "application/json")

            let! response = httpClient.SendAsync(request)
            let! body = response.Content.ReadAsStringAsync()

            if not response.IsSuccessStatusCode then
                return Error $"Gemini API error ({int response.StatusCode}): {body}"
            else
                use doc = JsonDocument.Parse(body)

                let text =
                    doc.RootElement
                        .GetProperty("candidates").[0]
                        .GetProperty("content")
                        .GetProperty("parts").[0]
                        .GetProperty("text")
                        .GetString()

                return parseResponseJson text
        with ex ->
            return Error $"Request failed: {ex.Message}"
    }
