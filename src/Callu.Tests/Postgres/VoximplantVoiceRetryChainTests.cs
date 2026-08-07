using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Voximplant;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The VoxEngine callback arms the voice retry chain after an unanswered call, and stands it down.</summary>
// Asserted on real rows: both failures were in what the callback wrote, not in what it returned.
[Collection(PostgresCollection.Name)]
public class VoximplantVoiceRetryChainTests(PostgresFixture pg)
{
    private const string Primary = "+905551112233";
    private const string SecondOnCall = "+905554445566";

    [PostgresFact]
    public async Task ALateAlertingCallback_DoesNotUnwindTheArmedChain()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(Callback(incidentId, "session-1", "no_answer", Primary), null, NoopCallbacks());
        var armed = await LoadCallAsync(cs, "session-1");

        Assert.Equal(CallStatus.NoAnswer, armed.Status);
        Assert.NotNull(armed.CompletedAt);
        Assert.NotNull(armed.NextRetryAt);

        // VoxEngine re-delivers the call's 'alerting' event after the call has already ended.
        await persistence.ProcessAsync(Callback(incidentId, "session-1", "alerting", Primary), null, NoopCallbacks());

        var after = await LoadCallAsync(cs, "session-1");
        Assert.Equal(CallStatus.NoAnswer, after.Status);
        Assert.Equal(armed.CompletedAt, after.CompletedAt);       // the sweep's anchor survives
        Assert.Equal(armed.NextRetryAt, after.NextRetryAt);       // the chain survives
    }

    /// <summary>An unrecognised status maps to Connected, whose branch clears the retry — it must not here.</summary>
    [PostgresFact]
    public async Task ALateUnknownStatusCallback_DoesNotDisarmTheChain()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(Callback(incidentId, "session-1", "no_answer", Primary), null, NoopCallbacks());
        var armed = await LoadCallAsync(cs, "session-1");

        await persistence.ProcessAsync(
            Callback(incidentId, "session-1", "some_status_from_a_newer_scenario", Primary), null, NoopCallbacks());

        var after = await LoadCallAsync(cs, "session-1");
        Assert.Equal(CallStatus.NoAnswer, after.Status);
        Assert.Equal(armed.NextRetryAt, after.NextRetryAt);
        Assert.NotNull(after.NextRetryAt);
    }

    /// <summary>
    /// A call that is still alive keeps taking its callbacks — the guard is about calls that have
    /// ENDED, not about non-terminal callbacks in general.
    /// </summary>
    [PostgresFact]
    public async Task AConnectedCallbackForALiveCall_IsStillApplied()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(Callback(incidentId, "session-1", "alerting", Primary), null, NoopCallbacks());
        await persistence.ProcessAsync(Callback(incidentId, "session-1", "connected", Primary), null, NoopCallbacks());

        var stored = await LoadCallAsync(cs, "session-1");
        Assert.Equal(CallStatus.Connected, stored.Status);
        Assert.Null(stored.CompletedAt);
    }

    /// <summary>A phone acknowledgement stands down the other responder's armed retry, not only its own.</summary>
    [PostgresFact]
    public async Task APhoneAcknowledgement_StandsDownTheOtherRespondersArmedRetry()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        // The second on-call's call goes unanswered: their chain is armed.
        await persistence.ProcessAsync(Callback(incidentId, "session-2", "no_answer", SecondOnCall), null, NoopCallbacks());
        Assert.NotNull((await LoadCallAsync(cs, "session-2")).NextRetryAt);

        // The primary picks up and presses 1.
        await persistence.ProcessAsync(Callback(incidentId, "session-1", "acknowledged", Primary), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var incident = await db.Incidents.AsNoTracking().FirstAsync(i => i.Id == incidentId);
        Assert.Equal(IncidentStatus.Acknowledged, incident.Status);

        var stillArmed = await db.CallLogs
            .AsNoTracking()
            .CountAsync(c => c.IncidentId == incidentId && c.NextRetryAt != null);
        Assert.Equal(0, stillArmed);
    }

    // ---------------------------------------------------------------- harness

    private static VoxCallbackRequest Callback(Guid incidentId, string session, string status, string phone) =>
        new()
        {
            IncidentId = incidentId.ToString(),
            CallSessionId = session,
            Status = status,
            Duration = 20,
            Data = new Dictionary<string, object> { ["phone"] = phone }
        };

    private static VoximplantCallbackProcessingCallbacks NoopCallbacks() =>
        new(
            (_, _) => Task.FromResult<VoxCallData?>(null),
            (_, _) => Task.FromResult(true),
            _ => Task.CompletedTask);

    private static async Task<CallLog> LoadCallAsync(string connectionString, string session)
    {
        await using var db = PostgresFixture.Context(connectionString);
        return await db.CallLogs.AsNoTracking().SingleAsync(c => c.CallToken == session);
    }

    private async Task<(string ConnectionString, Guid IncidentId, VoximplantVoiceCallbackPersistence Persistence, ServiceProvider Provider)>
        ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var incident = new Incident
            {
                Title = "Checkout failing",
                Description = "5xx on /pay",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        var provider = services.BuildServiceProvider();

        var persistence = new VoximplantVoiceCallbackPersistence(
            provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            provider,
            NullLogger<VoximplantVoiceCallbackPersistence>.Instance);

        return (cs, incidentId, persistence, provider);
    }
}
