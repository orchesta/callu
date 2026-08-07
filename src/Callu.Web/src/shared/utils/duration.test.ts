import { describe, it, expect } from 'vitest';
import { formatMinutes } from './duration';

describe('formatMinutes', () => {
  it('keeps seconds below a minute instead of collapsing to 0m', () => {
    expect(formatMinutes(26 / 60)).toBe('26s');
    expect(formatMinutes(32 / 60)).toBe('32s');
    expect(formatMinutes(0)).toBe('0s');
  });

  it('formats minutes, hours and days the way the dashboard does', () => {
    expect(formatMinutes(1)).toBe('1m');
    expect(formatMinutes(45)).toBe('45m');
    expect(formatMinutes(90)).toBe('1h 30m');
    expect(formatMinutes(60)).toBe('1h 0m');
    expect(formatMinutes(1440)).toBe('1d');
    expect(formatMinutes(2880)).toBe('2d');
  });

  it('does not present a missing measurement as a duration', () => {
    expect(formatMinutes(Number.NaN)).toBe('—');
    expect(formatMinutes(-1)).toBe('—');
  });
});
