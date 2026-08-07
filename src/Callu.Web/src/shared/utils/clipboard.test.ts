import { describe, it, expect, vi, afterEach } from 'vitest';
import { copyText } from './clipboard';

/**
 * Whether a copy actually landed, which is what the callers paint their checkmark on.
 */
// The default deployment is plain HTTP, where navigator.clipboard is absent entirely, so
// "it resolved" and "it copied" are not the same question.
describe('copyText', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function stubClipboard(writeText: unknown) {
    vi.stubGlobal('navigator', { ...navigator, clipboard: writeText ? { writeText } : undefined });
  }

  // jsdom ships no execCommand at all, which is also why the fallback calls it optionally.
  function stubExecCommand(result: boolean) {
    const fn = vi.fn().mockReturnValue(result);
    Object.defineProperty(document, 'execCommand', { value: fn, configurable: true, writable: true });
    return fn;
  }

  it('reports success when the clipboard accepts the text', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stubClipboard(writeText);

    expect(await copyText('secret-key')).toBe(true);
    expect(writeText).toHaveBeenCalledWith('secret-key');
  });

  it('does not report success when the clipboard rejects', async () => {
    stubClipboard(vi.fn().mockRejectedValue(new Error('denied')));
    stubExecCommand(false);

    expect(await copyText('secret-key')).toBe(false);
  });

  /// The plain-HTTP case: there is no clipboard object at all, and the old code threw here.
  it('falls back instead of throwing when there is no clipboard at all', async () => {
    stubClipboard(undefined);
    const execCommand = stubExecCommand(true);

    expect(await copyText('secret-key')).toBe(true);
    expect(execCommand).toHaveBeenCalledWith('copy');
  });

  it('reports failure when the fallback cannot copy either', async () => {
    stubClipboard(undefined);
    stubExecCommand(false);

    expect(await copyText('secret-key')).toBe(false);
  });

  it('leaves no scratch node behind', async () => {
    stubClipboard(undefined);
    stubExecCommand(true);

    await copyText('secret-key');

    expect(document.querySelector('textarea')).toBeNull();
  });
});
