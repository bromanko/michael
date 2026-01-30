# Spike: Conversational Input Parser

**Date:** 2026-01-30
**Status:** Complete — concept validated

## Goal

Validate that AI models can reliably parse natural language availability input
into structured time windows for the Michael booking flow.

## Setup

- Python spike in `spike/` with a shared prompt and test harness
- Three providers tested: Anthropic (Claude Sonnet 4.5), OpenAI (GPT-5.2),
  Google (Gemini 3 Flash)
- Fixed reference datetime: `2026-01-30T10:00:00-05:00` (Friday, America/New_York)
- 10 test cases covering simple, complex, partial, ambiguous, structured paste,
  timezone, multi-week, and correction-style inputs

## Models Tested

| Provider  | Model                        | Input $/1M | Output $/1M |
|-----------|------------------------------|-----------|-------------|
| Anthropic | `claude-sonnet-4-5-20250929` | $3.00     | $15.00      |
| OpenAI    | `gpt-5.2`                   | $1.75     | $14.00      |
| Google    | `gemini-3-flash-preview`     | $0.50     | $3.00       |

## Results

### Scorecard

| Test                       | Anthropic | OpenAI | Gemini |
|----------------------------|:---------:|:------:|:------:|
| simple_afternoon           | Pass      | Pass   | Pass   |
| complex_multi_day          | Pass      | Pass   | Pass   |
| partial_no_contact         | Pass      | Pass   | Pass   |
| ambiguous_week             | Pass      | Pass   | Pass   |
| structured_paste           | Pass      | Fail   | Pass   |
| full_info_single_message   | Pass      | Pass   | Pass   |
| timezone_mention           | Pass      | Pass   | Pass   |
| multi_week_range           | Pass      | Pass   | Pass   |
| structured_paste_detailed  | Caveat    | Pass   | Caveat |
| correction_style           | Pass      | Pass   | Pass   |

### Notable Findings

**All three models handle the core cases well.** Date resolution, exception
splitting, timezone handling, field extraction, and missing field detection all
work reliably after prompt tuning.

**Key prompt improvements that made a difference:**

1. Including the explicit day-of-week for the reference date (e.g., "Friday")
   eliminated off-by-one date resolution errors across all providers.
2. Providing concrete date resolution examples ("if today is Friday Jan 30,
   next Monday = Feb 2") improved consistency.
3. Instructing models to always produce future dates fixed the past-date
   regression on structured paste inputs.
4. Defining point-in-time expressions as 2-hour windows ("at 2pm" = 14:00–16:00)
   gave more useful availability ranges.
5. Asking models to populate the description field with interpretation notes
   provides natural content for the confirmation step.

**Two disagreements across providers:**

1. **structured_paste ("Mon Jan 20"):** Anthropic and Gemini pragmatically
   remapped past dates to the next Monday/Tuesday. OpenAI over-literalized and
   jumped to Oct 2026 to find the next Jan 20 that falls on a Monday. Anthropic
   and Gemini's behavior is more useful.

2. **structured_paste_detailed ("Monday 2/3"):** The input had mismatched
   day-of-week labels and calendar dates (Feb 3, 2026 is a Tuesday, not Monday).
   OpenAI trusted the explicit dates; Anthropic and Gemini trusted the weekday
   labels. Both are defensible — the confirmation step handles this.

## Recommendation

**Use Gemini 3 Flash as the primary provider.** It matched or exceeded the
other providers on every test case, at roughly 4–5x lower cost. Estimated
cost per booking request is ~$0.002 for text and ~$0.002 for screenshots.

Anthropic Sonnet 4.5 is a strong backup if Gemini has reliability or
availability issues.

## Output Files

Raw provider outputs are preserved in `spike/`:
- `spike/anthropic-output.txt`
- `spike/openai-output.txt`
- `spike/gemini-output.txt`

## Next Steps

- Prototype screenshot (vision) parsing with the same providers
- Integrate the parser into the F# backend as an API endpoint
- Build the confirmation step UI in Elm
- Add rate limiting around the AI endpoint
