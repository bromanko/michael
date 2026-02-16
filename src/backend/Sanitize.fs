module Michael.Sanitize

open System

// ---------------------------------------------------------------------------
// User-input sanitization
//
// All participant-supplied strings should pass through these helpers before
// being stored, logged, or rendered in emails. This prevents:
//   • Email header injection via \r\n in name/title
//   • Log injection via newlines
//   • Excessively long fields consuming storage or breaking layouts
// ---------------------------------------------------------------------------

/// Remove ASCII control characters (U+0000–U+001F, U+007F) from a string.
/// Preserves normal whitespace (spaces) but strips tabs, newlines, carriage
/// returns, null bytes, and other non-printable characters.
let stripControlChars (s: string) : string =
    if isNull s then
        ""
    else
        String(s |> Seq.filter (fun c -> not (Char.IsControl c)) |> Seq.toArray)

/// Enforce a maximum character length, truncating if necessary.
let truncate (maxLen: int) (s: string) : string =
    if isNull s || s.Length <= maxLen then
        s
    else
        s.Substring(0, maxLen)

/// Maximum lengths for participant-supplied fields. Keep in sync with any
/// database column constraints or UI limits.
[<RequireQualifiedAccess>]
module MaxLength =
    let name = 200
    let email = 254 // RFC 5321 max
    let phone = 30
    let title = 300
    let description = 2000

/// Sanitize a user-supplied string: strip control characters, trim
/// whitespace, and enforce a maximum length.
let sanitizeField (maxLen: int) (s: string) : string =
    s |> stripControlChars |> (fun s -> s.Trim() |> truncate maxLen)

/// Sanitize common booking fields. Returns the original record with all
/// string fields cleaned.
let sanitizeName (s: string) = sanitizeField MaxLength.name s
let sanitizeEmail (s: string) = sanitizeField MaxLength.email s
let sanitizePhone (s: string) = sanitizeField MaxLength.phone s
let sanitizeTitle (s: string) = sanitizeField MaxLength.title s
let sanitizeDescription (s: string) = sanitizeField MaxLength.description s
