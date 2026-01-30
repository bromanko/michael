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
    return f"""\
You are a scheduling assistant for a meeting booking tool. Your job is to
extract structured scheduling data from a participant's natural language input.

## Reference date/time

The current date and time is: {reference_dt}
The participant's timezone is: {reference_tz}

Use this to resolve relative expressions like "next Tuesday", "tomorrow",
"next week", etc. "Next <weekday>" means the NEXT occurrence of that weekday
that falls AFTER the reference date. If the reference date IS that weekday,
"next <weekday>" means the one in the following week.

## What to extract

From the participant's message, extract ALL of the following that are present:

1. **Availability windows** -- when they are free. Convert every mentioned
   time range into an explicit start/end pair as ISO-8601 datetime strings
   in the participant's timezone. If the participant says something vague
   like "afternoon", interpret it as 12:00 to 17:00. If they say "morning",
   use 09:00 to 12:00. If they say "all day" or give no time qualifier for
   a specific date, use 09:00 to 17:00 (business hours). If they mention a
   specific timezone (e.g., "2pm EST"), note it in the timezone field and
   still produce the start/end in that zone.

2. **Duration** -- the requested meeting length in minutes.

3. **Title** -- a short title or topic for the meeting.

4. **Description** -- any additional context or description beyond the title.

5. **Name** -- the participant's name.

6. **Email** -- the participant's email address.

7. **Phone** -- the participant's phone number.

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
- If input is ambiguous or very vague (e.g., "next week"), do your best to
  produce reasonable business-hours windows and note any assumptions in the
  description field.
- If structured/formatted text is pasted (e.g., "Available slots: ..."),
  parse it just like natural language -- extract the same fields.
- Prefer extracting a meeting title from topical phrases like "chat about X"
  or "discuss Y" -- use X or Y as the title.
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
        model="claude-sonnet-4-20250514",
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
    """Parse using OpenAI GPT-4o."""
    import openai

    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        return ParseResult(error="OPENAI_API_KEY not set")

    client = openai.OpenAI(api_key=api_key)
    system = build_system_prompt(reference_dt, reference_tz)

    response = client.chat.completions.create(
        model="gpt-4o",
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
    import google.generativeai as genai

    api_key = os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY")
    if not api_key:
        return ParseResult(error="GEMINI_API_KEY / GOOGLE_API_KEY not set")

    genai.configure(api_key=api_key)
    model = genai.GenerativeModel("gemini-2.0-flash")
    system = build_system_prompt(reference_dt, reference_tz)

    response = model.generate_content(
        contents=[
            {"role": "user", "parts": [{"text": f"{system}\n\n---\n\nUser input:\n{user_input}"}]},
        ],
        generation_config=genai.GenerationConfig(temperature=0),
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
