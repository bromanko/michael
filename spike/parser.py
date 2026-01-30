"""
Conversational input parser for the Michael scheduling tool.

Calls an AI model to extract structured scheduling data from natural language
input. Supports Anthropic (Claude), OpenAI (GPT-4o), and Google (Gemini).
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field, asdict
from datetime import datetime
from enum import Enum
from typing import Optional

from pydantic import BaseModel, Field


# ---------------------------------------------------------------------------
# Structured output schema
# ---------------------------------------------------------------------------

class AvailabilityWindow(BaseModel):
    """A single window of availability."""
    start: str = Field(
        description="ISO-8601 datetime string for the start of the window"
    )
    end: str = Field(
        description="ISO-8601 datetime string for the end of the window"
    )
    timezone: Optional[str] = Field(
        default=None,
        description="IANA timezone (e.g. 'America/New_York') if explicitly stated by the user",
    )


class ParseResult(BaseModel):
    """Structured result of parsing a participant's natural language input."""

    availability_windows: list[AvailabilityWindow] = Field(
        default_factory=list,
        description="List of time windows when the participant is available",
    )
    duration_minutes: Optional[int] = Field(
        default=None,
        description="Requested meeting duration in minutes, if mentioned",
    )
    title: Optional[str] = Field(
        default=None,
        description="Meeting title or topic, if mentioned",
    )
    description: Optional[str] = Field(
        default=None,
        description="Additional meeting description or context, if mentioned",
    )
    name: Optional[str] = Field(
        default=None,
        description="Participant's name, if provided",
    )
    email: Optional[str] = Field(
        default=None,
        description="Participant's email address, if provided",
    )
    phone: Optional[str] = Field(
        default=None,
        description="Participant's phone number, if provided",
    )
    missing_fields: list[str] = Field(
        default_factory=list,
        description=(
            "Fields that are required but were not provided. "
            "Possible values: 'availability', 'duration', 'title', 'name', 'email'"
        ),
    )
    raw_model_output: Optional[str] = Field(
        default=None,
        description="The raw text returned by the model (for debugging)",
    )
    error: Optional[str] = Field(
        default=None,
        description="Error message if parsing failed",
    )


# ---------------------------------------------------------------------------
# System prompt
# ---------------------------------------------------------------------------

SCHEMA_JSON = """{
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


def build_system_prompt(reference_dt: str, reference_tz: str) -> str:
    """Build the system prompt with a fixed reference datetime and timezone."""
    # Compute the day-of-week for the reference date so models don't have to
    # infer it (a common source of error).
    from datetime import datetime as _dt
    ref_parsed = _dt.fromisoformat(reference_dt)
    day_of_week = ref_parsed.strftime("%A")

    return f"""\
You are a scheduling assistant for a meeting booking tool. Your job is to
extract structured scheduling data from a participant's natural language input.

## Reference date/time

The current date and time is: {reference_dt}
The current day of the week is: {day_of_week}
The participant's timezone is: {reference_tz}

Use this to resolve ALL relative date expressions. Here is how to resolve them:

- "today" = {ref_parsed.strftime("%Y-%m-%d")} ({day_of_week})
- "tomorrow" = the next calendar day
- "next <weekday>" = the FIRST occurrence of that weekday AFTER today. For
  example, if today is Friday January 30, then "next Monday" = February 2,
  "next Tuesday" = February 3, "next Friday" = February 6.
- "this <weekday>" = same as "next <weekday>" if that day hasn't occurred yet
  this week; otherwise the following week.
- "next week" = the full Monday-through-Friday of the week following the
  current one. If today is Friday Jan 30, "next week" = Feb 2-6.

IMPORTANT: You MUST verify that the day-of-week you produce matches the
calendar date. For example, if you output 2026-02-03, verify that Feb 3 2026
is indeed a Tuesday (it is — Jan 30 is Friday, +1=Sat, +2=Sun, +3=Mon Feb 2,
+4=Tue Feb 3). Getting the day-of-week wrong is the most common error.

## Date resolution rules

- ALL dates in the output MUST be in the future relative to the reference
  date ({ref_parsed.strftime("%Y-%m-%d")}).
- If the participant provides a date that appears to be in the past (e.g.,
  "Jan 20" when today is Jan 30), resolve it to the next future occurrence
  of that date pattern. For "Mon Jan 20", find the next Monday that is a
  Jan 20 or, if the intent is clearly "next Monday", use the next Monday.
  Use your best judgment but NEVER return a past date.
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
   field. Otherwise use the participant's default timezone ({reference_tz}).

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

{SCHEMA_JSON}

## Rules

- Return ONLY valid JSON. No markdown code fences. No explanation text.
- All datetime strings must be ISO-8601 with timezone offset
  (e.g., "2026-02-03T09:00:00-05:00").
- If the participant mentions an exception (e.g., "except Wednesday at noon"),
  split the window around the exception. For example, "10am to 3pm except
  noon" becomes two windows: 10:00-12:00 and 13:00-15:00.
- If structured/formatted text is pasted (e.g., "Available slots: ..."),
  parse it just like natural language — extract the same fields.
- NEVER return dates in the past relative to {ref_parsed.strftime("%Y-%m-%d")}.
"""


# ---------------------------------------------------------------------------
# Provider enum
# ---------------------------------------------------------------------------

class Provider(str, Enum):
    ANTHROPIC = "anthropic"
    OPENAI = "openai"
    GEMINI = "gemini"


# ---------------------------------------------------------------------------
# Parsing helpers
# ---------------------------------------------------------------------------

def _extract_json(text: str) -> dict:
    """Extract a JSON object from model output, handling common wrapping."""
    text = text.strip()
    # Strip markdown code fences if present
    if text.startswith("```"):
        # Remove opening fence (with optional language tag)
        first_newline = text.index("\n")
        text = text[first_newline + 1 :]
        # Remove closing fence
        if text.endswith("```"):
            text = text[: -3]
        text = text.strip()
    return json.loads(text)


def _build_result(raw: str) -> ParseResult:
    """Parse raw model output into a ParseResult."""
    try:
        data = _extract_json(raw)
    except (json.JSONDecodeError, ValueError) as exc:
        return ParseResult(
            raw_model_output=raw,
            error=f"Failed to parse JSON: {exc}",
            missing_fields=["availability", "duration", "title", "name", "email"],
        )

    try:
        result = ParseResult(**data)
        result.raw_model_output = raw
        return result
    except Exception as exc:
        return ParseResult(
            raw_model_output=raw,
            error=f"Failed to validate schema: {exc}",
            missing_fields=["availability", "duration", "title", "name", "email"],
        )


# ---------------------------------------------------------------------------
# Provider implementations
# ---------------------------------------------------------------------------

def parse_with_anthropic(
    user_input: str,
    reference_dt: str,
    reference_tz: str,
) -> ParseResult:
    """Parse using Anthropic Claude."""
    import anthropic

    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        return ParseResult(error="ANTHROPIC_API_KEY not set")

    client = anthropic.Anthropic(api_key=api_key)
    system = build_system_prompt(reference_dt, reference_tz)

    message = client.messages.create(
        model="claude-sonnet-4-5-20250929",
        max_tokens=2048,
        system=system,
        messages=[{"role": "user", "content": user_input}],
    )

    raw = message.content[0].text
    return _build_result(raw)


def parse_with_openai(
    user_input: str,
    reference_dt: str,
    reference_tz: str,
) -> ParseResult:
    """Parse using OpenAI GPT-5.2."""
    import openai

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        return ParseResult(error="OPENAI_API_KEY not set")

    client = openai.OpenAI(api_key=api_key)
    system = build_system_prompt(reference_dt, reference_tz)

    response = client.chat.completions.create(
        model="gpt-5.2",
        temperature=0,
        messages=[
            {"role": "system", "content": system},
            {"role": "user", "content": user_input},
        ],
    )

    raw = response.choices[0].message.content
    return _build_result(raw)


def parse_with_gemini(
    user_input: str,
    reference_dt: str,
    reference_tz: str,
) -> ParseResult:
    """Parse using Google Gemini."""
    from google import genai

    api_key = os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY")
    if not api_key:
        return ParseResult(error="GEMINI_API_KEY / GOOGLE_API_KEY not set")

    client = genai.Client(api_key=api_key)
    system = build_system_prompt(reference_dt, reference_tz)

    response = client.models.generate_content(
        model="gemini-3-flash-preview",
        config={"system_instruction": system, "temperature": 0},
        contents=user_input,
    )

    raw = response.text
    return _build_result(raw)


# ---------------------------------------------------------------------------
# Unified entry point
# ---------------------------------------------------------------------------

PROVIDER_FUNCTIONS = {
    Provider.ANTHROPIC: parse_with_anthropic,
    Provider.OPENAI: parse_with_openai,
    Provider.GEMINI: parse_with_gemini,
}


def parse(
    provider: Provider,
    user_input: str,
    reference_dt: str = "2026-01-30T10:00:00-05:00",
    reference_tz: str = "America/New_York",
) -> ParseResult:
    """Parse user input using the specified provider."""
    fn = PROVIDER_FUNCTIONS[provider]
    return fn(user_input, reference_dt, reference_tz)
