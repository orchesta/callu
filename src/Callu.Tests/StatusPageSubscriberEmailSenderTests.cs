using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Callu.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public sealed class StatusPageSubscriberEmailSenderTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;

    public StatusPageSubscriberEmailSenderTests()
        => _ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"subs-{Guid.NewGuid():N}").Options);

    public void Dispose() => _ctx.Dispose();

    private sealed class SendTracker(Func<string, bool> outcome)
    {
        private int _inFlight;

        public int MaxConcurrent { get; private set; }
        public List<string> Sent { get; } = [];

        public async Task<bool> SendAsync(string to)
        {
            var now = Interlocked.Increment(ref _inFlight);
            MaxConcurrent = Math.Max(MaxConcurrent, now);

            await Task.Yield();

            try
            {
                if (!outcome(to))
                    return false;

                Sent.Add(to);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private static (IEmailService Service, SendTracker Tracker) EmailService(Func<string, bool> outcome)
    {
        var tracker = new SendTracker(outcome);
        var service = Substitute.For<IEmailService>();

        service.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => tracker.SendAsync(call.ArgAt<string>(0)));

        return (service, tracker);
    }

    private StatusPageSubscriberEmailSender Sender(IEmailService email) => new(
        new Repository<StatusPageIncident>(_ctx, NullLogger<Repository<StatusPageIncident>>.Instance),
        new StatusPageRepository(_ctx, NullLogger<StatusPageRepository>.Instance),
        new Repository<StatusPageSubscriber>(_ctx, NullLogger<Repository<StatusPageSubscriber>>.Instance),
        email,
        NullLogger<StatusPageSubscriberEmailSender>.Instance);

    private async Task<Guid> SeedAsync(int confirmedSubscribers, int unconfirmed = 0)
    {
        var page = new StatusPage
        {
            Id = Guid.NewGuid(),
            Name = "Public status",
            Slug = $"s-{Guid.NewGuid():N}",
            AllowSubscriptions = true,
        };

        var incident = new StatusPageIncident
        {
            Id = Guid.NewGuid(),
            StatusPageId = page.Id,
            Title = "Elevated error rate",
            Status = "investigating",
        };

        _ctx.StatusPages.Add(page);
        _ctx.StatusPageIncidents.Add(incident);

        for (var i = 0; i < confirmedSubscribers; i++)
            _ctx.StatusPageSubscribers.Add(new StatusPageSubscriber
            {
                Id = Guid.NewGuid(),
                StatusPageId = page.Id,
                Email = $"subscriber{i}@example.test",
                IsConfirmed = true,
            });

        for (var i = 0; i < unconfirmed; i++)
            _ctx.StatusPageSubscribers.Add(new StatusPageSubscriber
            {
                Id = Guid.NewGuid(),
                StatusPageId = page.Id,
                Email = $"pending{i}@example.test",
                IsConfirmed = false,
            });

        await _ctx.SaveChangesAsync();
        return incident.Id;
    }

    [Fact]
    public async Task SendsAreSerialised_SoTheSharedDbContextIsNeverReentered()
    {
        var incidentId = await SeedAsync(confirmedSubscribers: 5);
        var (email, tracker) = EmailService(_ => true);

        await Sender(email).SendForIncidentAsync(incidentId);

        Assert.Equal(5, tracker.Sent.Count);
        Assert.Equal(1, tracker.MaxConcurrent);
    }

    [Fact]
    public async Task EveryConfirmedSubscriberIsNotified_AndUnconfirmedOnesAreNot()
    {
        var incidentId = await SeedAsync(confirmedSubscribers: 3, unconfirmed: 2);
        var (email, tracker) = EmailService(_ => true);

        await Sender(email).SendForIncidentAsync(incidentId);

        Assert.Equal(3, tracker.Sent.Count);
        Assert.All(tracker.Sent, address => Assert.StartsWith("subscriber", address));
    }

    [Fact]
    public async Task WhenNoSubscriberCouldBeReached_ItFaults()
    {
        var incidentId = await SeedAsync(confirmedSubscribers: 3);
        var (email, tracker) = EmailService(_ => false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sender(email).SendForIncidentAsync(incidentId));
    }

    [Fact]
    public async Task WhenSomeSucceeded_ItDoesNotFault_SoNobodyIsNotifiedTwice()
    {
        var incidentId = await SeedAsync(confirmedSubscribers: 4);
        var (email, tracker) = EmailService(to => to.EndsWith("0@example.test", StringComparison.Ordinal));

        await Sender(email).SendForIncidentAsync(incidentId);

        Assert.Single(tracker.Sent);
    }

    [Fact]
    public async Task APageWithSubscriptionsDisabled_NotifiesNobody()
    {
        var incidentId = await SeedAsync(confirmedSubscribers: 2);

        var page = await _ctx.StatusPages.FirstAsync();
        page.AllowSubscriptions = false;
        await _ctx.SaveChangesAsync();

        var (email, tracker) = EmailService(_ => true);
        await Sender(email).SendForIncidentAsync(incidentId);

        Assert.Empty(tracker.Sent);
    }
}
