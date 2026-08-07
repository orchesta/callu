// Cross-tab mutex for the token refresh: presenting the same refresh token twice revokes the whole
// family, so two tabs renewing at once would sign the operator out of both.

/** Origin-scoped: every tab of this install queues on the same name. */
const LOCK_NAME = 'callu:auth:refresh';

/** Same name for the election channel — a different namespace, so no collision with the Web Lock. */
const CHANNEL_NAME = 'callu:auth:refresh';

/** localStorage key holding the fallback lease. */
const LEASE_KEY = 'callu:auth:refresh:lock';

/** Lease lifetime. Longer than a refresh call, so a crashed holder's lease expires by itself. */
const LEASE_TTL_MS = 15_000;

/** After claiming a free lease, wait this long and re-read: a racing tab's write lands last and wins. */
const CLAIM_SETTLE_MS = 40;

/** Poll interval while another tab holds the lease or the leader is still working. */
const POLL_INTERVAL_MS = 40;

/** Added to every wait. Two tabs that started together stop reading back together. */
const JITTER_MS = 60;

/** How long a contender listens for rival claims before deciding it won. */
const CLAIM_WINDOW_MS = 60;

/** Second window: a claim that crossed ours in flight arrives here and can still displace us. */
const CONFIRM_WINDOW_MS = 60;

/** Total time spent waiting for other tabs before proceeding regardless. Above the lease TTL. */
const MAX_WAIT_MS = 20_000;

interface Lease {
  owner: string;
  expiresAt: number;
}

type ElectionMessage =
  | { k: 'claim'; id: string }
  | { k: 'lead'; id: string }
  | { k: 'busy'; id: string }
  | { k: 'done'; id: string };

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function jitter(): number {
  return Math.floor(Math.random() * JITTER_MS);
}

/** Owner id that needs no secure context, timestamp-prefixed so `min(id)` favours whoever asked first. */
function newOwnerId(): string {
  return `${Date.now().toString(36).padStart(9, '0')}-${Math.random().toString(36).slice(2, 10)}`;
}

/* ---------------------------------------------------------------- barrier 1: leader election */

interface Election {
  /** Resolves when this caller may run the guarded work. */
  takeTurn(deadline: number): Promise<void>;
  /** Tells the waiting tabs the turn is over. */
  close(): void;
}

function openElection(): Election | null {
  if (typeof BroadcastChannel === 'undefined') return null;

  let channel: BroadcastChannel;
  try {
    channel = new BroadcastChannel(CHANNEL_NAME);
  } catch {
    return null;
  }

  const id = newOwnerId();
  const contenders = new Set<string>();
  let busySeen = false;
  let doneSeen = false;
  let leading = false;
  let closed = false;

  function post(k: ElectionMessage['k']): void {
    try {
      channel.postMessage({ k, id } satisfies ElectionMessage);
    } catch {
      /* channel gone; the lease still guards the work */
    }
  }

  channel.onmessage = (event: MessageEvent<unknown>) => {
    const message = event.data as Partial<ElectionMessage> | null | undefined;
    if (!message || typeof message.id !== 'string' || message.id === id) return;

    switch (message.k) {
      case 'claim':
        contenders.add(message.id);
        // A tab that arrived while we were working: tell it to wait rather than let it stand.
        if (leading) post('busy');
        break;
      case 'lead':
        contenders.add(message.id);
        break;
      case 'busy':
        busySeen = true;
        break;
      case 'done':
        doneSeen = true;
        break;
      default:
        break;
    }
  };

  function outrankedByAContender(): boolean {
    for (const other of contenders) {
      if (other < id) return true;
    }
    return false;
  }

  async function tryLead(): Promise<boolean> {
    contenders.clear();
    busySeen = false;
    doneSeen = false;

    post('claim');
    await delay(CLAIM_WINDOW_MS + jitter());

    if (busySeen || outrankedByAContender()) return false;

    post('lead');
    await delay(CONFIRM_WINDOW_MS + jitter());

    // A rival claim that crossed ours in flight lands in this window. Both tabs compare the same
    // pair of ids and reach the same answer, so the late message resolves the tie rather than
    // producing two leaders.
    if (busySeen || outrankedByAContender()) return false;

    leading = true;
    return true;
  }

  async function awaitDone(deadline: number): Promise<boolean> {
    while (!doneSeen) {
      if (Date.now() >= deadline) return false;
      await delay(POLL_INTERVAL_MS + jitter());
    }
    return true;
  }

  return {
    async takeTurn(deadline: number): Promise<void> {
      while (Date.now() < deadline) {
        if (await tryLead()) return;

        // Someone else is refreshing. Wait for them to report back, then stand again: the tabs
        // still queued elect one of themselves instead of all restarting at once. Whoever wins
        // the next round finds the token already renewed and adopts it.
        if (!(await awaitDone(deadline))) break;
      }

      // Out of time — the holder never reported and is presumably gone. Proceed (the lease is
      // still in front of the work), but announce ourselves so a tab arriving now waits for us
      // rather than starting a third refresh alongside.
      leading = true;
    },

    close(): void {
      if (closed) return;
      closed = true;

      if (leading) post('done');

      try {
        channel.close();
      } catch {
        /* already closed */
      }
    },
  };
}

/* -------------------------------------------------------------------- barrier 2: storage lease */

function readLease(): Lease | null {
  const raw = localStorage.getItem(LEASE_KEY);
  if (!raw) return null;

  try {
    const parsed: unknown = JSON.parse(raw);
    if (
      typeof parsed !== 'object' ||
      parsed === null ||
      typeof (parsed as Lease).owner !== 'string' ||
      typeof (parsed as Lease).expiresAt !== 'number'
    ) {
      return null;
    }

    return parsed as Lease;
  } catch {
    return null;
  }
}

async function acquireLease(owner: string, deadline: number): Promise<boolean> {
  for (;;) {
    const lease = readLease();
    const now = Date.now();

    if (!lease || lease.expiresAt <= now) {
      localStorage.setItem(LEASE_KEY, JSON.stringify({ owner, expiresAt: now + LEASE_TTL_MS }));
      await delay(CLAIM_SETTLE_MS + jitter());

      // Two tabs can find the lease free and both write. The last write is what both read back,
      // so exactly one of them sees its own id here; the other returns to waiting. The jitter
      // keeps the two read-backs from landing in the same instant.
      if (readLease()?.owner === owner) return true;
    } else {
      await delay(POLL_INTERVAL_MS + jitter());
    }

    if (Date.now() >= deadline) return false;
  }
}

function releaseLease(owner: string): void {
  try {
    if (readLease()?.owner === owner) {
      localStorage.removeItem(LEASE_KEY);
    }
  } catch {
    /* storage went away; the lease expires on its own */
  }
}

async function withLease<T>(fn: () => Promise<T>, deadline: number): Promise<T> {
  const owner = newOwnerId();
  let held = false;

  try {
    held = await acquireLease(owner, deadline);
  } catch {
    // localStorage unavailable (private mode, storage disabled). Nothing to serialise on.
    return fn();
  }

  try {
    // Waiting timed out: the lease is held by a tab that is not releasing it and whose TTL keeps
    // getting renewed by someone. Refusing to refresh would sign the user out for certain;
    // refreshing may collide. We take the collision.
    return await fn();
  } finally {
    if (held) releaseLease(owner);
  }
}

/* --------------------------------------------------------------------------------- composition */

async function withFallbackLock<T>(fn: () => Promise<T>): Promise<T> {
  const deadline = Date.now() + MAX_WAIT_MS;
  const election = openElection();

  try {
    if (election) await election.takeTurn(deadline);

    return await withLease(fn, deadline);
  } finally {
    // Only now, with the work finished, do the waiting tabs get their turn — they will find the
    // token already renewed.
    election?.close();
  }
}

/** Runs `fn` exactly once, with at most one caller per origin inside it at a time. */
export async function withRefreshLock<T>(fn: () => Promise<T>): Promise<T> {
  const lockManager = typeof navigator !== 'undefined' ? navigator.locks : undefined;

  if (!lockManager) {
    return withFallbackLock(fn);
  }

  let started = false;

  try {
    return await lockManager.request(LOCK_NAME, () => {
      started = true;
      return fn();
    });
  } catch (error) {
    if (started) throw error;
    // The lock manager itself refused (e.g. SecurityError). Fall back rather than run unguarded.
    return withFallbackLock(fn);
  }
}
