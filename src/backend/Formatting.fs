module Michael.Formatting

open NodaTime.Text

/// Pattern for formatting OffsetDateTime: always includes full ±HH:mm offset
/// (e.g. 2026-02-20T13:00:00-05:00). Shared across handler modules.
let odtFormatPattern =
    OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'sso<+HH':'mm>")
