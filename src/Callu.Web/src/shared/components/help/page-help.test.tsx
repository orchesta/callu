import { describe, it, expect } from 'vitest';
import en from '@/shared/locales/en.json';
import tr from '@/shared/locales/tr.json';
import { resolveGuideKey, relatedPages } from './guide-routes';

/** Every guide the locale files define, including the ones no sidebar entry points at. */
const ALL_GUIDE_KEYS = [
  ...new Set(
    Object.keys(en)
      .filter((k) => k.startsWith('help.') && k.split('.').length === 3)
      .map((k) => k.split('.')[1]),
  ),
];

// Every destination the sidebar offers. A page with no guide renders no "?" button at all,
// which is invisible until someone goes looking for help and finds none.
const SIDEBAR_ROUTES = [
  '/dashboard',
  '/incidents',
  '/conferences',
  '/services',
  '/escalations',
  '/schedules',
  '/teams',
  '/reports',
  '/postmortems',
  '/runbooks',
  '/call-logs',
  '/maintenance',
  '/audit-logs',
  '/diagnostics',
  '/settings',
  '/users',
  '/settings/communications',
  '/settings/email-templates',
  '/settings/status-page',
];

describe('page help coverage', () => {
  // Everything below iterates this list, so an empty one would let every check pass on nothing.
  it('found the guides to check', () => {
    expect(ALL_GUIDE_KEYS.length).toBeGreaterThanOrEqual(SIDEBAR_ROUTES.length);
  });

  it('resolves a guide for every sidebar destination', () => {
    const missing = SIDEBAR_ROUTES.filter((route) => resolveGuideKey(route) === null);
    expect(missing).toEqual([]);
  });

  it('has title, summary and steps for every resolved guide, in both languages', () => {
    const keys = [...new Set(SIDEBAR_ROUTES.map((r) => resolveGuideKey(r)))].filter(
      (k): k is string => k !== null,
    );

    const incomplete: string[] = [];
    for (const key of keys) {
      for (const [lang, dict] of [['en', en], ['tr', tr]] as const) {
        const bag = dict as Record<string, string>;
        for (const field of ['title', 'summary', 'steps']) {
          const value = bag[`help.${key}.${field}`];
          if (typeof value !== 'string' || value.trim().length === 0) {
            incomplete.push(`${lang}:help.${key}.${field}`);
          }
        }
      }
    }

    expect(incomplete).toEqual([]);
  });

  // Every guide had a tip, and every tip restated a button already on screen. What an operator is
  // actually stuck on is why nobody was paged, and that answer lives on a different page.
  it('names the common mistake for every guide, in both languages', () => {
    const missing: string[] = [];
    for (const key of ALL_GUIDE_KEYS) {
      for (const [lang, dict] of [['en', en], ['tr', tr]] as const) {
        const value = (dict as Record<string, string>)[`help.${key}.pitfall`];
        if (typeof value !== 'string' || value.trim().length < 80) {
          missing.push(`${lang}:help.${key}.pitfall`);
        }
      }
    }

    expect(missing).toEqual([]);
  });

  it('points every guide at the pages it depends on', () => {
    const stranded = ALL_GUIDE_KEYS.filter((key) => relatedPages(key).length === 0);

    expect(stranded).toEqual([]);
  });

  it('never links a guide back to its own page', () => {
    const selfReferential = ALL_GUIDE_KEYS.filter((key) =>
      relatedPages(key).some((page) => resolveGuideKey(page.to) === key),
    );

    expect(selfReferential).toEqual([]);
  });

  it('links only to routes that resolve, with a label in both languages', () => {
    const broken: string[] = [];
    for (const key of ALL_GUIDE_KEYS) {
      for (const page of relatedPages(key)) {
        if (resolveGuideKey(page.to) === null) broken.push(`${key} -> ${page.to}`);
        for (const [lang, dict] of [['en', en], ['tr', tr]] as const) {
          if (!(dict as Record<string, string>)[page.labelKey]) {
            broken.push(`${lang}:${page.labelKey}`);
          }
        }
      }
    }

    expect(broken).toEqual([]);
  });

  it('routes the incident detail page to its own guide, not the list one', () => {
    expect(resolveGuideKey('/incidents/019fceb5-b835-74e4-8f18-03492dcf901b')).toBe('incidentDetail');
    expect(resolveGuideKey('/incidents')).toBe('incidents');
  });

  it('prefers the specific settings guides over the general one', () => {
    expect(resolveGuideKey('/settings/status-page')).toBe('statusPage');
    expect(resolveGuideKey('/settings')).toBe('settings');
  });
});
