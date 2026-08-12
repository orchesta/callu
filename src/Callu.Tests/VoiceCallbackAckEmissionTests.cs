using Callu.Application.Plugins;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.CalluVoice;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>A phone acknowledgement hands the same event to the ACK dispatcher a UI acknowledgement would.</summary>
public class VoiceCallbackAckEmissionTests : IDisposable
{
    private static readonly Guid IncidentId = Guid.NewGuid();

    private readonly string _dbName = $"voice-ack-{Guid.NewGuid():N}";
    private readonly ApplicationDbContext _ctx;
    private readonly IIncidentEventDispatcher _dispatcher = Substitute.For<IIncidentEventDispatcher>();
    private readonly ServiceProvider _provider;

    public VoiceCallbackAckEmissionTests()
    {
        _ctx = NewContext();

        var services = new ServiceCollection();
        services.AddSingleton(_dispatcher);
        services.AddSingleton(Substitute.For<IAuditLogService>());
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AKeypressThatTakesAnOpenIncident_EmitsAcknowledge()
    {
        SeedIncident(IncidentStatus.Open);
        var emitted = new TaskCompletionSource();
        _dispatcher.SendServiceAckAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Callu.Application.Plugins.AckDispatchOutcome.Recorded)
            .AndDoes(_ => emitted.TrySetResult());

        await Persistence().ProcessAsync(Ticket(), Callback("acknowledged"));

        // The dispatch runs on a background task so the phone scenario is not kept waiting.
        await emitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _dispatcher.Received(1)
            .SendServiceAckAsync(IncidentId, "acknowledge", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AKeypressOnAnAlreadyAcknowledgedIncident_EmitsNothing()
    {
        SeedIncident(IncidentStatus.Acknowledged);

        await Persistence().ProcessAsync(Ticket(), Callback("acknowledged"));

        await _dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
    }

    [Fact]
    public async Task ANonAcknowledgingCallback_EmitsNothing()
    {
        SeedIncident(IncidentStatus.Open);

        await Persistence().ProcessAsync(Ticket(), Callback("no_answer"));

        await _dispatcher.DidNotReceiveWithAnyArgs()
            .SendServiceAckAsync(default, default!, default);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(_dbName).Options);

    private sealed class Factory(Func<ApplicationDbContext> create) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => create();
    }

    private CalluVoiceCallbackPersistence Persistence() =>
        new(new Factory(NewContext), _provider, NullLogger<CalluVoiceCallbackPersistence>.Instance);

    private void SeedIncident(IncidentStatus status)
    {
        _ctx.Incidents.Add(new Incident
        {
            Id = IncidentId,
            Title = "redis down",
            Status = status,
            Severity = IncidentSeverity.High,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        _ctx.SaveChanges();
        _ctx.ChangeTracker.Clear();
    }

    private static CalluVoiceCallbackTicket Ticket() =>
        new(IncidentId, Guid.NewGuid().ToString("D"), "+15550100");

    private static CalluVoiceCallbackRequest Callback(string status) => new()
    {
        CallId = Guid.NewGuid().ToString("D"),
        Status = status,
        DurationSeconds = 12,
    };
}
