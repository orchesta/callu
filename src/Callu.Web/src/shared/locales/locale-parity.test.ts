import { describe, it, expect } from 'vitest';
import en from './en.json';
import tr from './tr.json';

/**
 * The two locale files have to carry the same keys, and neither may leave a value untranslated.
 */
// A missing key renders as the key itself, which reads as a broken screen rather than as English.
describe('locale parity', () => {
  const enKeys = Object.keys(en);
  const trKeys = Object.keys(tr);

  it('has no key in one file that is missing from the other', () => {
    expect(enKeys.filter((k) => !(k in tr))).toEqual([]);
    expect(trKeys.filter((k) => !(k in en))).toEqual([]);
  });

  it('has no empty value on either side', () => {
    const blank = (entries: Record<string, string>) =>
      Object.entries(entries)
        .filter(([, v]) => typeof v !== 'string' || v.trim().length === 0)
        .map(([k]) => k);

    expect(blank(en as Record<string, string>)).toEqual([]);
    expect(blank(tr as Record<string, string>)).toEqual([]);
  });

  /// A placeholder that exists on one side only makes the other render a literal {name}.
  it('uses the same placeholders in both languages', () => {
    const placeholders = (value: string) =>
      [...value.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

    const mismatched = enKeys
      .filter((k) => k in tr)
      .filter((k) => {
        const a = placeholders(String((en as Record<string, string>)[k]));
        const b = placeholders(String((tr as Record<string, string>)[k]));
        return a.join(',') !== b.join(',');
      });

    expect(mismatched).toEqual([]);
  });
});
