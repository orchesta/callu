import { cultureFor, getLocale } from "@/shared/locales/i18n";

/**
 * The BCP 47 tag dates are formatted with: the language chosen in the app.
 *
 * Passing nothing to `toLocaleDateString` uses the browser's language instead, which is a different
 * setting and is often a different language; hardcoding a tag ignores the choice altogether.
 */
export function dateLocale(): string {
  return cultureFor(getLocale());
}
