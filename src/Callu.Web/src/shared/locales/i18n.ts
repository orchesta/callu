// Flat-key i18n with lazily loaded locale files and dynamic language switching.

import en from "./en.json";

export type LocaleCode = "en" | "tr";

export interface LocaleInfo {
    code: LocaleCode;
    name: string;
    nativeName: string;
    dir: "ltr" | "rtl";
    /** The culture the backend stores and speaks in — what a voice call is read in. */
    culture: string;
}

export const SUPPORTED_LOCALES: LocaleInfo[] = [
    { code: "en", name: "English", nativeName: "English", dir: "ltr", culture: "en-US" },
    { code: "tr", name: "Turkish", nativeName: "Türkçe", dir: "ltr", culture: "tr-TR" },
];

const STORAGE_KEY = "callu_locale";

let currentLocale: LocaleCode = (localStorage.getItem(STORAGE_KEY) as LocaleCode) || "en";

/** True until someone picks a language on this device; the stored account choice may fill it in. */
export function hasDeviceLocaleChoice(): boolean {
    return localStorage.getItem(STORAGE_KEY) !== null;
}
let strings: Record<string, string> = en;
let fallback: Record<string, string> = en;

type LocaleChangeListener = (locale: LocaleCode) => void;
const listeners: Set<LocaleChangeListener> = new Set();

const loaders: Record<LocaleCode, () => Promise<{ default: Record<string, string> }>> = {
    en: () => Promise.resolve({ default: en }),
    tr: () => import("./tr.json"),
};

/**
 * Get the current active locale code.
 */
export function getLocale(): LocaleCode {
    return currentLocale;
}

/**
 * Switch the active locale. Loads the locale file lazily if needed.
 * Returns a promise that resolves when the locale is ready.
 */
export async function setLocale(code: LocaleCode): Promise<void> {
    if (!loaders[code]) {
        console.warn(`[i18n] Unsupported locale: ${code}, falling back to 'en'`);
        code = "en";
    }

    try {
        const module = await loaders[code]();
        strings = module.default;
        currentLocale = code;
        localStorage.setItem(STORAGE_KEY, code);

        const info = SUPPORTED_LOCALES.find((l) => l.code === code);
        if (info) {
            document.documentElement.dir = info.dir;
            document.documentElement.lang = code;
        }

        listeners.forEach((fn) => fn(code));
    } catch (err) {
        console.error(`[i18n] Failed to load locale '${code}':`, err);
        strings = en;
        currentLocale = "en";
    }
}

/**
 * Subscribe to locale changes. Returns an unsubscribe function.
 */
export function onLocaleChange(fn: LocaleChangeListener): () => void {
    listeners.add(fn);
    return () => listeners.delete(fn);
}

/** Translated string for a dot-notated key, falling back to English and then to the key itself. */
export function t(key: string, params?: Record<string, string | number>): string {
    let value = strings[key] ?? fallback[key];

    if (value === undefined) {
        return key;
    }

    if (params) {
        for (const [paramKey, paramValue] of Object.entries(params)) {
            value = value.replace(new RegExp(`\\{${paramKey}\\}`, 'g'), String(paramValue));
        }
    }

    return value;
}

if (currentLocale !== "en") {
    setLocale(currentLocale);
}

/** The backend culture for a UI locale, e.g. "tr" to "tr-TR". */
export function cultureFor(code: LocaleCode): string {
    return SUPPORTED_LOCALES.find((l) => l.code === code)?.culture ?? "en-US";
}

/** The UI locale for a stored backend culture, e.g. "tr-TR" to "tr". */
export function localeForCulture(culture: string | null | undefined): LocaleCode | null {
    if (!culture) return null;
    const language = culture.split("-")[0].toLowerCase();
    return SUPPORTED_LOCALES.find((l) => l.code === language)?.code ?? null;
}
