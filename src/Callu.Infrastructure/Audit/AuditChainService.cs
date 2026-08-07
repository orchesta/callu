using System.Security.Cryptography;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Audit;

public sealed record AuditChainSealResult(int SealedCount, long LastSequence, string? FailureReason = null);

public sealed record AuditChainVerdict(
    bool Intact,
    long CheckedCount,
    long? FirstBrokenSequence,
    string? Reason);

/// <summary>Seals audit entries into a hash chain and verifies that chain.</summary>
public sealed class AuditChainService(
    ApplicationDbContext db,
    IDataProtectionProvider dataProtection,
    ILogger<AuditChainService> logger)
{
    internal const int SealBatchSize = 500;
    internal const int VerifyBatchSize = 1_000;
    private const string ProtectorPurpose = "Callu.AuditChain.v1";

    internal const string KeyUnreadableReason =
        "The audit chain key cannot be decrypted with the current Data Protection key ring. "
        + "Restore the original key ring (callu:dp-keys with the database); Callu will not mint a replacement.";

    /// <summary>Chains every entry written since the last run, oldest first, in bounded batches.</summary>
    public async Task<AuditChainSealResult> SealAsync(CancellationToken ct = default)
    {
        var state = await LoadOrCreateStateAsync(ct);
        if (!TryUnprotectKey(state, out var key, out var failureReason))
        {
            // Detection only: do not remint ProtectedKey. New rows stay unsealed until the ring is restored.
            logger.LogError("Audit chain seal skipped: {Reason}", failureReason);
            return new AuditChainSealResult(0, state.LastSequence, failureReason);
        }

        var total = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.AuditLogs
                .Where(l => l.Sequence == null)
                .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
                .Take(SealBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var entry in batch)
            {
                var sequence = state.LastSequence + 1;
                var hash = AuditChainHash.Compute(
                    entry, sequence, state.LastHash, key, CalluAuditChain.CanonicalizationId);

                entry.Sequence = sequence;
                entry.PrevHash = state.LastHash;
                entry.RowHash = hash;
                entry.CanonicalizationVersion = CalluAuditChain.CanonicalizationId;

                state.LastSequence = sequence;
                state.LastHash = hash;
            }

            await db.SaveChangesAsync(ct);
            total += batch.Count;

            if (batch.Count < SealBatchSize) break;
        }

        return new AuditChainSealResult(total, state.LastSequence);
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
        var state = await LoadStateAsync(ct);

        if (state is null) return;

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
            }
            return;
        }

        var prunedThrough = oldest.Sequence!.Value - 1;
        if (prunedThrough <= state.PrunedThroughSequence) return;

        state.PrunedThroughSequence = prunedThrough;
        state.PrunedThroughHash = oldest.PrevHash;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Audit chain: retention removed entries through sequence {Sequence}", prunedThrough);
    }

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
