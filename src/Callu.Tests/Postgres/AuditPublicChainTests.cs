using System.Text.Json.Nodes;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Audit;
using Callu.Tests.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Callu.Infrastructure.Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Quartz;

namespace Callu.Tests;

/// <summary>The public OpenAuditModel chain the sealer extends after its keyed one.</summary>
// Backfill, the stop before a tampered row and two sealers racing all depend on Postgres itself:
// its transactions, its advisory lock and what each statement sees.
[Collection(PostgresCollection.Name)]
public class AuditPublicChainTests(PostgresFixture fixture)
{
    private static readonly DateTime Base = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    // AuditChainTests' row, plus the envelope fields AuditLogService always writes: the public event
    // is validated against the schema, and a zero enum is no actor type or outcome it knows.
    private static AuditLog Row(int minute, AuditAction action = AuditAction.Login) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Base.AddMinutes(minute),
        UpdatedAt = Base.AddMinutes(minute),
        ActorId = $"user-{minute}",
        ActorDisplayName = "Ada",
        ActorType = AuditActorType.User,
        Action = action,
        EventName = AuditEventNaming.ToEventName("Incident", action),
        EventCategory = AuditEventNaming.ToEventCategory("Incident"),
        Outcome = AuditEventNaming.ToOutcome(action),
        ResourceType = "Incident",
        ResourceId = Guid.NewGuid(),
        Summary = $"entry {minute}",
    };

    private static AuditChainService Service(ApplicationDbContext ctx) =>
        new(ctx,
            DataProtectionProvider.Create(nameof(AuditPublicChainTests)),
            NullLogger<AuditChainService>.Instance);

    private async Task<(AuditChainService Chain, ApplicationDbContext Ctx)> SeededAsync(int rows)
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, rows).Select(i => Row(i)));
        await ctx.SaveChangesAsync();

        return (Service(ctx), ctx);
    }

    /// <summary>What a 1.2.0 database looks like after the migration: chained privately, no public event.</summary>
    private static async Task ResetPublicChainAsync(ApplicationDbContext ctx)
    {
        await ctx.AuditLogs.ExecuteUpdateAsync(s => s.SetProperty(l => l.OamEvent, (string?)null).SetProperty(l => l.OamHash, (string?)null));
        await ctx.AuditChainLeaves.ExecuteDeleteAsync();
        await ctx.AuditChainStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.OamLastSequence, 0L).SetProperty(x => x.OamLastHash, (string?)null));
    }

    private static Task<List<long?>> PublicSequencesAsync(ApplicationDbContext ctx) =>
        ctx.AuditLogs.AsNoTracking().Where(l => l.OamHash != null).Select(l => l.Sequence).OrderBy(s => s).ToListAsync();

    [PostgresFact]
    public async Task EverySealedRow_JoinsThePublicChain_InSequenceOrder()
    {
        var (chain, ctx) = await SeededAsync(5);

        var result = await chain.SealAsync();

        Assert.Equal(5, result.PublicSealedCount);
        Assert.Equal(5, result.PublicHead);

        var rows = await ctx.AuditLogs.OrderBy(l => l.Sequence).ToListAsync();
        var events = rows.Select(r => JsonNode.Parse(r.OamEvent!)!.AsObject()).ToList();

        Assert.All(rows, r => Assert.NotNull(r.OamHash));
        Assert.Null(events[0]["integrity"]!["previousHash"]);
        for (var i = 1; i < events.Count; i++)
            Assert.Equal(rows[i - 1].OamHash, events[i]["integrity"]!["previousHash"]!.GetValue<string>());
        Assert.All(events, e => OamFixtures.AssertValidEvent(e));
        Assert.All(rows, r => Assert.Equal(r.OamHash, OpenAuditDigest.Compute(JsonNode.Parse(r.OamEvent!)!.AsObject())));
        Assert.Equal(5, await ctx.AuditChainLeaves.CountAsync());
    }

    [PostgresFact]
    public async Task RowsSealedBy120_AreBackfilled_WithoutTouchingTheirStoredValues()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();

        // What a 1.2.0 database looks like after the migration: chained privately, no public event.
        await ctx.AuditLogs.ExecuteUpdateAsync(s => s.SetProperty(l => l.OamEvent, (string?)null).SetProperty(l => l.OamHash, (string?)null));
        await ctx.AuditChainLeaves.ExecuteDeleteAsync();
        await ctx.AuditChainStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.OamLastSequence, 0L).SetProperty(x => x.OamLastHash, (string?)null));
        var before = await ctx.AuditLogs.AsNoTracking().OrderBy(l => l.Sequence).Select(l => new { l.RowHash, l.PrevHash, l.ChangeAfter }).ToListAsync();
        ctx.ChangeTracker.Clear();

        var result = await chain.SealAsync();

        Assert.Equal(3, result.PublicSealedCount);
        var after = await ctx.AuditLogs.AsNoTracking().OrderBy(l => l.Sequence).ToListAsync();
        Assert.All(after, r => Assert.NotNull(r.OamHash));
        Assert.Equal(before.Select(b => b.RowHash), after.Select(a => a.RowHash));
        Assert.True((await chain.VerifyAsync()).Intact);
    }

    /// <summary>Rows a 1.2.0 Worker sealed, under v3 or under the form before the version was recorded, join the public chain.</summary>
    // The backfill re-checks every row's keyed hash under the form it was sealed with; recomputing it under
    // the current form instead would stop the public chain at the first row an upgrade brought with it.
    [PostgresFact]
    public async Task RowsSealedUnderV3AndTheLegacyForm_AllJoinThePublicChain()
    {
        var (chain, ctx) = await SeededAsync(6);
        await chain.SealAsync();

        var state = await ctx.AuditChainStates.SingleAsync();
        var key = Convert.FromBase64String(
            DataProtectionProvider.Create(nameof(AuditPublicChainTests)).CreateProtector("Callu.AuditChain.v1")
                .Unprotect(state.ProtectedKey));

        // Re-sealed by hand the way 1.2.0 and the build before it did: no Details, the form they knew.
        string? previous = null;
        foreach (var row in await ctx.AuditLogs.OrderBy(l => l.Sequence).ToListAsync())
        {
            var legacy = row.Sequence!.Value % 2 == 1;
            row.TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
            row.SpanId = "00f067aa0ba902b7";
            row.RequestId = $"0HN7C1QK2R3S4:{row.Sequence:D8}";
            row.CanonicalizationVersion = legacy ? null : CalluAuditChain.CorrelatedCanonicalizationId;
            row.PrevHash = previous;
            row.RowHash = AuditChainHash.Compute(row, row.Sequence.Value, previous, key,
                legacy ? CalluAuditChain.LegacyCanonicalizationId : CalluAuditChain.CorrelatedCanonicalizationId);
            previous = row.RowHash;
        }
        state.LastHash = previous;
        await ctx.SaveChangesAsync();
        await ResetPublicChainAsync(ctx);
        ctx.ChangeTracker.Clear();

        var result = await chain.SealAsync();

        Assert.Null(result.FailureReason);
        Assert.Equal(6, result.PublicSealedCount);
        Assert.Equal(6, result.PublicHead);
        Assert.Equal([1L, 2, 3, 4, 5, 6], await PublicSequencesAsync(ctx));
        Assert.True((await chain.VerifyAsync()).Intact);
    }

    /// <summary>A capped run seals that many batches into both chains and leaves the rest for the next run.</summary>
    [PostgresFact]
    public async Task ACappedSeal_StopsAfterThatManyBatches_AndTheNextRunCarriesOn()
    {
        var (chain, ctx) = await SeededAsync(AuditChainService.SealBatchSize * 2 + 10);

        var capped = await chain.SealAsync(maxBatches: 2);

        Assert.Null(capped.FailureReason);
        Assert.Equal(AuditChainService.SealBatchSize * 2, capped.SealedCount);
        Assert.Equal(AuditChainService.SealBatchSize * 2, capped.PublicHead);
        Assert.Equal(10, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));

        var rest = await chain.SealAsync();

        Assert.Equal(10, rest.SealedCount);
        Assert.Equal(AuditChainService.SealBatchSize * 2 + 10, rest.PublicHead);
    }

    /// <summary>A row the event builder cannot read stops the public chain before it; it does not roll back the keyed chain.</summary>
    [PostgresFact]
    public async Task ARowWhoseDetailsAreNotJson_StopsThePublicChain_AndTheKeyedPassStillCommits()
    {
        var (chain, ctx) = await SeededAsync(2);
        var broken = Row(3);
        broken.Details = "{not json";
        ctx.AddRange(broken, Row(4));
        await ctx.SaveChangesAsync();

        var result = await chain.SealAsync();

        Assert.NotNull(result.FailureReason);
        Assert.Contains("Entry 3", result.FailureReason);
        Assert.Equal(4, result.SealedCount);
        Assert.Equal(4, result.LastSequence);
        Assert.Equal(2, result.PublicHead);
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));
        Assert.Equal([1L, 2], await PublicSequencesAsync(ctx));
    }

    [PostgresFact]
    public async Task ATamperedRow_StopsThePublicChainBeforeIt()
    {
        var (chain, ctx) = await SeededAsync(4);
        await chain.SealAsync();
        await ctx.AuditLogs.ExecuteUpdateAsync(s => s.SetProperty(l => l.OamEvent, (string?)null).SetProperty(l => l.OamHash, (string?)null));
        await ctx.AuditChainLeaves.ExecuteDeleteAsync();
        await ctx.AuditChainStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.OamLastSequence, 0L).SetProperty(x => x.OamLastHash, (string?)null));
        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "rewritten"));
        ctx.ChangeTracker.Clear();

        var result = await chain.SealAsync();

        Assert.NotNull(result.FailureReason);
        Assert.Equal(2, result.PublicHead);
        var sealedSequences = await ctx.AuditLogs.Where(l => l.OamHash != null).Select(l => l.Sequence).OrderBy(s => s).ToListAsync();
        Assert.Equal([1L, 2], sealedSequences);
    }

    [PostgresFact]
    public async Task TwoSealersAtOnce_ProduceOneUnbrokenChain()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using (var seed = PostgresFixture.Context(cs))
        {
            seed.AddRange(Enumerable.Range(1, 1200).Select(i => Row(i)));
            await seed.SaveChangesAsync();
        }

        var provider = DataProtectionProvider.Create(nameof(AuditPublicChainTests));
        await using var a = PostgresFixture.Context(cs);
        await using var b = PostgresFixture.Context(cs);
        await Task.WhenAll(
            new AuditChainService(a, provider, NullLogger<AuditChainService>.Instance).SealAsync(),
            new AuditChainService(b, provider, NullLogger<AuditChainService>.Instance).SealAsync());

        await using var check = PostgresFixture.Context(cs);
        var rows = await check.AuditLogs.AsNoTracking().OrderBy(l => l.Sequence).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 1200).Select(i => (long?)i), rows.Select(r => r.Sequence));
        Assert.All(rows, r => Assert.NotNull(r.OamHash));
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(rows[i - 1].OamHash, JsonNode.Parse(rows[i].OamEvent!)!["integrity"]!["previousHash"]!.GetValue<string>());
        Assert.Single(rows.Select(r => JsonNode.Parse(r.OamEvent!)!["integrity"]!["chainId"]!.GetValue<string>()).Distinct());
    }

    // Deleting the row the chain stopped at must not let the next run link 2 straight to 4.
    [PostgresFact]
    public async Task ADeletedTamperedRow_DoesNotLetThePublicChainBridgeTheGap()
    {
        var (chain, ctx) = await SeededAsync(4);
        await chain.SealAsync();
        await ResetPublicChainAsync(ctx);
        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "rewritten"));

        var first = await chain.SealAsync();
        Assert.Equal(2, first.PublicHead);

        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteDeleteAsync();

        var second = await chain.SealAsync();

        Assert.NotNull(second.FailureReason);
        Assert.Contains("expected entry 3", second.FailureReason);
        Assert.Contains("found 4", second.FailureReason);
        Assert.Equal(2, second.PublicHead);
        Assert.Equal([1L, 2], await PublicSequencesAsync(ctx));
        Assert.False(await ctx.AuditChainLeaves.AnyAsync(l => l.Sequence == 4));
    }

    // A row already in the public chain whose public columns were cleared would otherwise be sealed
    // again after the head and collide with its own leaf, rolling the whole batch back on every run.
    [PostgresFact]
    public async Task AClearedPublicRow_StopsThePublicPass_AndThePrivateOneStillSeals()
    {
        var (chain, ctx) = await SeededAsync(3);
        await chain.SealAsync();
        await ctx.AuditLogs.Where(l => l.Sequence == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.OamEvent, (string?)null).SetProperty(l => l.OamHash, (string?)null));
        ctx.AddRange(Row(10), Row(11));
        await ctx.SaveChangesAsync();

        var result = await chain.SealAsync();

        Assert.NotNull(result.FailureReason);
        Assert.Contains("expected entry 4", result.FailureReason);
        Assert.Contains("found 2", result.FailureReason);
        Assert.Equal(2, result.SealedCount);
        Assert.Equal(5, result.LastSequence);
        Assert.Equal(3, result.PublicHead);
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));
        Assert.Equal(3, await ctx.AuditChainLeaves.CountAsync());
    }

    // The design stops the public chain, not the sealer: the keyed chain keeps up with every batch.
    [PostgresFact]
    public async Task AfterAPublicStop_ThePrivatePassStillSealsEveryBatch()
    {
        var (chain, ctx) = await SeededAsync(4);
        await chain.SealAsync();
        await ResetPublicChainAsync(ctx);
        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "rewritten"));
        ctx.AddRange(Enumerable.Range(100, AuditChainService.SealBatchSize + 10).Select(i => Row(i)));
        await ctx.SaveChangesAsync();

        var result = await chain.SealAsync();

        Assert.NotNull(result.FailureReason);
        Assert.Equal(AuditChainService.SealBatchSize + 10, result.SealedCount);
        Assert.Equal(4 + AuditChainService.SealBatchSize + 10, result.LastSequence);
        Assert.Equal(2, result.PublicHead);
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(l => l.Sequence == null));
        Assert.Equal(3, (await chain.VerifyAsync()).FirstBrokenSequence);
    }

    // The keyed check has to read what the database holds, not what this context loaded earlier.
    [PostgresFact]
    public async Task ATamperedRow_IsCaught_EvenWhenTheContextStillTracksItsOldValues()
    {
        var (chain, ctx) = await SeededAsync(4);
        await chain.SealAsync();
        _ = await ctx.AuditLogs.ToListAsync();
        await ResetPublicChainAsync(ctx);
        await ctx.AuditLogs.Where(l => l.Sequence == 3).ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "rewritten"));

        var result = await chain.SealAsync();

        Assert.NotNull(result.FailureReason);
        Assert.Equal(2, result.PublicHead);
        Assert.Equal([1L, 2], await PublicSequencesAsync(ctx));
    }

    [PostgresFact]
    public async Task NotePruned_WaitsForTheSealLock()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, 3).Select(i => Row(i)));
        await ctx.SaveChangesAsync();
        var chain = Service(ctx);
        await chain.SealAsync();
        await ctx.AuditLogs.Where(l => l.Sequence == 1).ExecuteDeleteAsync();

        await using var gate = new NpgsqlConnection(cs);
        await gate.OpenAsync();
        await using var holding = await gate.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand(
                         $"SELECT pg_advisory_xact_lock({AuditChainService.SealLockKey})", gate, holding))
            await take.ExecuteNonQueryAsync();

        var note = Task.Run(() => chain.NotePrunedAsync());

        var finished = await Task.WhenAny(note, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.False(finished == note, "NotePrunedAsync updated the chain state without the seal lock.");

        await holding.CommitAsync();
        await note;

        await using var check = PostgresFixture.Context(cs);
        Assert.Equal(1, (await check.AuditChainStates.SingleAsync()).PrunedThroughSequence);
    }

    [PostgresFact]
    public async Task TheSealJob_ReportsBothChains_EvenAfterAPublicStop()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        await using (var seed = PostgresFixture.Context(cs))
        {
            seed.AddRange(Enumerable.Range(1, 4).Select(i => Row(i)));
            await seed.SaveChangesAsync();
            await Service(seed).SealAsync();
            await ResetPublicChainAsync(seed);
            await seed.AuditLogs.Where(l => l.Sequence == 3).ExecuteUpdateAsync(s => s.SetProperty(l => l.Summary, "rewritten"));
            seed.AddRange(Row(10), Row(11));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => PostgresFixture.Context(cs));
        services.AddSingleton<IDataProtectionProvider>(DataProtectionProvider.Create(nameof(AuditPublicChainTests)));
        services.AddLogging();
        services.AddScoped<AuditChainService>();
        await using var provider = services.BuildServiceProvider();

        var logger = new RecordingLogger<AuditChainSealQuartzJob>();
        var context = Substitute.For<IJobExecutionContext>();
        context.JobDetail.Key.Returns(new JobKey("audit-chain-seal"));
        context.CancellationToken.Returns(CancellationToken.None);

        await new AuditChainSealQuartzJob(provider.GetRequiredService<IServiceScopeFactory>(), logger).Execute(context);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("sealed 2 entry(ies), now through sequence 6"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("public chain extended over 2 entry(ies), now through sequence 2"));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
