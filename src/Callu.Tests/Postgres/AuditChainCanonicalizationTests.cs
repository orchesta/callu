using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Callu.Tests;

/// <summary>Growing the sealed field set without invalidating what is already sealed.</summary>
// The canonical form is written into the hash, so adding a field to it changes every hash there is.
// The version each row was sealed under is therefore stored with the row: a chain sealed by an older
// build has to keep verifying under a newer one, or the trail is lost on upgrade rather than tampered
// with. The other half is that the fields being added are now covered at all.
[Collection(PostgresCollection.Name)]
public class AuditChainCanonicalizationTests(PostgresFixture fixture)
{
    private static readonly DateTime Base = new(2026, 8, 7, 6, 0, 0, DateTimeKind.Utc);

    private static AuditLog Row(int minute) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Base.AddMinutes(minute),
        UpdatedAt = Base.AddMinutes(minute),
        ActorId = $"operator-{minute}",
        ActorDisplayName = "On-call Responder",
        Action = AuditAction.Login,
        ResourceType = "Incident",
        ResourceId = Guid.NewGuid(),
        Summary = $"entry {minute}",
        TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
        SpanId = "00f067aa0ba902b7",
        CorrelationId = "escalation:step-2",
        RequestId = "0HN7C1QK2R3S4:00000003",
    };

    private static AuditChainService Service(ApplicationDbContext ctx, string ring = "canon") =>
        new(ctx, DataProtectionProvider.Create(ring), NullLogger<AuditChainService>.Instance);

    /// <summary>A row sealed by a build whose canonical form lacked these fields still verifies.</summary>
    [PostgresFact]
    public async Task ARowSealedUnderTheOlderFormStillVerifies()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var ctx = PostgresFixture.Context(cs);
        var row = Row(1);
        ctx.Add(row);
        await ctx.SaveChangesAsync();

        var chain = Service(ctx);
        await chain.SealAsync();

        // Re-sealed by hand the way the previous build did it: the older field set, and no version
        // recorded, which is exactly what its rows look like after an upgrade.
        var state = await ctx.AuditChainStates.SingleAsync();
        var key = Convert.FromBase64String(
            DataProtectionProvider.Create("canon").CreateProtector("Callu.AuditChain.v1")
                .Unprotect(state.ProtectedKey));

        row.CanonicalizationVersion = null;
        row.RowHash = AuditChainHash.Compute(
            row, row.Sequence!.Value, row.PrevHash, key, CalluAuditChain.LegacyCanonicalizationId);
        state.LastHash = row.RowHash;
        await ctx.SaveChangesAsync();

        var verdict = await Service(ctx).VerifyAsync();

        Assert.True(verdict.Intact, verdict.Reason);
        Assert.Equal(1, verdict.CheckedCount);
    }

    /// <summary>The trace an event ran under is now part of what the chain covers.</summary>
    // It is as re-writable as the address the request came from, which was always sealed. Left out,
    // the record of which operation an audited action belonged to could be rewritten undetected.
    [PostgresFact]
    public async Task RewritingTheTraceOfASealedRow_BreaksTheChain()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var ctx = PostgresFixture.Context(cs);
        ctx.Add(Row(1));
        await ctx.SaveChangesAsync();

        await Service(ctx).SealAsync();

        await ctx.AuditLogs.ExecuteUpdateAsync(s =>
            s.SetProperty(l => l.TraceId, "1111111111111111111111111111abcd"));

        var verdict = await Service(ctx).VerifyAsync();

        Assert.False(verdict.Intact);
        Assert.Equal(1, verdict.FirstBrokenSequence);
    }

    /// <summary>Sealing records which form was used, so a later build knows how to recompute it.</summary>
    [PostgresFact]
    public async Task SealingRecordsTheFormItUsed()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var ctx = PostgresFixture.Context(cs);
        ctx.Add(Row(1));
        await ctx.SaveChangesAsync();

        await Service(ctx).SealAsync();

        var sealedRow = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(CalluAuditChain.CanonicalizationId, sealedRow.CanonicalizationVersion);
    }

    /// <summary>Two sealers handing out the same number is a chain that can never verify again.</summary>
    // DisallowConcurrentExecution binds one scheduler; with the in-memory store a second Worker runs
    // every job of its own. The database is what makes the collision a failed write instead.
    [PostgresFact]
    public async Task TwoRowsCannotShareASequence()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var ctx = PostgresFixture.Context(cs);
        var first = Row(1);
        var second = Row(2);
        first.Sequence = 1;
        second.Sequence = 1;
        ctx.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    /// <summary>Unsealed rows all carry no sequence at once, so the index has to ignore them.</summary>
    [PostgresFact]
    public async Task ManyUnsealedRowsAreNotACollision()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        await using var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, 5).Select(Row));

        await ctx.SaveChangesAsync();

        Assert.Equal(5, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));
    }
}
