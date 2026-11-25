using System.Security.Cryptography;
using Callu.Domain.Entities;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Audit;

public sealed record AuditChainSealResult(int SealedCount, long LastSequence, string? FailureReason = null, long PublicHead = 0, int PublicSealedCount = 0);

public sealed record AuditChainVerdict(
    bool Intact,
    long CheckedCount,
    long? FirstBrokenSequence,
    string? Reason);

/// <summary>Seals audit entries into a hash chain and verifies that chain.</summary>
public sealed class AuditChainService(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    ILogger<AuditChainService> logger,
    OpenAuditEventSource? source = null)
{
    /// <summary>Taken by every sealer, in the Worker and in an export, for one batch at a time.</summary>
    // [DisallowConcurrentExecution] holds only inside one scheduler, and an export seals from the API.
    // The unique index on Sequence stays as the backstop it was.
    internal const long SealLockKey = 7_243_010_001;

    internal const int SealBatchSize = 500;
    internal const int VerifyBatchSize = 1_000;
    private const string ProtectorPurpose = "Callu.AuditChain.v1";

    internal const string KeyUnreadableReason =
        "The audit chain key cannot be decrypted with the current Data Protection key ring. "
        + "Restore the original key ring (callu:dp-keys with the database); Callu will not mint a replacement.";

    private readonly OpenAuditEventSource _source = source ?? OpenAuditEventSource.Default;

    /// <summary>Chains every entry written since the last run into both chains, oldest first, in bounded batches.</summary>
    // Clears the context's change tracker before every batch, so call it on a context with nothing pending.
    // maxBatches stops after that many committed batches: a caller inside a request seals what it can wait
    // for and leaves the rest of a large backfill to the Worker.
    public async Task<AuditChainSealResult> SealAsync(CancellationToken ct = default, int? maxBatches = null)
    {
        var state = await LoadOrCreateStateAsync(ct);
        if (!TryUnprotectKey(state, out var key, out var failureReason))
        {
            // Detection only: do not remint ProtectedKey. New rows stay unsealed until the ring is restored.
            logger.LogError("Audit chain seal skipped: {Reason}", failureReason);
            return new AuditChainSealResult(0, state.LastSequence, failureReason, state.OamLastSequence);
        }

        var batchId = "seal-" + Guid.NewGuid().ToString("N");
        var total = 0;
        var publicTotal = 0;
        var batches = 0;
        string? publicStop = null;

        while (!ct.IsCancellationRequested)
        {
            // Nothing carried over from the last batch: the keyed check reads what the database holds,
            // not what this context loaded earlier, and the one-time backfill of a large table stays linear.
            db.BetweenBatches();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await TakeSealLockAsync(ct);

            // Read under the lock: another sealer may have moved the tails while this one waited.
            state = await db.AuditChainStates.FirstAsync(s => s.Id == AuditChainState.SingletonId, ct);
            state.ChainId ??= AuditChainState.NewChainId();

            var privateCount = await SealPrivateBatchAsync(state, key, ct);

            // Written inside the transaction, so the public pass's query sees what the private pass
            // just sealed and a new row joins both chains in one run.
            await db.SaveChangesAsync(ct);

            // A public stop ends the public pass for this run only; the keyed chain keeps sealing.
            var publicCount = 0;
            if (publicStop is null)
                (publicCount, publicStop) = await SealPublicBatchAsync(state, key, batchId, ct);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            total += privateCount;
            publicTotal += publicCount;

            if (privateCount < SealBatchSize && (publicStop is not null || publicCount < SealBatchSize)) break;
            if (++batches >= maxBatches) break;
        }

        if (publicStop is not null)
            logger.LogError("Audit public chain stopped: {Reason}", publicStop);

        return new AuditChainSealResult(total, state.LastSequence, publicStop, state.OamLastSequence, publicTotal);
    }

    private async Task<int> SealPrivateBatchAsync(AuditChainState state, byte[] key, CancellationToken ct)
    {
        var batch = await db.AuditLogs
            .Where(l => l.Sequence == null)
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Take(SealBatchSize)
            .ToListAsync(ct);

        foreach (var entry in batch)
        {
            var sequence = state.LastSequence + 1;
            var hash = AuditChainHash.Compute(entry, sequence, state.LastHash, key, CalluAuditChain.CanonicalizationId);

            entry.Sequence = sequence;
            entry.PrevHash = state.LastHash;
            entry.RowHash = hash;
            entry.CanonicalizationVersion = CalluAuditChain.CanonicalizationId;

            state.LastSequence = sequence;
            state.LastHash = hash;
        }

        return batch.Count;
    }

    /// <summary>Extends the public chain over sealed rows, after checking each one's place and private hash.</summary>
    // The public chain is never extended over a row the keyed chain says was altered, nor across a gap
    // or back over a row it already holds: it stops before it, keeps what it sealed, and the daily
    // verify reports the break. Before the chain starts, it starts at the oldest row retention kept.
    private async Task<(int Count, string? Broken)> SealPublicBatchAsync(
        AuditChainState state, byte[] key, string batchId, CancellationToken ct)
    {
        var batch = await db.AuditLogs
            .Where(l => l.Sequence != null && l.OamHash == null)
            .OrderBy(l => l.Sequence)
            .Take(SealBatchSize)
            .ToListAsync(ct);

        var expected = state.OamLastSequence > 0 ? state.OamLastSequence + 1 : state.PrunedThroughSequence + 1;
        var count = 0;
        foreach (var entry in batch)
        {
            var sequence = entry.Sequence!.Value;

            if (sequence != expected)
                return (count, $"The public chain expected entry {expected} next but found {sequence}; it stops before it.");

            var recomputed = AuditChainHash.Compute(
                entry, sequence, entry.PrevHash, key, CalluAuditChain.VersionOf(entry.CanonicalizationVersion));

            if (!CryptographicEquals(recomputed, entry.RowHash))
                return (count, $"Entry {sequence} no longer matches its chain hash; the public chain stops before it.");

            OpenAuditSealedEvent sealedEvent;
            try
            {
                sealedEvent = OpenAuditEventBuilder.Seal(entry,
                    new OpenAuditSealContext(_source, state.ChainId!, batchId, state.OamLastHash));
            }
            catch (Exception ex)
            {
                // One row the builder cannot read must not roll back the batch, and the keyed pass with it.
                logger.LogError(ex, "Audit entry {Sequence} could not be built as an OpenAuditModel event", sequence);
                return (count, $"Entry {sequence} could not be built as an OpenAuditModel event ({ex.GetType().Name}); the public chain stops before it.");
            }

            entry.OamEvent = sealedEvent.Canonical;
            entry.OamHash = sealedEvent.Hash;
            db.AuditChainLeaves.Add(new AuditChainLeaf { Sequence = sequence, Hash = sealedEvent.Hash });

            state.OamLastSequence = sequence;
            state.OamLastHash = sealedEvent.Hash;
            expected = sequence + 1;
            count++;
        }

        return (count, null);
    }

    /// <summary>Replays the chain in sequence order and reports the first entry that does not fit.</summary>
    public async Task<AuditChainVerdict> VerifyAsync(CancellationToken ct = default)
    {
        var state = await LoadStateAsync(ct);

        if (state is null)
            return new AuditChainVerdict(true, 0, null, "The chain has not been started yet.");

        if (!TryUnprotectKey(state, out var key, out var failureReason))
        {
            logger.LogError("Audit chain verify cannot run: {Reason}", failureReason);
            return new AuditChainVerdict(false, 0, null, failureReason);
        }

        // Retention deletes the oldest entries, so the surviving chain legitimately starts partway
        // in. It still has to link back to the hash recorded when those entries were pruned.
        var expectedPrevHash = state.PrunedThroughSequence > 0 ? state.PrunedThroughHash : null;
        long? expectedSequence = state.PrunedThroughSequence > 0 ? state.PrunedThroughSequence + 1 : 1;
        long verified = 0;
        long cursor = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.AuditLogs
                .AsNoTracking()
                .Where(l => l.Sequence != null && l.Sequence > cursor)
                .OrderBy(l => l.Sequence)
                .Take(VerifyBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var entry in batch)
            {
                var sequence = entry.Sequence!.Value;

                if (sequence != expectedSequence)
                    return new AuditChainVerdict(false, verified, sequence,
                        $"Expected sequence {expectedSequence} but found {sequence}; entries are missing or were renumbered.");

                if (entry.PrevHash != expectedPrevHash)
                    return new AuditChainVerdict(false, verified, sequence,
                        "This entry does not link back to the one before it.");

                // The form the row was sealed under, not the current one: a row still verifies after
                // the canonical form grows, which is the only way it can ever grow.
                var recomputed = AuditChainHash.Compute(
                    entry, sequence, entry.PrevHash, key,
                    CalluAuditChain.VersionOf(entry.CanonicalizationVersion));

                if (!CryptographicEquals(recomputed, entry.RowHash))
                    return new AuditChainVerdict(false, verified, sequence,
                        "This entry's contents no longer match its hash.");

                expectedPrevHash = entry.RowHash;
                expectedSequence = sequence + 1;
                cursor = sequence;
                verified++;
            }

            if (batch.Count < VerifyBatchSize) break;
        }

        if (state.LastSequence > 0 && cursor != state.LastSequence)
            return new AuditChainVerdict(false, verified, cursor + 1,
                $"The chain ends at {cursor} but was sealed through {state.LastSequence}; the tail is missing.");

        return new AuditChainVerdict(true, verified, null, null);
    }

    /// <summary>Records how far retention has deleted, so the surviving chain still verifies.</summary>
    public async Task NotePrunedAsync(CancellationToken ct = default)
    {
        // Under the seal lock, so a sealer's batch and this update never write the state row over each other.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await TakeSealLockAsync(ct);

        var state = await LoadStateAsync(ct);

        if (state is null) return;

        // This context may have loaded the row before the lock was taken.
        await db.Entry(state).ReloadAsync(ct);

        var oldest = await db.AuditLogs
            .AsNoTracking()
            .Where(l => l.Sequence != null)
            .OrderBy(l => l.Sequence)
            .FirstOrDefaultAsync(ct);

        // Everything sealed is gone: the chain restarts from the tail it had reached.
        if (oldest is null)
        {
            if (state.LastSequence > state.PrunedThroughSequence)
            {
                state.PrunedThroughSequence = state.LastSequence;
                state.PrunedThroughHash = state.LastHash;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return;
        }

        var prunedThrough = oldest.Sequence!.Value - 1;
        if (prunedThrough <= state.PrunedThroughSequence) return;

        state.PrunedThroughSequence = prunedThrough;
        state.PrunedThroughHash = oldest.PrevHash;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Audit chain: retention removed entries through sequence {Sequence}", prunedThrough);
    }

    private async Task TakeSealLockAsync(CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SealLockKey})", ct);

    private Task<AuditChainState?> LoadStateAsync(CancellationToken ct) =>
        db.AuditChainStates.FirstOrDefaultAsync(s => s.Id == AuditChainState.SingletonId, ct);

    private async Task<AuditChainState> LoadOrCreateStateAsync(CancellationToken ct)
    {
        var state = await LoadStateAsync(ct);

        if (state is not null) return state;

        state = new AuditChainState
        {
            Id = AuditChainState.SingletonId,
            ProtectedKey = Protector().Protect(Convert.ToBase64String(AuditChainHash.NewKey())),
        };

        db.AuditChainStates.Add(state);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another host created it first; the primary key is what makes that a race we can lose
            // safely rather than a second key nobody can verify against.
            db.Entry(state).State = EntityState.Detached;
            state = await db.AuditChainStates
                .FirstAsync(s => s.Id == AuditChainState.SingletonId, ct);
        }

        return state;
    }

    private byte[] UnprotectKey(AuditChainState state) =>
        Convert.FromBase64String(Protector().Unprotect(state.ProtectedKey));

    /// <summary>
    /// Decrypts the stored chain key. False means the key ring cannot read it — never remint.
    /// </summary>
    private bool TryUnprotectKey(AuditChainState state, out byte[] key, out string? failureReason)
    {
        try
        {
            key = UnprotectKey(state);
            failureReason = null;
            return true;
        }
        catch (CryptographicException)
        {
            key = [];
            failureReason = KeyUnreadableReason;
            return false;
        }
        catch (FormatException)
        {
            key = [];
            failureReason = KeyUnreadableReason;
            return false;
        }
    }

    private IDataProtector Protector() => dataProtection.CreateProtector(ProtectorPurpose);

    private static bool CryptographicEquals(string computed, string? stored) =>
        stored is not null &&
        CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(computed), SafeDecode(stored));

    private static byte[] SafeDecode(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { return []; }
    }
}
