using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Quartz;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Parameters;
using Quartz;
using System.Text.Json.Nodes;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Exceptions;
using Callu.Tests.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Checkpoints over the public chain: manual and published, and what each one claims.</summary>
// Publication ordering (issued only after the URL accepts) and the pending/retry state live in Postgres.
[Collection(PostgresCollection.Name)]
public class AuditCheckpointTests(PostgresFixture fixture)
{
    private static readonly DateTime Base = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

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

    private async Task<(ApplicationDbContext Ctx, AuditChainService Chain)> SealedAsync(int rows) =>
        (await SealedWithConnectionAsync(rows)) is var (ctx, chain, _) ? (ctx, chain) : default;

    private async Task<(ApplicationDbContext Ctx, AuditChainService Chain, string ConnectionString)> SealedWithConnectionAsync(int rows)
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        var ctx = PostgresFixture.Context(cs);
        ctx.AddRange(Enumerable.Range(1, rows).Select(i => Row(i)));
        await ctx.SaveChangesAsync();

        var chain = new AuditChainService(
            ctx, DataProtectionProvider.Create(nameof(AuditCheckpointTests)), NullLogger<AuditChainService>.Instance);
        await chain.SealAsync();
        return (ctx, chain, cs);
    }

    private static AuditCheckpointService Checkpoints(
        ApplicationDbContext ctx, HttpMessageHandler? handler = null, string? publishUrl = null)
    {
        var signing = Substitute.For<IAuditSigningKeyService>();
        signing.GetActiveSignerAsync(Arg.Any<CancellationToken>())
            .Returns(new Ed25519Signer(OamFixtures.TestKeyId, OamFixtures.TestSeed));

        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(AuditCheckpointService.HttpClientName)
            .Returns(_ => new HttpClient(handler ?? new StubHandler(HttpStatusCode.OK)));

        return new AuditCheckpointService(ctx, signing, clients,
            Options.Create(new AuditCheckpointOptions { PublishUrl = publishUrl }),
            NullLogger<AuditCheckpointService>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status);
        }
    }

    [PostgresFact]
    public async Task AManualCheckpoint_RecordsTheHead_AndIsIssuedAtOnce()
    {
        var (ctx, _) = await SealedAsync(5);

        var checkpoint = await Checkpoints(ctx).IssueManualAsync("CHG-2041: head recorded in the change ticket", default);

        var state = await ctx.AuditChainStates.SingleAsync();
        var document = JsonNode.Parse(checkpoint!.Document)!.AsObject();
        Assert.Equal(AuditCheckpointStatus.Issued, checkpoint.Status);
        Assert.Equal(state.OamLastSequence, document["head"]!["sequence"]!.GetValue<long>());
        Assert.Equal(state.OamLastHash, document["head"]!["hash"]!.GetValue<string>());
        Assert.Equal(5, document["eventCount"]!.GetValue<long>());
        Assert.Equal("manual", document["anchor"]!["type"]!.GetValue<string>());
        Assert.NotNull(document["signature"]);
        OamFixtures.AssertValidCheckpoint(document);
        Assert.Equal(5, JsonNode.Parse(checkpoint.RootDocument)!["leafCount"]!.GetValue<long>());
    }

    [PostgresFact]
    public async Task NoCheckpoint_BeforeAnythingIsSealed_AndABlankReferenceIsRefused()
    {
        var cs = await fixture.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);
        var ctx = PostgresFixture.Context(cs);

        Assert.Null(await Checkpoints(ctx).IssueManualAsync("CHG-1", default));
        await Assert.ThrowsAsync<ValidationException>(() => Checkpoints(ctx).IssueManualAsync("  ", default));
    }

    /// <summary>The anchor reference is signed into the checkpoint, so it has to be one the schema accepts.</summary>
    // The schema's pattern is a JavaScript one: its "." stops at a line break or a line/paragraph separator.
    [PostgresFact]
    public async Task AReferenceWithALineBreakOrAControlCharacter_IsRefused_BeforeAnythingIsSigned()
    {
        var (ctx, _) = await SealedAsync(2);

        foreach (var reference in new[] { "CHG-1\nsecond line", "CHG-1\r\nx", "CHG-1\u2028x", "CHG-1\u2029x", "CHG-1\tx", "CHG-1\u0000x", "CHG-1\u0085x" })
            await Assert.ThrowsAsync<ValidationException>(() => Checkpoints(ctx).IssueManualAsync(reference, default));

        Assert.Equal(0, await ctx.AuditCheckpoints.CountAsync());
    }

    [PostgresFact]
    public async Task AnOrdinaryReference_IsIssued_AndTheCheckpointValidates()
    {
        var (ctx, _) = await SealedAsync(2);

        var checkpoint = await Checkpoints(ctx).IssueManualAsync("  CHG-2041 — değişiklik kaydına yazıldı  ", default);

        Assert.Equal("CHG-2041 — değişiklik kaydına yazıldı", checkpoint!.AnchorReference);
        OamFixtures.AssertValidCheckpoint(JsonNode.Parse(checkpoint.Document)!.AsObject());
    }

    [PostgresFact]
    public async Task Publication_IsIssuedOnlyAfterTheUrlAccepted_AndNamesTheUrlWithoutItsSecrets()
    {
        var (ctx, _) = await SealedAsync(3);
        var handler = new StubHandler(HttpStatusCode.OK);

        var outcome = await Checkpoints(ctx, handler, "https://user:pw@siem.example/checkpoints?token=abc")
            .PublishDueAsync(default);

        Assert.Equal(AuditCheckpointPublishStatus.Published, outcome.Status);
        var document = JsonNode.Parse(outcome.Checkpoint!.Document)!;
        Assert.Equal("publication", document["anchor"]!["type"]!.GetValue<string>());
        Assert.Equal("https://siem.example/checkpoints", document["anchor"]!["reference"]!.GetValue<string>());
        var body = JsonNode.Parse(handler.Bodies.Single())!;
        Assert.NotNull(body["checkpoint"]);
        Assert.NotNull(body["root"]);
    }

    [PostgresFact]
    public async Task AFailedPublication_StaysPending_IsNotListed_AndIsRetriedWithTheSameDocument()
    {
        var (ctx, _) = await SealedAsync(3);
        var failing = Checkpoints(ctx, new StubHandler(HttpStatusCode.BadGateway), "https://siem.example/c");

        var first = await failing.PublishDueAsync(default);
        Assert.Equal(AuditCheckpointPublishStatus.Failed, first.Status);
        Assert.Empty(await failing.ListIssuedAsync(default));

        var handler = new StubHandler(HttpStatusCode.OK);
        var second = await Checkpoints(ctx, handler, "https://siem.example/c").PublishDueAsync(default);

        Assert.Equal(AuditCheckpointPublishStatus.Published, second.Status);
        Assert.Equal(first.Checkpoint!.Id, second.Checkpoint!.Id);
        Assert.Equal(1, second.Checkpoint.PublishAttempts);
    }

    [PostgresFact]
    public async Task NothingNew_IsNotPublishedTwice_AndNoUrlMeansNoPublication()
    {
        var (ctx, _) = await SealedAsync(3);
        var service = Checkpoints(ctx, new StubHandler(HttpStatusCode.OK), "https://siem.example/c");
        await service.PublishDueAsync(default);

        Assert.Equal(AuditCheckpointPublishStatus.NothingNew, (await service.PublishDueAsync(default)).Status);
        Assert.Equal(AuditCheckpointPublishStatus.Disabled, (await Checkpoints(ctx).PublishDueAsync(default)).Status);
    }

    [PostgresFact]
    public async Task ACorrectedUrl_RetiresTheCheckpointSignedForTheOldOne_AndIssuesOneNamingTheNew()
    {
        var (ctx, _) = await SealedAsync(3);
        var failing = await Checkpoints(ctx, new StubHandler(HttpStatusCode.BadGateway), "https://siem.example/a")
            .PublishDueAsync(default);

        var outcome = await Checkpoints(ctx, new StubHandler(HttpStatusCode.OK), "https://siem.example/b")
            .PublishDueAsync(default);

        var service = Checkpoints(ctx);
        var issued = await service.ListIssuedAsync(default);
        var single = Assert.Single(issued);
        Assert.Equal(AuditCheckpointPublishStatus.Published, outcome.Status);
        Assert.NotEqual(failing.Checkpoint!.Id, outcome.Checkpoint!.Id);
        Assert.Equal("https://siem.example/b", single.AnchorReference);
        Assert.Equal(outcome.Checkpoint.Id, single.Id);
        Assert.True((await ctx.AuditCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Id == failing.Checkpoint.Id)).IsDeleted);
        Assert.Equal(1, await ctx.AuditCheckpoints.CountAsync(c => c.Status == AuditCheckpointStatus.Issued));
    }

    [PostgresFact]
    public async Task AChainWhoseFirstLeafIsNotOne_CountsLeaves_AndRootsOverExactlyThose()
    {
        var (ctx, _) = await SealedAsync(5);
        await ctx.AuditChainLeaves.Where(l => l.Sequence < 3).ExecuteDeleteAsync();

        var checkpoint = await Checkpoints(ctx).IssueManualAsync("CHG-9", default);

        var digests = (await ctx.AuditChainLeaves.AsNoTracking().OrderBy(l => l.Sequence).Select(l => l.Hash).ToListAsync())
            .Select(Convert.FromHexString).ToList();
        var document = JsonNode.Parse(checkpoint!.Document)!;
        Assert.Equal(3, document["eventCount"]!.GetValue<long>());
        Assert.Equal(5, document["head"]!["sequence"]!.GetValue<long>());
        Assert.Equal(Convert.ToHexStringLower(MerkleTree.Root(digests)), checkpoint.RootHash);
        Assert.Equal(3, JsonNode.Parse(checkpoint.RootDocument)!["leafCount"]!.GetValue<long>());
    }

    /// <summary>A leaf run that does not end at the head, or has a gap, is an integrity fault, not "nothing sealed".</summary>
    [PostgresFact]
    public async Task AGapInTheLeaves_OrAWrongTail_RefusesTheCheckpoint_AsAnIntegrityFault()
    {
        var (ctx, _) = await SealedAsync(5);
        await ctx.AuditChainLeaves.Where(l => l.Sequence == 3).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<AuditChainIntegrityException>(() => Checkpoints(ctx).IssueManualAsync("CHG-1", default));

        var (other, _) = await SealedAsync(3);
        await other.AuditChainStates.ExecuteUpdateAsync(s => s.SetProperty(x => x.OamLastHash, new string('a', 64)));
        await Assert.ThrowsAsync<AuditChainIntegrityException>(() => Checkpoints(other).IssueManualAsync("CHG-2", default));

        Assert.Equal(0, await ctx.AuditCheckpoints.CountAsync());
    }

    [PostgresFact]
    public async Task TheManualEndpoint_AnswersAnIntegrityFault_WithA500ThatSaysSo()
    {
        var (ctx, _) = await SealedAsync(4);
        await ctx.AuditChainLeaves.Where(l => l.Sequence == 2).ExecuteDeleteAsync();

        var result = await Controller(ctx).CreateCheckpoint(new CreateCheckpointRequest("CHG-1"), default);

        var answer = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, answer.StatusCode);
        Assert.Contains("integrity", JsonSerializer.Serialize(answer.Value), StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Publication_OverAnInconsistentLeafRun_IsAnIntegrityFault_AndTheJobRecordsIt()
    {
        var (ctx, _, cs) = await SealedWithConnectionAsync(4);
        await ctx.AuditChainLeaves.Where(l => l.Sequence == 2).ExecuteDeleteAsync();

        var outcome = await Checkpoints(ctx, publishUrl: "https://siem.example/c").PublishDueAsync(default);
        Assert.Equal(AuditCheckpointPublishStatus.IntegrityFault, outcome.Status);
        Assert.Equal(0, await ctx.AuditCheckpoints.CountAsync());

        var logger = new RecordingLogger<AuditCheckpointQuartzJob>();
        var (job, reader) = await JobAsync(cs, new StubHandler(HttpStatusCode.OK), "https://siem.example/c", logger);
        await job.Execute(JobContext());

        var row = await reader.AuditLogs.AsNoTracking().SingleAsync(l => l.ResourceType == "AuditCheckpoint");
        Assert.Equal("audit-checkpoint.publish-failed", row.EventName);
        Assert.Contains("integrity", row.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [PostgresFact]
    public async Task AProofForASealedEvent_WithNoLeafOrADifferentLeaf_IsAnIntegrityFault()
    {
        var (ctx, chain) = await SealedAsync(5);
        var service = Checkpoints(ctx);
        await service.IssueManualAsync("CHG-5", default);
        var third = await ctx.AuditLogs.AsNoTracking().SingleAsync(l => l.Sequence == 3);
        var fourth = await ctx.AuditLogs.AsNoTracking().SingleAsync(l => l.Sequence == 4);

        await ctx.AuditChainLeaves.Where(l => l.Sequence == 3).ExecuteDeleteAsync();
        await ctx.AuditChainLeaves.Where(l => l.Sequence == 4)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Hash, new string('b', 64)));

        Assert.Equal(AuditProofStatus.IntegrityFault, (await service.ProofAsync(third.Id, null, default)).Status);
        Assert.Equal(AuditProofStatus.IntegrityFault, (await service.ProofAsync(fourth.Id, null, default)).Status);

        var answer = Assert.IsType<ObjectResult>(await Controller(ctx).GetProof(fourth.Id, null, default));
        Assert.Equal(500, answer.StatusCode);
        Assert.Contains("integrity", JsonSerializer.Serialize(answer.Value), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The streamed tree hands out exactly the proof the recursive one did, for every event.</summary>
    [PostgresFact]
    public async Task EveryProof_IsByteForByteTheOneTheRecursiveTreeGives()
    {
        var (ctx, _) = await SealedAsync(37);
        var service = Checkpoints(ctx);
        var checkpoint = await service.IssueManualAsync("CHG-37", default);
        var rows = await ctx.AuditLogs.AsNoTracking().OrderBy(l => l.Sequence).ToListAsync();
        var digests = rows.Select(r => Convert.FromHexString(r.OamHash!)).ToList();

        Assert.Equal(Convert.ToHexStringLower(MerkleTree.Root(digests)), checkpoint!.RootHash);
        for (var index = 0; index < rows.Count; index++)
        {
            var expected = OpenAuditDocuments.Canonical(OpenAuditDocuments.Proof(
                rows[index].OamHash!, index, rows[index].Id, MerkleTree.AuditPath(digests, index),
                JsonNode.Parse(checkpoint.RootDocument)!.AsObject()));

            Assert.Equal(expected, (await service.ProofAsync(rows[index].Id, checkpoint.Id, default)).Document);
        }
    }

    private static AuditLogsController Controller(ApplicationDbContext ctx) =>
        new(Substitute.For<IAuditLogService>(),
            new AuditChainService(ctx, DataProtectionProvider.Create(nameof(AuditCheckpointTests)), NullLogger<AuditChainService>.Instance),
            Substitute.For<IAuditSigningKeyService>(),
            Checkpoints(ctx))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "test")),
                },
            },
        };

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    [PostgresFact]
    public async Task TheRootDocumentSignature_VerifiesOverItsCanonicalFormWithoutTheSignature()
    {
        var (ctx, _) = await SealedAsync(4);
        var checkpoint = await Checkpoints(ctx).IssueManualAsync("CHG-3", default);

        var root = JsonNode.Parse(checkpoint!.RootDocument)!.AsObject();
        var signature = root["signature"]!["value"]!.GetValue<string>();
        root.Remove("signature");
        var publicKey = new Ed25519PrivateKeyParameters(OamFixtures.TestSeed).GeneratePublicKey().GetEncoded();

        Assert.True(Ed25519Signer.Verify(
            publicKey, Encoding.UTF8.GetBytes(OpenAuditDocuments.Canonical(root)), signature));
    }

    [Theory]
    [InlineData("https://siem.example", "https://siem.example/")]
    [InlineData("https://siem.example:8443/c", "https://siem.example:8443/c")]
    [InlineData("https://siem.example/c#frag", "https://siem.example/c")]
    [InlineData("https://user:pw@siem.example/c?token=abc#x", "https://siem.example/c")]
    public void ThePublicReference_NamesOnlyHostPortAndPath(string url, string expected) =>
        Assert.Equal(expected, AuditCheckpointService.PublicReference(url));

    [PostgresFact]
    public async Task AFailedPublication_RecordsNothingSecretInItsError()
    {
        var (ctx, _) = await SealedAsync(2);
        var options = Options.Create(new AuditCheckpointOptions
        {
            PublishUrl = "https://user:pw@siem.example/c?token=abc",
            PublishToken = "tok-secret",
        });
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(AuditCheckpointService.HttpClientName)
            .Returns(_ => new HttpClient(new ThrowingHandler()));
        var signing = Substitute.For<IAuditSigningKeyService>();
        signing.GetActiveSignerAsync(Arg.Any<CancellationToken>())
            .Returns(new Ed25519Signer(OamFixtures.TestKeyId, OamFixtures.TestSeed));

        var outcome = await new AuditCheckpointService(ctx, signing, clients, options,
            NullLogger<AuditCheckpointService>.Instance).PublishDueAsync(default);

        Assert.Equal(AuditCheckpointPublishStatus.Failed, outcome.Status);
        Assert.Equal(1, outcome.Checkpoint!.PublishAttempts);
        foreach (var secret in new[] { "tok-secret", "pw", "token=abc", "user" })
            Assert.DoesNotContain(secret, outcome.Checkpoint.LastError);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("boom https://user:pw@siem.example/c?token=abc Bearer tok-secret");
    }

    private static async Task<(AuditCheckpointQuartzJob Job, ApplicationDbContext Reader)> JobAsync(
        string cs, HttpMessageHandler handler, string? url, ILogger<AuditCheckpointQuartzJob>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => PostgresFixture.Context(cs));
        services.AddScoped<IAuditLogService>(sp => new AuditLogService(
            new AuditLogRepository(sp.GetRequiredService<ApplicationDbContext>(), NullLogger<AuditLogRepository>.Instance),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new TransactionManager(sp.GetRequiredService<ApplicationDbContext>(), NullLogger<TransactionManager>.Instance)));
        var signing = Substitute.For<IAuditSigningKeyService>();
        signing.GetActiveSignerAsync(Arg.Any<CancellationToken>())
            .Returns(new Ed25519Signer(OamFixtures.TestKeyId, OamFixtures.TestSeed));
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(AuditCheckpointService.HttpClientName).Returns(_ => new HttpClient(handler));
        services.AddScoped(sp => new AuditCheckpointService(
            sp.GetRequiredService<ApplicationDbContext>(), signing, clients,
            Options.Create(new AuditCheckpointOptions { PublishUrl = url }),
            NullLogger<AuditCheckpointService>.Instance));

        var provider = services.BuildServiceProvider();
        return (new AuditCheckpointQuartzJob(
            provider.GetRequiredService<IServiceScopeFactory>(), logger ?? NullLogger<AuditCheckpointQuartzJob>.Instance),
            PostgresFixture.Context(cs));
    }

    private static IJobExecutionContext JobContext()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.JobDetail.Key.Returns(new JobKey("AuditCheckpointJob"));
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private static Task<List<string>> EventNamesAsync(ApplicationDbContext reader, string resourceType) =>
        reader.AuditLogs.AsNoTracking().Where(l => l.ResourceType == resourceType)
            .OrderBy(l => l.CreatedAt).Select(l => l.EventName).ToListAsync();

    [PostgresFact]
    public async Task TheJob_WritesCreate_WhenThePublicationIsAccepted_AndPublishFailed_WhenItIsNot()
    {
        var (_, _, cs) = await SealedWithConnectionAsync(3);

        var (okJob, reader) = await JobAsync(cs, new StubHandler(HttpStatusCode.OK), "https://siem.example/c");
        await okJob.Execute(JobContext());
        Assert.Equal(["audit-checkpoint.create"], await EventNamesAsync(reader, "AuditCheckpoint"));
        var row = await reader.AuditLogs.AsNoTracking().SingleAsync(l => l.ResourceType == "AuditCheckpoint");
        Assert.Contains("anchor=publication", row.ChangeAfter);

        // Move the head so there is something new to publish, then fail it.
        var more = PostgresFixture.Context(cs);
        more.AddRange(Row(10), Row(11));
        await more.SaveChangesAsync();
        await new AuditChainService(more, DataProtectionProvider.Create(nameof(AuditCheckpointTests)),
            NullLogger<AuditChainService>.Instance).SealAsync();

        var (badJob, _) = await JobAsync(cs, new StubHandler(HttpStatusCode.BadGateway), "https://siem.example/c");
        await badJob.Execute(JobContext());
        Assert.Equal(["audit-checkpoint.create", "audit-checkpoint.publish-failed"],
            await EventNamesAsync(PostgresFixture.Context(cs), "AuditCheckpoint"));
    }

    [PostgresFact]
    public async Task TheJob_AuditsAnUnexpectedFailure_InsteadOfLettingItEscape()
    {
        var (_, _, cs) = await SealedWithConnectionAsync(2);

        var (job, reader) = await JobAsync(cs, new ThrowingHandler(), "https://siem.example/c");
        await job.Execute(JobContext());

        Assert.Equal(["audit-checkpoint.publish-failed"], await EventNamesAsync(reader, "AuditCheckpoint"));
    }

    [PostgresFact]
    public async Task AProof_LeadsFromTheEventToTheCheckpointsSignedRoot()
    {
        var (ctx, _) = await SealedAsync(7);
        var service = Checkpoints(ctx);
        var checkpoint = await service.IssueManualAsync("CHG-7", default);
        var target = await ctx.AuditLogs.AsNoTracking().SingleAsync(l => l.Sequence == 4);

        var result = await service.ProofAsync(target.Id, null, default);

        Assert.Equal(AuditProofStatus.Found, result.Status);
        var proof = JsonNode.Parse(result.Document!)!.AsObject();
        OamFixtures.AssertValidProof(proof);
        Assert.Equal(target.OamHash, proof["leaf"]!["hash"]!.GetValue<string>());
        Assert.Equal(3, proof["leaf"]!["index"]!.GetValue<long>());
        Assert.Equal(checkpoint!.RootHash, proof["root"]!["hash"]!.GetValue<string>());

        var hash = MerkleTree.LeafHash(Convert.FromHexString(target.OamHash!));
        foreach (var step in proof["path"]!.AsArray())
        {
            var sibling = Convert.FromHexString(step!["hash"]!.GetValue<string>());
            hash = step["side"]!.GetValue<string>() == "left" ? MerkleTree.NodeHash(sibling, hash) : MerkleTree.NodeHash(hash, sibling);
        }
        Assert.Equal(checkpoint.RootHash, Convert.ToHexStringLower(hash));
    }

    [PostgresFact]
    public async Task AProofSurvivesRetention()
    {
        var (ctx, chain) = await SealedAsync(6);
        var service = Checkpoints(ctx);
        var checkpoint = await service.IssueManualAsync("CHG-6", default);

        await ctx.AuditLogs.Where(l => l.Sequence <= 3).ExecuteDeleteAsync();
        await chain.NotePrunedAsync();
        var survivor = await ctx.AuditLogs.AsNoTracking().SingleAsync(l => l.Sequence == 5);

        var result = await service.ProofAsync(survivor.Id, checkpoint!.Id, default);

        Assert.Equal(AuditProofStatus.Found, result.Status);
        Assert.Equal(6, JsonNode.Parse(result.Document!)!["root"]!["leafCount"]!.GetValue<long>());
    }

    [PostgresFact]
    public async Task NoProof_ForAnUnsealedEvent_OrBeyondEveryCheckpoint()
    {
        var (ctx, _) = await SealedAsync(2);
        var service = Checkpoints(ctx);
        var sealedRow = await ctx.AuditLogs.AsNoTracking().FirstAsync();

        Assert.Equal(AuditProofStatus.NoCheckpoint, (await service.ProofAsync(sealedRow.Id, null, default)).Status);

        var unsealed = new AuditLog { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, ResourceType = "Incident", EventName = "incident.view", EventCategory = "incident-management" };
        ctx.AuditLogs.Add(unsealed);
        await ctx.SaveChangesAsync();

        Assert.Equal(AuditProofStatus.NotSealed, (await service.ProofAsync(unsealed.Id, null, default)).Status);
    }
}
