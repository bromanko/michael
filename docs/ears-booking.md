# EARS Requirements Specification: Michael Booking System

**Source:** `docs/design.md`, `docs/plan-booking-page.md`, `docs/spike-conversational-parser.md`, and existing codebase (`src/frontend/booking/`, `src/backend/`)
**Date:** 2026-02-15
**System:** the booking system

---

## 1. Booking Flow Navigation

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| NAV-001 | When the booking page loads, the booking system shall display the meeting title step as the initial step. | Test: Load the booking page; verify the title input is displayed. |
| NAV-002 | When the participant completes the title step, the booking system shall advance to the duration selection step. | Test: Enter a title and submit; verify the duration step is displayed. |
| NAV-003 | When the participant completes the duration step, the booking system shall advance to the availability input step. | Test: Select a duration and submit; verify the availability input is displayed. |
| NAV-004 | When the participant completes the availability input step, the booking system shall send the availability text to the parse endpoint and advance to the availability confirmation step upon a successful response. | Test: Enter availability text and submit; verify the parse API is called and the confirmation step is displayed. |
| NAV-005 | When the participant confirms the parsed availability, the booking system shall request overlapping slots from the slots endpoint and advance to the slot selection step upon a successful response. | Test: Confirm availability; verify the slots API is called and slot selection is displayed. |
| NAV-006 | When the participant selects a time slot, the booking system shall advance to the contact information step. | Test: Click a slot; verify the contact info form is displayed. |
| NAV-007 | When the participant completes the contact information step, the booking system shall advance to the booking confirmation step. | Test: Enter valid contact info and submit; verify the confirmation summary is displayed. |
| NAV-008 | When the participant confirms the booking, the booking system shall send the booking request to the book endpoint and advance to the completion step upon a successful response. | Test: Click confirm; verify the book API is called and the completion step is displayed. |
| NAV-009 | When the participant clicks the back button, the booking system shall return to the immediately preceding step. | Test: On each step with a back button, click it; verify the previous step is displayed. |
| NAV-010 | When the participant navigates back from the slot selection step, the booking system shall clear the loaded slots and the selected slot. | Test: Navigate back from slot selection, then forward again; verify slots are re-fetched. |
| NAV-011 | When the participant navigates back from the availability confirmation step, the booking system shall clear the parsed availability windows. | Test: Navigate back from confirmation; verify parsed windows are cleared. |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| NAV-020 | The booking system shall display a progress bar indicating the participant's position within the booking flow. | Test: At each step, verify the progress bar width corresponds to the current step number out of the total. |
| NAV-021 | The booking system shall not provide a back button on the title step. | Inspection: Review the title step view; verify no back button is rendered. |
| NAV-022 | The booking system shall not provide a back button on the completion step. | Inspection: Review the completion step view; verify no back button is rendered. |

---

## 2. Meeting Title Input

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| TTL-001 | When the title step is displayed, the booking system shall focus the title text input. | Test: Navigate to the title step; verify the title input has focus. |
| TTL-002 | When the participant presses Enter with a non-empty title, the booking system shall advance to the duration step. | Test: Type a title and press Enter; verify the duration step appears. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| TTL-010 | If the title field is empty or contains only whitespace, then the booking system shall prevent form submission by disabling the submit button and ignoring the Enter key. | Test: Clear the title and press Enter; verify the step does not advance and no duration options appear. |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| TTL-020 | The booking system shall disable the title submit button when the title field is empty or contains only whitespace. | Test: Load the title step; verify the button is disabled. Type a non-whitespace character; verify it becomes enabled. |

---

## 3. Duration Selection

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| DUR-001 | When the duration step is displayed, the booking system shall present preset duration options of 15, 30, 45, and 60 minutes, plus a custom duration option. | Test: Navigate to the duration step; verify all five options are displayed. |
| DUR-002 | When the participant selects a preset duration, the booking system shall visually highlight the selected option. | Test: Click a preset duration; verify it has the selected visual styling. |
| DUR-003 | When the participant selects the custom duration option, the booking system shall display a numeric input field and focus it. | Test: Click "Custom duration"; verify the input field appears and has focus. |
| DUR-004 | When the participant submits a valid preset duration, the booking system shall advance to the availability step. | Test: Select a preset and submit; verify the availability step appears. |
| DUR-005 | When the participant submits a valid custom duration between 5 and 480 minutes, the booking system shall advance to the availability step. | Test: Enter "25" as custom duration and submit; verify the availability step appears. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| DUR-010 | If the participant submits the duration step without selecting any option, then the booking system shall display the error message "Please select a duration." and remain on the duration step. | Test: Submit without selecting; verify the error message and step persistence. |
| DUR-011 | If the participant submits a custom duration less than 5 or greater than 480, then the booking system shall display the error message "Duration must be between 5 and 480 minutes." and remain on the duration step. | Test: Enter "3" and submit; verify the error. Enter "500" and submit; verify the error. |
| DUR-012 | The booking system shall use a numeric input type for the custom duration field, preventing non-numeric text entry at the browser level. If the participant submits the custom duration field while it is empty, the booking system shall display an appropriate validation error and remain on the duration step. | Test: Select custom duration, leave the field empty, and submit; verify a validation error is displayed. |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| DUR-020 | The booking system shall disable the duration submit button when no duration option is selected. | Test: Load the duration step with no prior selection; verify the button is disabled. |

---

## 4. Availability Input

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| AVL-001 | When the availability step is displayed, the booking system shall focus the availability text area. | Test: Navigate to the availability step; verify the textarea has focus. |
| AVL-002 | When the participant submits non-empty availability text, the booking system shall send a POST request to `/api/parse` containing the text and the participant's timezone. | Test: Enter text and submit; verify the HTTP request is sent with the correct payload. |
| AVL-003 | When the participant presses Enter (without Shift) in the availability text area, the booking system shall submit the availability input. | Test: Type text and press Enter; verify the form submits. |
| AVL-004 | When the participant presses Shift+Enter in the availability text area, the booking system shall insert a newline without submitting. | Test: Press Shift+Enter; verify a newline is inserted and the form is not submitted. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| AVL-010 | If the participant submits the availability step with empty or whitespace-only text, then the booking system shall display the error message "Please describe your availability." and remain on the availability step. | Test: Submit with empty textarea; verify the error message. |

### State-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| AVL-020 | While the availability parse request is in progress, the booking system shall display the button label "Finding slots..." and disable the submit button. | Test: Submit availability; verify the loading state is shown before the response arrives. |

---

## 5. Natural Language Parsing (Backend)

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| PRS-001 | When a POST request is received at `/api/parse` with a non-empty message and valid timezone, the booking system shall concatenate any `previousMessages` with the current message, send the combined text to the AI model, and return the parsed availability windows, extracted fields, and a human-readable system message. The `previousMessages` field is optional; when absent or empty, only the current message is sent. | Test: POST valid parse request; verify the response contains `parseResult` and `systemMessage`. |
| PRS-002 | When parsing a natural language availability message, the booking system shall produce structured availability windows with ISO-8601 start and end timestamps including a full UTC offset in `±HH:MM` format. Each window shall include a `timezone` field echoing the IANA timezone used for resolution. | Test: Parse "tomorrow 2pm to 5pm"; verify the response windows have valid ISO-8601 offset datetimes with full `±HH:MM` offsets and include a `timezone` field. |
| PRS-003 | When the AI model extracts additional fields (duration, title, description, name, email, phone) from the message, the booking system shall include those fields in the parse result. The `description` field contains AI-generated context about parsing decisions (e.g., date resolution notes). | Test: Parse "30 min chat with Jane, jane@example.com, free Friday afternoon"; verify duration, title, name, and email are populated. |
| PRS-004 | When the parse result contains unprovided fields, the booking system shall list those fields in the `missingFields` array. | Test: Parse a message containing only availability; verify missingFields includes the expected field names. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| PRS-010 | If the parse request has an empty or whitespace-only message, then the booking system shall return HTTP 400 with the error "Message is required." | Test: POST with empty or whitespace-only message; verify HTTP 400 and error text. |
| PRS-011 | If the parse request has an empty, whitespace-only, or missing timezone, then the booking system shall return HTTP 400 with the error "Timezone is required." | Test: POST with empty or whitespace-only timezone; verify HTTP 400 and error text. |
| PRS-012 | If the parse request specifies an unrecognized IANA timezone, then the booking system shall return HTTP 400 with an error identifying the invalid timezone. | Test: POST with timezone "Fake/Zone"; verify HTTP 400 and descriptive error. |
| PRS-013 | If the AI model returns an error or unparseable response, then the booking system shall return HTTP 500 with the error "An internal error occurred." and log the error details. | Test: Simulate AI failure; verify HTTP 500 response. Inspection: Verify error is logged. |
| PRS-014 | If the parse request message exceeds 2 000 characters, then the booking system shall return HTTP 400 with an error indicating the message is too long. | Test: POST with 2 001-character message; verify HTTP 400 and error text. |
| PRS-015 | If the parse request `previousMessages` array exceeds 20 entries, then the booking system shall return HTTP 400 with an error indicating too many previous messages. | Test: POST with 21 previous messages; verify HTTP 400 and error text. |
| PRS-016 | If any individual entry in `previousMessages` exceeds 2 000 characters, then the booking system shall return HTTP 400 with an error indicating the previous message is too long. | Test: POST with one 2 001-character previous message; verify HTTP 400 and error text. |
| PRS-017 | If the combined length of all previous messages and the current message exceeds 20 000 characters, then the booking system shall return HTTP 400 with an error indicating the combined input is too long. | Test: POST with 10 previous messages of 1 900 characters each plus a 2 000-character message; verify HTTP 400 and error text. |
| PRS-018 | When the parse endpoint returns a response, the booking system shall sanitize all AI-generated text fields (title, description, name, email, phone) by stripping control characters, trimming whitespace, and truncating to field-specific maximum lengths before including them in the response. | Inspection: Verify `sanitizeParseResult` is applied to all LLM output fields. Test: verify response fields do not contain control characters. |

---

## 6. Availability Confirmation

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| ACF-001 | When the availability confirmation step is displayed, the booking system shall show each parsed availability window with a human-readable date and time range. | Test: After parsing, verify each window is rendered with formatted date and time. |
| ACF-002 | When the participant confirms the parsed availability, the booking system shall send a POST request to `/api/slots` with the confirmed windows, selected duration, and timezone. | Test: Click confirm; verify the slots request payload matches the parsed windows and duration. |
| ACF-003 | When the participant navigates back from the confirmation step, the booking system shall return to the availability text input with the original text preserved. | Test: Navigate back from confirmation; verify the text area contains the previously entered text. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| ACF-010 | If the parse response returns zero availability windows, then the booking system shall display the error message "Could not parse availability windows. Please try describing your availability differently." and remain on the availability step. | Test: Provide unparseable input; verify the error message on the availability step. |

---

## 7. Slot Computation (Backend)

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SLT-001 | When a POST request is received at `/api/slots` with valid availability windows, duration, and timezone, the booking system shall compute the intersection of participant windows with host availability and return the resulting time slots. | Test: POST valid slots request; verify the response contains overlapping slots. |
| SLT-002 | When computing slots, the booking system shall subtract existing confirmed bookings from the available intervals. | Test: Create a booking in a host-available window, then request slots overlapping that window; verify the booked period is excluded. |
| SLT-003 | When computing slots, the booking system shall subtract calendar events from connected calendar sources from the available intervals. | Test: Create cached calendar events, then request slots; verify those periods are excluded. |
| SLT-004 | When computing slots, the booking system shall divide each available interval into contiguous, non-overlapping blocks of the requested duration in minutes, starting from the beginning of the interval. Gaps created by existing bookings or calendar events may shift subsequent slot start times. | Test: Request 30-minute slots for a clean 2-hour window; verify four 30-minute slots are returned. |
| SLT-005 | When computing slots, the booking system shall return slot times expressed in the participant's requested timezone with full `±HH:MM` UTC offsets. | Test: Request slots with timezone "America/Chicago"; verify returned slot timestamps carry the correct UTC offset in `±HH:MM` format for that timezone. |
| SLT-006 | When computing slots, the booking system shall exclude slots that start before the configured minimum scheduling notice period from the current time. | Test: With 6-hour minimum notice, request slots; verify no slot starts within 6 hours of now. |
| SLT-007 | When computing slots, the booking system shall exclude slots that start beyond the configured booking window from the current time. | Test: With a 30-day booking window, request slots 60 days out; verify no slots are returned. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SLT-010 | If the slots request contains zero availability windows, then the booking system shall return HTTP 400 with the error "At least one availability window is required." | Test: POST with empty windows array; verify HTTP 400 and error text. |
| SLT-011 | If the slots request specifies a duration outside the range 5–480 minutes, then the booking system shall return HTTP 400 with the error "DurationMinutes must be between 5 and 480." | Test: POST with duration 0; verify HTTP 400. POST with duration 500; verify HTTP 400. |
| SLT-012 | If the slots request contains an availability window with an unparseable ISO-8601 datetime, then the booking system shall return HTTP 400 identifying the invalid field and value. | Test: POST with malformed start time; verify HTTP 400 with descriptive error. |
| SLT-013 | If no overlapping slots exist between the participant's windows and host availability, then the booking system shall return an empty slots array. | Test: Request slots for a weekend when the host is only available weekdays; verify an empty slots array. |

---

## 8. Slot Selection (Frontend)

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SSE-001 | When the slot selection step is displayed with available slots, the booking system shall render each slot as a selectable button showing the date and time range. | Test: View the slot selection step; verify each slot shows a formatted date and time range. |
| SSE-002 | When the slot selection step is displayed, the booking system shall focus the first slot button. | Test: View the slot selection step; verify the first slot has keyboard focus. |
| SSE-003 | When the participant clicks a slot, the booking system shall record the selected slot and advance to the contact information step. | Test: Click a slot; verify the contact info step appears with the slot recorded. |

### State-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SSE-010 | While the slots list contains more items than fit in the visible area, the booking system shall display a scrollable container with a maximum height constraint. | Demonstration: Load many slots; verify the container scrolls and does not extend the page indefinitely. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SSE-020 | If the slots response contains zero slots, then the booking system shall display the message "No overlapping slots found for those times." and provide a link to try different times. | Test: Arrange no overlap; verify the empty-state message and the "Try different times" link that navigates back. |

---

## 9. Contact Information

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| CTI-001 | When the contact information step is displayed, the booking system shall focus the name input field. | Test: Navigate to contact info step; verify the name input has focus. |
| CTI-002 | When the participant submits with a valid name and a valid email address, the booking system shall advance to the confirmation step. | Test: Enter valid name and email, submit; verify the confirmation step appears. |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| CTI-010 | The booking system shall require a participant name (non-empty, non-whitespace). | Test: Submit with empty name; verify error "Please enter your name." |
| CTI-011 | The booking system shall require a participant email address (non-empty, non-whitespace). | Test: Submit with empty email; verify error "Please enter your email address." |
| CTI-012 | The booking system shall validate the email address contains exactly one `@` separating a non-empty local part and a domain containing a `.` that does not end with `.`. | Test: Submit with "bad", "@no.com", "a@b", "a@b."; verify each produces "Please enter a valid email address." Submit with "a@b.com"; verify acceptance. |
| CTI-013 | The booking system shall treat the phone number field as optional. | Test: Submit with name and email but no phone; verify the form advances. |

---

## 10. Booking Confirmation (Frontend)

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| BCF-001 | When the confirmation step is displayed, the booking system shall show a summary including the meeting title, duration, selected time slot (date and time range), participant name, and participant email. | Test: Verify each field is rendered on the confirmation step. |
| BCF-002 | When the confirmation step is displayed and the participant provided a phone number, the booking system shall include the phone number in the summary. | Test: Enter a phone number earlier; verify it appears on the confirmation step. |
| BCF-003 | When the confirmation step is displayed and the participant did not provide a phone number, the booking system shall omit the phone field from the summary. | Test: Leave phone blank; verify no phone field in the summary. |
| BCF-004 | When the participant confirms the booking, the booking system shall send a POST request to `/api/book` with all booking details. | Test: Click confirm; verify the HTTP request payload contains name, email, title, slot, duration, and timezone. |

### State-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| BCF-010 | While the booking request is in progress, the booking system shall display the button label "Booking..." and disable the confirm button. | Test: Click confirm; verify loading state before response arrives. |

---

## 11. Booking Creation (Backend)

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| BKC-001 | When a POST request is received at `/api/book` with valid booking details and the selected slot is still available, the booking system shall create a booking record with status "confirmed" and return the booking ID with `confirmed: true`. | Test: POST valid booking; verify HTTP 200, a booking ID is returned, and the database contains a confirmed booking. |
| BKC-002 | When creating a booking, the booking system shall re-verify slot availability by recomputing available slots against current host availability, existing bookings, and calendar events at the time of the request. | Test: Book two overlapping slots concurrently; verify only one succeeds and the other receives a conflict response. |
| BKC-003 | When creating a booking, the booking system shall use an IMMEDIATE transaction to prevent concurrent conflicting inserts. | Inspection: Review the `insertBookingIfSlotAvailable` function; verify `BEGIN IMMEDIATE` is used. |
| BKC-004 | When a booking is successfully created, the booking system shall log the booking ID and participant email. | Inspection: Review handler code; verify the log statement includes booking ID and email. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| BKC-010 | If the book request has an empty or whitespace-only name, then the booking system shall return HTTP 400 with the error "Name is required." | Test: POST with empty name; verify HTTP 400 and error text. |
| BKC-011 | If the book request has an invalid email address, then the booking system shall return HTTP 400 with the error "A valid email address is required." | Test: POST with "bad-email"; verify HTTP 400 and error text. |
| BKC-012 | If the book request has an empty or whitespace-only title, then the booking system shall return HTTP 400 with the error "Title is required." | Test: POST with empty title; verify HTTP 400 and error text. |
| BKC-013 | If the book request specifies a duration outside the range 5–480 minutes, then the booking system shall return HTTP 400 with the error "DurationMinutes must be between 5 and 480." | Test: POST with duration 2; verify HTTP 400. |
| BKC-014 | If the book request specifies a slot where the end is not after the start, then the booking system shall return HTTP 400 with the error "Slot.End must be after Slot.Start." | Test: POST with end before start; verify HTTP 400. |
| BKC-015 | If the book request specifies a slot whose duration does not match the requested DurationMinutes, then the booking system shall return HTTP 400 with the error "Slot duration must match DurationMinutes." | Test: POST with a 60-minute slot but DurationMinutes of 30; verify HTTP 400. |
| BKC-016 | If the selected slot is no longer available at the time of booking, then the booking system shall return HTTP 409 with `code: "slot_unavailable"`. | Test: Book a slot, then attempt to book the same slot again; verify HTTP 409. |
| BKC-017 | If a database error occurs during booking insertion, then the booking system shall return HTTP 500 with the error "An internal error occurred." and log the error details. | Test: Simulate a database write failure; verify HTTP 500 and error logging. |

---

## 12. Booking Completion (Frontend)

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| CMP-001 | When the booking is successfully created, the booking system shall display a success indicator and the booking ID. | Test: Complete a booking; verify the checkmark, "You're booked." heading, and booking ID are displayed. |
| CMP-002 | When the booking is successfully created, the booking system shall display the message "A confirmation email is on its way." | Test: Complete a booking; verify the email confirmation message is displayed. |

---

## 13. Time Zone Handling

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| TZ-001 | When the booking page loads, the booking system shall detect the participant's timezone from the browser and use it as the default. | Test: Load the page in a browser set to "America/Chicago"; verify the model timezone is "America/Chicago". |
| TZ-002 | When the participant changes the timezone on the availability confirmation step, the booking system shall re-parse the availability text using the new timezone. | Test: Change timezone on the confirmation step; verify a new parse request is sent with the updated timezone. |
| TZ-003 | When the participant changes the timezone on the slot selection step, the booking system shall re-fetch slots using the new timezone. | Test: Change timezone on the slot selection step; verify a new slots request is sent with the updated timezone. |
| TZ-004 | When the participant clicks the timezone selector, the booking system shall display a dropdown of common IANA timezones with human-readable names. | Test: Click the timezone button; verify the dropdown appears with timezone options. |
| TZ-005 | When the participant selects a timezone from the dropdown, the booking system shall close the dropdown and update the active timezone. | Test: Select a timezone; verify the dropdown closes and the displayed timezone updates. |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| TZ-010 | The booking system shall display the timezone selector on both the availability confirmation step and the slot selection step. | Inspection: Review both step views; verify the timezone selector component is rendered. |
| TZ-011 | The booking system shall display all times in the participant's selected timezone. | Test: Select "Asia/Tokyo" and verify all displayed times reflect the Tokyo offset. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| TZ-020 | If the browser-detected timezone does not contain a "/" character or exceeds 50 characters, then the booking system shall default to "UTC". | Test: Pass an invalid timezone in flags; verify the model timezone is "UTC". |

---

## 14. CSRF Protection

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| CSR-001 | When a GET request is received at `/api/csrf-token`, the booking system shall return a JSON response containing `ok: true` and a `token` field, and set the token as a cookie named `michael_csrf`. | Test: GET `/api/csrf-token`; verify the response body contains `ok` and `token` fields and the Set-Cookie header sets `michael_csrf`. |
| CSR-002 | When a POST request to a protected endpoint includes an `X-CSRF-Token` header matching the `michael_csrf` cookie value, the booking system shall process the request. | Test: Send a POST with matching header and cookie; verify the request succeeds. |

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| CSR-010 | If a POST request to a protected endpoint is missing the `X-CSRF-Token` header, then the booking system shall return HTTP 403 with the error "Forbidden." | Test: POST without the header; verify HTTP 403. |
| CSR-011 | If a POST request to a protected endpoint has an `X-CSRF-Token` header that does not match the cookie, then the booking system shall return HTTP 403 with the error "Forbidden." | Test: POST with a mismatched header; verify HTTP 403. |
| CSR-012 | If a protected POST request receives HTTP 403 and no CSRF refresh has been attempted in the current operation, then the booking system shall fetch a new CSRF token and retry the request once. | Test: Simulate an expired CSRF token; verify the frontend fetches a new token and retries. |
| CSR-013 | If the CSRF refresh retry also fails, then the booking system shall display the error "Booking session expired. Please refresh and try again." and stop retrying. | Test: Simulate persistent CSRF failure; verify the error message is displayed and no further retries occur. |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| CSR-020 | The booking system shall compare CSRF tokens using constant-time comparison to prevent timing side-channel attacks. | Inspection: Review `fixedTimeEquals`; verify it uses `CryptographicOperations.FixedTimeEquals`. |
| CSR-021 | The booking system shall set the CSRF cookie with `SameSite=Strict`, `HttpOnly=false`, and `IsEssential=true`. | Inspection: Review cookie options in `handleCsrfToken`; verify the attributes. |
| CSR-022 | The booking system shall validate that a CSRF token received from the server is exactly 64 hexadecimal characters before storing it. | Inspection: Review `validCsrfToken` in Model.elm; verify the length and character checks. |

---

## 15. Slot Conflict Recovery (Frontend)

### Unwanted Behavior Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SCR-001 | If the booking request returns HTTP 409 (slot unavailable), then the booking system shall return the participant to the slot selection step with the error "That slot is no longer available. Please choose another time." and re-fetch updated slots. | Test: Simulate a 409 response; verify the step, error message, and re-fetched slot list. |
| SCR-002 | If the availability parse request fails with a network error, then the booking system shall display "Network error. Please try again." | Test: Simulate a network failure on parse; verify the error message. |
| SCR-003 | If the availability parse request times out, then the booking system shall display "Request timed out. Please try again." | Test: Simulate a timeout on parse; verify the error message. |
| SCR-004 | If the availability parse request returns an HTTP error status other than 403, then the booking system shall display "Server error (<status>)". | Test: Simulate HTTP 500 on parse; verify the error message includes the status code. |
| SCR-005 | If the slots request fails, then the booking system shall display "Failed to load available slots. Please try again." | Test: Simulate a slots request failure; verify the error message. |
| SCR-006 | If the booking request fails with an error other than 403 or 409, then the booking system shall display "Failed to confirm booking. Please try again." | Test: Simulate a non-409, non-403 booking failure; verify the error message. |
| SCR-007 | If the CSRF token is absent when a protected request is attempted, then the booking system shall display "Failed to initialize booking session. Please refresh and try again." | Test: Clear the CSRF token, then attempt a protected request; verify the error message. |

---

## 16. Host Availability Configuration

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| HAV-001 | The booking system shall store host weekly availability as day-of-week and local start/end time pairs. | Inspection: Review the `host_availability` table schema and `HostAvailabilitySlot` type. |
| HAV-002 | The booking system shall seed default host availability of Monday through Friday, 09:00–17:00, if no host availability records exist at startup. | Test: Initialize a fresh database; verify five rows exist with days 1–5 and times 09:00–17:00. |

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| HAV-010 | When expanding host availability into concrete intervals, the booking system shall use the host's configured timezone to resolve DST transitions. | Test: Request slots spanning a DST transition date; verify slot boundaries shift by the correct offset. |

---

## 17. Scheduling Settings

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| SCH-001 | The booking system shall enforce a configurable minimum scheduling notice period, defaulting to 6 hours. | Test: With default settings, verify no slots are returned that start within 6 hours of the current time. |
| SCH-002 | The booking system shall enforce a configurable maximum booking window, defaulting to 30 days. | Test: With default settings, verify no slots are returned for dates more than 30 days in the future. |
| SCH-003 | The booking system shall use a configurable default meeting duration, defaulting to 30 minutes. | Inspection: Verify `defaultSettings` has `DefaultDurationMinutes = 30`. |

---

## 18. Input Validation (Backend — General)

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| VAL-001 | The booking system shall validate all ISO-8601 datetime strings using the NodaTime extended ISO pattern and reject values that do not parse. | Test: Send malformed datetime strings to each endpoint; verify HTTP 400 with descriptive field-level error messages. |
| VAL-002 | The booking system shall validate timezone identifiers against the IANA TZDB and reject unrecognized values. | Test: Send requests with invented timezone IDs; verify HTTP 400 with an error identifying the invalid timezone. |
| VAL-003 | The booking system shall validate email addresses on the backend using the same rules as the frontend (one `@`, non-empty local, domain with `.`, not ending in `.`). | Test: POST booking requests with various invalid emails; verify HTTP 400. |
| VAL-004 | The booking system shall accept meeting durations only in the range 5–480 minutes inclusive on all endpoints that accept a duration parameter. | Test: POST with durations 4, 5, 480, and 481 to both `/api/slots` and `/api/book`; verify 4 and 481 are rejected and 5 and 480 are accepted. |

---

## 19. Accessibility & Keyboard Navigation

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| A11-001 | When each step is displayed, the booking system shall programmatically move focus to the primary interactive element of that step. | Test: Navigate through each step; verify the appropriate element receives focus (title input, duration button, availability textarea, confirm button, first slot, name input, confirm button). |

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| A11-010 | The booking system shall support form submission via the Enter key on all form steps. | Test: On each form step, press Enter; verify the form submits. |
| A11-011 | The booking system shall use semantic HTML form elements with associated labels for all input fields on the contact information step. | Inspection: Review the contact info view; verify `label` elements with `for` attributes match `input` `id` attributes. |
| A11-012 | The booking system shall render time slot options as focusable buttons navigable with Tab and arrow keys. | Demonstration: Navigate slots with Tab and arrow keys; verify focus moves between slots. |

---

## 20. Display & Formatting

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| DSP-001 | The booking system shall display availability window dates in a human-readable format (e.g., "Mon Feb 16, 2026"). | Demonstration: View the availability confirmation step; verify dates are formatted readably. |
| DSP-002 | The booking system shall display time ranges in a human-readable format (e.g., "9:00 AM – 5:00 PM"). | Demonstration: View parsed windows and slot options; verify times are formatted readably. |
| DSP-003 | The booking system shall display timezone names with underscores replaced by spaces and slashes padded with spaces (e.g., "America / New York"). | Test: Verify `formatTimezoneName "America/New_York"` produces "America / New York". |

---

## 21. Agent Accessibility

### Ubiquitous Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| AGT-001 | The booking system shall assign a stable `id` attribute to every interactive element (inputs, buttons, textareas) in the booking flow. | Inspection: Review the View module; verify IDs are present on all interactive elements. |
| AGT-002 | The booking system shall provide an accessible name for every text input, email input, telephone input, and textarea in the booking flow via a `label` element with a matching `for` attribute, an `aria-label` attribute, or a `placeholder` attribute. A `label` element is preferred for the contact information step where multiple fields appear together. | Inspection: Review each form step; verify each input has an accessible name. Verify contact info inputs use `label` elements with matching `for` attributes. |
| AGT-003 | The booking system shall use standard HTML `form` elements with submit handlers for every step that collects input. | Inspection: Review each step view; verify `Html.form` with `onSubmit` wraps the inputs and submit button. |
| AGT-004 | The booking system shall use descriptive text content in all buttons (e.g., "Confirm booking", "Find slots") rather than icon-only or image-only buttons. | Inspection: Review all button elements; verify each has human-readable text content. |
| AGT-005 | The booking system shall not convey information required to complete the booking flow solely through CSS, color, or visual styling. | Inspection: Review each step; verify all actionable information (errors, slot times, field labels) is present in the DOM text content. |

---

## 22. Error Display

### State-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| ERR-001 | While an error message is present, the booking system shall display it in a visually distinct error banner above the step content. | Test: Trigger any validation error; verify the error banner is displayed with appropriate styling. |

### Event-Driven Requirements

| ID | Requirement | Verification |
|----|-------------|--------------|
| ERR-010 | When the participant performs a successful action, the booking system shall clear any previously displayed error message. | Test: Trigger an error, then perform a valid action; verify the error banner disappears. |
| ERR-011 | When the participant navigates back, the booking system shall clear any displayed error message. | Test: Trigger an error, then click back; verify the error is cleared. |

---

## Traceability Notes

### Deferred from specification (not yet implemented)

- **Screenshot upload parsing** — Design document describes image-based availability input via AI vision model. Not yet implemented; will need its own requirements section.
- **External screenshot prompt** — Privacy-preserving prompt-based flow for participants to parse their own calendar screenshots. Not yet implemented.
- **Email notifications** — Confirmation emails and cancellation notifications are referenced in the design but not yet wired into the booking flow.
- **Cancellation from participant** — Design allows either party to cancel, but the booking page does not currently expose cancellation. Cancellation is only available via the admin dashboard.
- **Rate limiting** — Design specifies aggressive per-IP and per-endpoint rate limiting, especially for AI endpoints. Not yet implemented.
- **Proxy booking** — Design allows the host to use the booking flow on behalf of someone else. No special handling exists yet.
- **Video conferencing link** — Design references Zoom/Google Meet link inclusion. The `SchedulingSettings` type has a `VideoLink` field, but it is not surfaced in the booking confirmation or calendar invite.
- **Calendar invite (.ics) generation** — Design specifies sending `.ics` files. Not yet implemented in the booking flow.

### Assumptions

- The "host" is a single user; there is no multi-tenant or multi-host scenario.
- The AI model used for parsing is Gemini 3 Flash, as recommended by the spike results.
- The booking system operates as a single-instance deployment with SQLite.
- All backend validation mirrors frontend validation to prevent bypass via direct API calls.

### Resolved questions

- **SLT-006 / SLT-007:** Scheduling constraints (minimum notice, booking window) remain opaque to participants. Slots outside the window simply do not appear. The existing empty-state message ("No overlapping slots found") with the "try different times" prompt is sufficient for a single-host personal tool. No constraint values are surfaced to the frontend.
- **AGT-001–005:** Agent accessibility is verified by inspection against five concrete structural properties (stable IDs, label associations, standard form elements, descriptive button text, no CSS-only information). No automated agent testing infrastructure is required.
