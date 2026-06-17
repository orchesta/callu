/**
 * Multi-language i18n helper for CalluApp frontend.
 * Supports dynamic language switching with lazy-loaded locale files.
 *
 * Usage:
 *   import { t, setLocale, getLocale, SUPPORTED_LOCALES } from "@/shared/locales/i18n";
 *   t("common.save")           → "Save"
 *   t("tts.messages", { count: "5" }) → "5 messages"
 *   setLocale("tr")            → switches to Turkish
 */

import en from "./en.json";

export type LocaleCode = "en" | "tr";

export interface LocaleInfo {
    code: LocaleCode;
    name: string;
    nativeName: string;
    dir: "ltr" | "rtl";
}

export const SUPPORTED_LOCALES: LocaleInfo[] = [
    { code: "en", name: "English", nativeName: "English", dir: "ltr" },
    { code: "tr", name: "Turkish", nativeName: "Türkçe", dir: "ltr" },
];

const STORAGE_KEY = "callu_locale";

let currentLocale: LocaleCode = (localStorage.getItem(STORAGE_KEY) as LocaleCode) || "en";
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

/**
 * Get a translated string by key, with optional parameter substitution.
 * Falls back to English string, then to the key itself if not found.
 *
 * @param key - Dot-notated locale key (e.g., "common.save")
 * @param params - Optional key-value pairs for {placeholder} substitution
 * @returns The translated string
 */
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
