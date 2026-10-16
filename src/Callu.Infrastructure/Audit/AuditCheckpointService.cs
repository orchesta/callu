using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Audit;

public enum AuditCheckpointPublishStatus { Disabled, NothingNew, Published, Failed, IntegrityFault }

public sealed record AuditCheckpointPublishOutcome(AuditCheckpointPublishStatus Status, AuditCheckpoint? Checkpoint = null);

public sealed record AuditCheckpointSummary(
    Guid Id, long HeadSequence, long EventCount, string AnchorType, string AnchorReference, DateTime? IssuedAt);

public enum AuditProofStatus { Found, NotSealed, NoCheckpoint, IntegrityFault }

/// <summary>The stored leaves contradict the chain they were stored for; nothing is signed or proven over them.</summary>
public sealed class AuditChainIntegrityException(string message) : Exception(message);

public sealed record AuditProofResult(AuditProofStatus Status, string? Document = null);

/// <summary>Issues OpenAuditModel checkpoints over the public chain and cuts inclusion proofs against them.</summary>
public sealed class AuditCheckpointService(
    ApplicationDbContext db,
    IAuditSigningKeyService signingKeys,
    IHttpClientFactory httpClients,
    IOptions<AuditCheckpointOptions> options,
    ILogger<AuditCheckpointService> logger)
{
    public const string HttpClientName = "audit-checkpoint";
    private const string PublicationAnchor = "publication";
    private const int MaxReferenceLength = 2048;
    private const int MaxErrorLength = 500;

    /// <summary>A checkpoint an administrator anchors by hand; null when nothing is sealed yet.</summary>
    /// <exception cref="AuditChainIntegrityException">The stored leaves do not form the chain's run up to its head.</exception>
    public async Task<AuditCheckpoint?> IssueManualAsync(string reference, CancellationToken ct)
    {
        var text = reference?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReferenceLength)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["reference"] = [$"Say where the checkpoint is recorded, in at most {MaxReferenceLength} characters."],
            });

        // Signed into the checkpoint as it is, so it has to be a reference the schema accepts: one line,
        // and no control character a reader could not see.
        if (!OpenAuditEventBuilder.IsIdentifier(text) || text.Any(char.IsControl))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["reference"] = ["Give the reference on one line, without control characters."],
            });

        var checkpoint = await CreateAsync(OpenAuditDocuments.Anchor("manual", text), ct);
        if (checkpoint is null) return null;

        // Saved once and already issued: a manual row never exists as pending, so publication cannot pick it up.
        checkpoint.Status = AuditCheckpointStatus.Issued;
        checkpoint.IssuedAt = DateTime.UtcNow;
        db.AuditCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(ct);
        return checkpoint;
    }

    /// <summary>Publishes the pending checkpoint, or a new one when the head moved since the last publication.</summary>
    public async Task<AuditCheckpointPublishOutcome> PublishDueAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled) return new(AuditCheckpointPublishStatus.Disabled);

        var reference = PublicReference(settings.PublishUrl!);

        // A pending row is a signed statement of where it was placed. Only one signed for the URL in force
        // may be resumed; after the URL is corrected, the old row would claim a place it was never put.
        var pending = await db.AuditCheckpoints
            .Where(c => c.Status == AuditCheckpointStatus.Pending)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        AuditCheckpoint? checkpoint = null;
        foreach (var row in pending)
        {
            if (checkpoint is null && row.AnchorType == PublicationAnchor && row.AnchorReference == reference)
                checkpoint = row;
            else
                row.IsDeleted = true;
        }

        if (pending.Any(r => r.IsDeleted)) await db.SaveChangesAsync(ct);

        if (checkpoint is null)
        {
            var lastPublished = await db.AuditCheckpoints
                .Where(c => c.Status == AuditCheckpointStatus.Issued && c.AnchorType == PublicationAnchor)
                .MaxAsync(c => (long?)c.HeadSequence, ct) ?? 0;
            var head = await db.AuditChainStates.AsNoTracking()
                .Where(s => s.Id == AuditChainState.SingletonId)
                .Select(s => (long?)s.OamLastSequence)
                .FirstOrDefaultAsync(ct) ?? 0;

            if (head <= lastPublished) return new(AuditCheckpointPublishStatus.NothingNew);

            try
            {
                checkpoint = await CreateAsync(OpenAuditDocuments.Anchor(PublicationAnchor, reference), ct);
            }
            catch (AuditChainIntegrityException)
            {
                return new(AuditCheckpointPublishStatus.IntegrityFault);
            }
            if (checkpoint is null) return new(AuditCheckpointPublishStatus.NothingNew);

            // Saved pending before the POST, so a failure is retried with this same document.
            db.AuditCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            var client = httpClients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.PublishUrl)
            {
                Content = new StringContent(
                    $$"""{"checkpoint":{{checkpoint.Document}},"root":{{checkpoint.RootDocument}}}""",
                    Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(settings.PublishToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.PublishToken);

            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            checkpoint.Status = AuditCheckpointStatus.Issued;
            checkpoint.IssuedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return new(AuditCheckpointPublishStatus.Published, checkpoint);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Never the exception's message: a transport error can quote the URL, with its credentials and query.
            checkpoint.PublishAttempts++;
            checkpoint.LastError = Describe(ex);
            await db.SaveChangesAsync(ct);
            if (ex is HttpRequestException or TaskCanceledException)
                logger.LogWarning("Audit checkpoint {CheckpointId} was not accepted by its publication URL ({Error}); it stays pending",
                    checkpoint.Id, checkpoint.LastError);
            else
                logger.LogError(ex, "Audit checkpoint {CheckpointId} could not be published; it stays pending", checkpoint.Id);
            return new(AuditCheckpointPublishStatus.Failed, checkpoint);
        }
    }

    private static string Describe(Exception ex)
    {
        var text = ex switch
        {
            HttpRequestException { StatusCode: { } status } => $"The URL answered {(int)status} {status}.",
            HttpRequestException => "The URL could not be reached.",
            TaskCanceledException => "The URL did not answer in time.",
            _ => $"Publication failed ({ex.GetType().Name}).",
        };
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }

    public async Task<IReadOnlyList<AuditCheckpointSummary>> ListIssuedAsync(CancellationToken ct) =>
        await db.AuditCheckpoints.AsNoTracking()
            .Where(c => c.Status == AuditCheckpointStatus.Issued)
            .OrderByDescending(c => c.HeadSequence).ThenByDescending(c => c.CreatedAt)
            .Select(c => new AuditCheckpointSummary(c.Id, c.HeadSequence, c.EventCount, c.AnchorType, c.AnchorReference, c.IssuedAt))
            .ToListAsync(ct);

    public Task<AuditCheckpoint?> FindIssuedAsync(Guid id, CancellationToken ct) =>
        db.AuditCheckpoints.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Status == AuditCheckpointStatus.Issued, ct);

    /// <summary>An inclusion proof for one event against a checkpoint, or the latest one that covers it.</summary>
    public async Task<AuditProofResult> ProofAsync(Guid eventId, Guid? checkpointId, CancellationToken ct)
    {
        var row = await db.AuditLogs.AsNoTracking()
            .Where(l => l.Id == eventId)
            .Select(l => new { l.Sequence, l.OamHash })
            .FirstOrDefaultAsync(ct);
        if (row?.OamHash is null || row.Sequence is null) return new(AuditProofStatus.NotSealed);

        var covering = db.AuditCheckpoints.AsNoTracking()
            .Where(c => c.Status == AuditCheckpointStatus.Issued && c.HeadSequence >= row.Sequence);
        var checkpoint = checkpointId is { } id
            ? await covering.FirstOrDefaultAsync(c => c.Id == id, ct)
            : await covering.OrderByDescending(c => c.HeadSequence).ThenByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (checkpoint is null) return new(AuditProofStatus.NoCheckpoint);

        // The chain may start after a pruned prefix, so the leaf index is a position in the stream, not sequence - 1.
        var tree = new StreamingMerkleTree();
        var leafMatches = false;
        await foreach (var leaf in Leaves(checkpoint.HeadSequence).WithCancellation(ct))
        {
            var isTarget = leaf.Sequence == row.Sequence;
            if (isTarget) leafMatches = string.Equals(leaf.Hash, row.OamHash, StringComparison.OrdinalIgnoreCase);
            tree.Append(Convert.FromHexString(leaf.Hash), isTarget);
        }

        // A sealed event always has its leaf, and the leaves under an issued checkpoint always give its root.
        // Anything else is the stored chain contradicting itself, which a proof must not paper over.
        if (!leafMatches || Convert.ToHexStringLower(tree.Root()) != checkpoint.RootHash)
        {
            logger.LogError(
                "Audit proof refused for entry {Sequence}: its stored leaf is missing or differs, or the leaves no longer give checkpoint {CheckpointId}'s root",
                row.Sequence, checkpoint.Id);
            return new(AuditProofStatus.IntegrityFault);
        }

        var proof = OpenAuditDocuments.Proof(
            row.OamHash, tree.TargetIndex!.Value, eventId,
            tree.AuditPath(),
            JsonNode.Parse(checkpoint.RootDocument)!.AsObject());

        return new(AuditProofStatus.Found, OpenAuditDocuments.Canonical(proof));
    }

    /// <summary>The stored leaves through a head, oldest first, read one at a time rather than held as a list.</summary>
    private IAsyncEnumerable<AuditChainLeaf> Leaves(long throughSequence) =>
        db.AuditChainLeaves.AsNoTracking()
            .Where(l => l.Sequence <= throughSequence)
            .OrderBy(l => l.Sequence)
            .AsAsyncEnumerable();

    // Returned unsaved: each caller decides the status it is first stored with.
    private async Task<AuditCheckpoint?> CreateAsync(JsonObject anchor, CancellationToken ct)
    {
        var state = await db.AuditChainStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AuditChainState.SingletonId, ct);
        if (state?.ChainId is null || state.OamLastSequence == 0 || state.OamLastHash is null) return null;

        // Every leaf from the chain's first event: leafCount then equals eventCount, the cross-check
        // the proof format was designed around.
        var tree = new StreamingMerkleTree();
        long? firstSequence = null;
        string? lastHash = null;
        await foreach (var leaf in Leaves(state.OamLastSequence).WithCancellation(ct))
        {
            firstSequence ??= leaf.Sequence;
            lastHash = leaf.Hash;
            tree.Append(Convert.FromHexString(leaf.Hash));
        }

        // A tree that does not end at the recorded head, or has a gap, would be signed as that head's root.
        if (tree.Count == 0
            || !string.Equals(lastHash, state.OamLastHash, StringComparison.OrdinalIgnoreCase)
            || tree.Count != state.OamLastSequence - firstSequence + 1)
        {
            logger.LogError(
                "Audit checkpoint refused: the {Count} stored leaves do not form an unbroken run ending at head {Head}",
                tree.Count, state.OamLastSequence);
            throw new AuditChainIntegrityException(
                $"The stored leaves of the public chain do not form an unbroken run ending at its head ({state.OamLastSequence}).");
        }

        var signer = await signingKeys.GetActiveSignerAsync(ct);
        var now = DateTime.UtcNow;
        var rootHash = Convert.ToHexStringLower(tree.Root());

        var checkpoint = new AuditCheckpoint
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            ChainId = state.ChainId,
            HeadSequence = state.OamLastSequence,
            HeadHash = state.OamLastHash,
            EventCount = tree.Count,
            RootHash = rootHash,
            AnchorType = anchor["type"]!.GetValue<string>(),
            AnchorReference = anchor["reference"]!.GetValue<string>(),
            Document = OpenAuditDocuments.Canonical(OpenAuditDocuments.Checkpoint(
                state.ChainId, state.OamLastSequence, state.OamLastHash, tree.Count, now, anchor, signer)),
            RootDocument = OpenAuditDocuments.Canonical(OpenAuditDocuments.Root(rootHash, tree.Count, anchor, signer)),
            Status = AuditCheckpointStatus.Pending,
        };

        return checkpoint;
    }

    /// <summary>The URL as an anchor may name it: no credentials, no query, no fragment.</summary>
    internal static string PublicReference(string url)
    {
        var builder = new UriBuilder(url) { UserName = string.Empty, Password = string.Empty, Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.GetLeftPart(UriPartial.Path);
    }
}
