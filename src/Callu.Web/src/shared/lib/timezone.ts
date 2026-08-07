// Timezone helpers over Intl.DateTimeFormat; the full IANA dataset is loaded only by the picker.
// Wire format: an offset-less LocalDateTime is read in the schedule's zone, an Instant ends in Z.

export function formatForDisplay(instant: Date | string, timezone: string, locale = "en-US"): string {
    const d = typeof instant === "string" ? new Date(instant) : instant;
    return new Intl.DateTimeFormat(locale, {
        timeZone: timezone,
        dateStyle: "medium",
        timeStyle: "short",
    }).format(d);
}

/** Zone abbreviation (EDT, CET, ...) at a given instant. */
export function zoneAbbreviation(instant: Date | string, timezone: string): string {
    const d = typeof instant === "string" ? new Date(instant) : instant;
    const parts = new Intl.DateTimeFormat("en-US", {
        timeZone: timezone,
        timeZoneName: "short",
    }).formatToParts(d);
    return parts.find((p) => p.type === "timeZoneName")?.value ?? timezone;
}

export function browserTimezone(): string {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/** `YYYY-MM-DD` in the given zone; falls back to the browser's date if the zone is unrecognised. */
export function todayInZone(timezone: string, now: Date = new Date()): string {
    const pad = (n: number, width = 2) => String(n).padStart(width, "0");

    try {
        const parts = new Intl.DateTimeFormat("en-US", {
            timeZone: timezone,
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
        }).formatToParts(now);
        const field = (type: string) => Number(parts.find((p) => p.type === type)?.value);
        const [year, month, day] = [field("year"), field("month"), field("day")];
        if (!Number.isFinite(year * month * day)) throw new Error("Unreadable date parts");
        return `${pad(year, 4)}-${pad(month)}-${pad(day)}`;
    } catch {
        return `${pad(now.getFullYear(), 4)}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
    }
}

function zoneOffsetMs(utcMillis: number, timezone: string): number {
    const parts = new Intl.DateTimeFormat("en-US", {
        timeZone: timezone,
        hour12: false,
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
    }).formatToParts(new Date(utcMillis));

    const field = (type: string) => Number(parts.find((p) => p.type === type)?.value);
    const wallAsUtc = Date.UTC(
        field("year"),
        field("month") - 1,
        field("day"),
        field("hour") % 24,
        field("minute"),
        field("second"),
    );
    return wallAsUtc - utcMillis;
}

/** UTC Instant a wall-clock LocalDateTime maps to, using the offset at the target instant so DST holds. */
export function localDateTimeToUtcInstant(localIso: string, timezone: string): Date {
    const [datePart, timePart = "00:00:00"] = localIso.split("T");
    const [year, month, day] = datePart.split("-").map(Number);
    const [hour = 0, minute = 0, second = 0] = timePart.split(":").map(Number);

    const wallAsUtc = Date.UTC(year, month - 1, day, hour, minute, second);
    const firstGuess = zoneOffsetMs(wallAsUtc, timezone);
    const corrected = zoneOffsetMs(wallAsUtc - firstGuess, timezone);
    return new Date(wallAsUtc - corrected);
}
