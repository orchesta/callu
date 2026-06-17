/**
 * Timezone helpers. Uses Intl.DateTimeFormat directly; the full IANA dataset
 * (@vvo/tzdb) is only loaded by the picker component.
 *
 * Wire format:
 *   - LocalDateTime: "2026-04-20T09:00:00" (no offset), interpreted in the schedule's TZ.
 *   - Instant: "2026-04-20T09:00:00Z".
 */

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

/**
 * Interpret a wall-clock LocalDateTime string in the given timezone and return the UTC
 * Instant it maps to. Doesn't handle DST gaps/ambiguities as carefully as the backend
 * does — good enough for form preview and override submission.
 */
export function localDateTimeToUtcInstant(localIso: string, timezone: string): Date {
    const asLocal = new Date(localIso);
    const zoneTime = new Date(asLocal.toLocaleString("en-US", { timeZone: timezone }));
    const offsetMs = asLocal.getTime() - zoneTime.getTime();
    return new Date(asLocal.getTime() + offsetMs);
}
