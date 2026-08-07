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

/// <summary>Why a call reached nobody, on the row and the timeline an operator reads.</summary>
// The carrier's own words used to go only into the metadata blob, so a trunk refusing every dial with
// "403 Auth Failed" showed up as "Call Failed" and could only be diagnosed in the provider's panel.
[Collection(PostgresCollection.Name)]
public class VoiceCallFailureReasonTests(PostgresFixture pg)
{
    private const string Phone = "+905551112233";

    [PostgresFact]
    public async Task WhatTheCarrierSaid_IsOnTheCallLogAndTheTimeline()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(
            Callback(incidentId, "session-1", "failed", new() { ["code"] = 403, ["reason"] = "Auth Failed" }),
            null,
            NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var call = await db.CallLogs.AsNoTracking().SingleAsync(c => c.CallToken == "session-1");
        Assert.Equal("403 Auth Failed", call.FailureReason);

        var timeline = await db.Set<IncidentTimelineEvent>().AsNoTracking()
            .SingleAsync(t => t.IncidentId == incidentId && t.EventType == TimelineEventType.CallFailed);
        Assert.Contains("403 Auth Failed", timeline.Description ?? "", StringComparison.Ordinal);
    }

    /// <summary>A blank reason reads as Callu losing it; the gap is the provider's and has to say so.</summary>
    [PostgresFact]
    public async Task AFailureTheProviderNeverExplained_SaysThatRatherThanNothing()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(
            Callback(incidentId, "session-1", "failed", []), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var call = await db.CallLogs.AsNoTracking().SingleAsync(c => c.CallToken == "session-1");
        Assert.Equal(VoiceCallFailureReason.NotReported, call.FailureReason);
    }

    /// <summary>An unanswered call explains itself; a duration is not a reason and must not be read as one.</summary>
    [PostgresFact]
    public async Task AnUnansweredCall_LeavesTheReasonEmpty()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(
            Callback(incidentId, "session-1", "no_answer", new() { ["duration"] = 30 }), null, NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var call = await db.CallLogs.AsNoTracking().SingleAsync(c => c.CallToken == "session-1");
        Assert.Null(call.FailureReason);
    }

    /// <summary>A provider can hand back an arbitrarily long reason, and the column is 500.</summary>
    [PostgresFact]
    public async Task AReasonLongerThanTheColumn_IsClippedRatherThanLosingTheWholeCallback()
    {
        var (cs, incidentId, persistence, provider) = await ArrangeAsync();
        await using var _ = provider;

        await persistence.ProcessAsync(
            Callback(incidentId, "session-1", "failed", new() { ["reason"] = new string('x', 4000) }),
            null,
            NoopCallbacks());

        await using var db = PostgresFixture.Context(cs);
        var call = await db.CallLogs.AsNoTracking().SingleAsync(c => c.CallToken == "session-1");
        Assert.Equal(CallLog.MaxFailureReasonLength, call.FailureReason!.Length);
    }

    // ---------------------------------------------------------------- harness

    private static VoxCallbackRequest Callback(
        Guid incidentId, string session, string status, Dictionary<string, object> data)
    {
        data["phone"] = Phone;
        return new VoxCallbackRequest
        {
            IncidentId = incidentId.ToString(),
            CallSessionId = session,
            Status = status,
            Duration = 0,
            Data = data
        };
    }

    private static VoximplantCallbackProcessingCallbacks NoopCallbacks() =>
        new(
            (_, _) => Task.FromResult<VoxCallData?>(null),
            (_, _) => Task.FromResult(true),
            _ => Task.CompletedTask);

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
