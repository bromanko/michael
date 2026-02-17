/**
 * DST-aware UTC offset computation using cached `Intl.DateTimeFormat`
 * instances. Caching avoids the overhead of constructing a new formatter
 * on every call — significant when probing 20+ candidate days.
 */

const formatters = new Map<string, Intl.DateTimeFormat>();

function getFormatter(tz: string): Intl.DateTimeFormat {
  let fmt = formatters.get(tz);

  if (!fmt) {
    fmt = new Intl.DateTimeFormat("en-US", {
      timeZone: tz,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
    });
    formatters.set(tz, fmt);
  }

  return fmt;
}

/**
 * Compute the UTC offset string (e.g. "-05:00", "+00:00") for a given date
 * in a given IANA timezone. Uses a cached `Intl.DateTimeFormat` so it
 * respects DST boundaries automatically.
 */
export function utcOffsetFor(date: Date, tz: string): string {
  const parts = getFormatter(tz).formatToParts(date);

  const get = (type: string) =>
    parts.find((p) => p.type === type)?.value ?? "0";
  const localDate = new Date(
    `${get("year")}-${get("month")}-${get("day")}T${get("hour")}:${get("minute")}:${get("second")}Z`,
  );

  // Difference between the UTC interpretation of the local time and the
  // actual UTC instant gives us the offset.
  const diffMs = localDate.getTime() - date.getTime();
  const totalMinutes = Math.round(diffMs / 60_000);
  const sign = totalMinutes >= 0 ? "+" : "-";
  const absMin = Math.abs(totalMinutes);
  const hh = String(Math.floor(absMin / 60)).padStart(2, "0");
  const mm = String(absMin % 60).padStart(2, "0");
  return `${sign}${hh}:${mm}`;
}
