"""
Test cases for validating conversational input parsing.

Each test case is a dict with:
  - id: short identifier
  - description: what the case tests
  - input: the raw text a participant would type or paste
  - notes: hints about what a correct parse should contain
"""

TEST_CASES = [
    {
        "id": "simple_afternoon",
        "description": "Simple single-day availability, relative date",
        "input": "I'm free Tuesday afternoon",
        "notes": "Should produce one window on the next Tuesday, roughly 12:00-17:00. "
                 "Duration, title, contact info should all be missing.",
    },
    {
        "id": "complex_multi_day",
        "description": "Multiple days, time range, exception, duration, title, and email",
        "input": (
            "I can do Monday or Wednesday, anytime between 10am and 3pm, "
            "except Wednesday at noon. 30 minutes is fine. "
            "Let's talk about the Q3 roadmap. I'm at jane@acme.com"
        ),
        "notes": "Two windows (Mon 10-15, Wed 10-15 minus 12-13). "
                 "Duration 30. Title ~'Q3 roadmap'. Email jane@acme.com.",
    },
    {
        "id": "partial_no_contact",
        "description": "Partial input -- availability only, no duration or contact",
        "input": "next Thursday works for me",
        "notes": "One window on next Thursday (all day or business hours). "
                 "Missing: duration, title, name, email.",
    },
    {
        "id": "ambiguous_week",
        "description": "Vague availability spanning a whole week",
        "input": "I'm free next week",
        "notes": "Should produce windows covering next week's business days. "
                 "Missing: duration, title, name, email.",
    },
    {
        "id": "structured_paste",
        "description": "Structured text pasted from external screenshot prompt",
        "input": (
            "Available slots:\n"
            "- Mon Jan 20 9:00-12:00\n"
            "- Tue Jan 21 14:00-17:00"
        ),
        "notes": "Two precise windows. Dates are absolute (Jan 20, Jan 21). "
                 "Missing: duration, title, name, email.",
    },
    {
        "id": "full_info_single_message",
        "description": "Everything provided in one message",
        "input": (
            "I'm free next Friday afternoon for a 30-min chat about the "
            "project redesign. My email is jane@example.com and my name is "
            "Jane Smith. You can also reach me at 555-123-4567."
        ),
        "notes": "One window (next Friday afternoon). Duration 30. "
                 "Title ~'project redesign'. Name Jane Smith. "
                 "Email jane@example.com. Phone 555-123-4567. Nothing missing.",
    },
    {
        "id": "timezone_mention",
        "description": "Explicit timezone in availability",
        "input": "I can meet at 2pm EST on Monday or 10am PST on Wednesday",
        "notes": "Two windows with timezone info. Times should be resolved "
                 "relative to the stated zones. Missing: duration, title, name, email.",
    },
    {
        "id": "multi_week_range",
        "description": "Availability spanning multiple weeks",
        "input": (
            "I'm available Feb 3-7 and Feb 10-14, mornings only (9am to noon). "
            "45 minutes please."
        ),
        "notes": "Ten windows (5 days x 2 weeks, 9:00-12:00 each). "
                 "Duration 45. Missing: title, name, email.",
    },
    {
        "id": "structured_paste_detailed",
        "description": "Detailed structured paste from external AI tool",
        "input": (
            "Here are my available times for the week of February 3rd:\n\n"
            "Monday 2/3: 9:00 AM - 11:30 AM, 2:00 PM - 4:00 PM\n"
            "Tuesday 2/4: 10:00 AM - 12:00 PM\n"
            "Wednesday 2/5: All day (9:00 AM - 5:00 PM)\n"
            "Thursday 2/6: 1:00 PM - 3:00 PM\n"
            "Friday 2/7: 9:00 AM - 10:30 AM\n\n"
            "All times in Eastern Time."
        ),
        "notes": "Six windows across five days (Monday has two). "
                 "Timezone Eastern. Missing: duration, title, name, email.",
    },
    {
        "id": "correction_style",
        "description": "Input that refines a previous statement (simulating follow-up)",
        "input": "Actually, scratch Wednesday. I can only do Monday 10-2 and Thursday after 3pm.",
        "notes": "Two windows: Mon 10:00-14:00, Thu 15:00-17:00 (or end of day). "
                 "This simulates a correction. Missing: duration, title, name, email.",
    },
]
