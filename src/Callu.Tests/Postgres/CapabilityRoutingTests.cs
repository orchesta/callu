using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Voice and video can sit on different providers, so each channel is pinned separately.</summary>
// The table and the registry already did this; nothing could write to it, so the choice existed
// only for whoever was willing to edit the database by hand.
[Collection(PostgresCollection.Name)]
public class CapabilityRoutingTests(PostgresFixture pg)
{
    /// <summary>Two live routes for one channel are refused by the schema, not merely tie-broken by a read.</summary>
    // Reachable from the unrouted starting state, which is the default: two concurrent first pins.
    [PostgresFact]
    public async Task TwoLiveMappingsForOneCapability_AreImpossible()
    {
        var world = await ArrangeAsync();

        await using var db = PostgresFixture.Context(world.ConnectionString);
        foreach (var providerId in new[] { world.VoximplantId, world.CalluVoiceId })
        {
            db.Add(new CapabilityProviderMapping
            {
                Id = Guid.NewGuid(),
                Capability = CommunicationCapability.VoiceCalls,
                ProviderId = providerId,
                Priority = 0,
                IsEnabled = true
            });
        }

        var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", (conflict.InnerException as Npgsql.PostgresException)?.SqlState);
    }

    /// <summary>A team pages through one policy; a second active one is refused rather than picked at random.</summary>
    [PostgresFact]
    public async Task TwoActivePoliciesForOneTeam_AreImpossible()
    {
        var world = await ArrangeAsync();

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var team = new Team { Id = Guid.NewGuid(), Name = $"Payments {Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        db.Add(team);
        await db.SaveChangesAsync();

        foreach (var name in new[] { "first", "second" })
        {
            db.Add(new EscalationPolicy
            {
                Id = Guid.NewGuid(),
                Name = $"{name} {Guid.NewGuid():N}",
                TeamId = team.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", (conflict.InnerException as Npgsql.PostgresException)?.SqlState);
    }

    [PostgresFact]
    public async Task NothingIsPinnedUntilSomebodyPinsIt()
    {
        var world = await ArrangeAsync();

        var routes = (await world.Providers.GetCapabilityRoutesAsync()).ToList();

        var voice = routes.Single(r => r.Capability == CommunicationCapability.VoiceCalls);
        Assert.Null(voice.ProviderId);
    }

    /// <summary>The choice on offer is only ever a provider that can actually do the job.</summary>
    [PostgresFact]
    public async Task OnlyProvidersThatDeclareTheCapabilityAreOffered()
    {
        var world = await ArrangeAsync();

        var routes = (await world.Providers.GetCapabilityRoutesAsync()).ToList();

        var video = routes.Single(r => r.Capability == CommunicationCapability.VideoConference);
        Assert.Equal(["Voximplant"], video.Candidates.Select(c => c.Name));

        var voice = routes.Single(r => r.Capability == CommunicationCapability.VoiceCalls);
        Assert.Equal(["Voximplant", "Self-hosted voice"], voice.Candidates.Select(c => c.Name).Order().Reverse());
    }

    /// <summary>The case the owner asked for: video on one provider, voice on another.</summary>
    [PostgresFact]
    public async Task VoiceAndVideoCanSitOnDifferentProviders()
    {
        var world = await ArrangeAsync();

        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, world.CalluVoiceId);
        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VideoConference, world.VoximplantId);

        var routes = (await world.Providers.GetCapabilityRoutesAsync()).ToList();

        Assert.Equal(world.CalluVoiceId, routes.Single(r => r.Capability == CommunicationCapability.VoiceCalls).ProviderId);
        Assert.Equal(world.VoximplantId, routes.Single(r => r.Capability == CommunicationCapability.VideoConference).ProviderId);
    }

    /// <summary>Routing a channel to a provider that cannot carry it gives it to nobody.</summary>
    [PostgresFact]
    public async Task AProviderThatCannotDoTheJobIsRefused()
    {
        var world = await ArrangeAsync();

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VideoConference, world.CalluVoiceId));

        Assert.Contains("does not support", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pinning again replaces the pin rather than stacking a second one behind it.</summary>
    [PostgresFact]
    public async Task RepinningReplacesTheRouteInsteadOfAddingToIt()
    {
        var world = await ArrangeAsync();

        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, world.CalluVoiceId);
        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, world.VoximplantId);

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<CapabilityProviderMapping>().AsNoTracking()
            .Where(m => m.Capability == CommunicationCapability.VoiceCalls && !m.IsDeleted)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(world.VoximplantId, rows[0].ProviderId);
    }

    [PostgresFact]
    public async Task ClearingThePinLetsTheOrdinaryOrderApplyAgain()
    {
        var world = await ArrangeAsync();
        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, world.CalluVoiceId);

        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, null);

        var routes = (await world.Providers.GetCapabilityRoutesAsync()).ToList();
        Assert.Null(routes.Single(r => r.Capability == CommunicationCapability.VoiceCalls).ProviderId);
    }

    /// <summary>Which channel goes where decides who gets paged, so it is audited.</summary>
    [PostgresFact]
    public async Task PinningAChannelIsRecorded()
    {
        var world = await ArrangeAsync();

        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, world.CalluVoiceId);

        await using var db = PostgresFixture.Context(world.ConnectionString);
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.ResourceType == "CapabilityRoute")
            .ToListAsync();

        Assert.Single(rows);
        Assert.Contains("Self-hosted voice", rows[0].ChangeAfter ?? "", StringComparison.Ordinal);
    }

    /// <summary>A channel nothing can carry is not a choice, so it is not offered.</summary>
    // WhatsApp is declared by the enum and implemented by no provider; a row that can only ever
    // say "nothing supports this" is noise on a screen about what to do.
    [PostgresFact]
    public async Task AChannelNoConfiguredProviderCanCarryIsNotOffered()
    {
        var world = await ArrangeAsync();

        var routes = (await world.Providers.GetCapabilityRoutesAsync()).ToList();

        Assert.DoesNotContain(routes, r => r.Capability == CommunicationCapability.WhatsApp);
        Assert.Contains(routes, r => r.Capability == CommunicationCapability.VoiceCalls);
    }

    /// <summary>A route onto a switched-off provider is visible as such, not silently fine.</summary>
    [PostgresFact]
    public async Task ARouteOntoADisabledProviderSaysSo()
    {
        var world = await ArrangeAsync();
        await world.Providers.SetCapabilityRouteAsync(CommunicationCapability.VoiceCalls, world.CalluVoiceId);

        await using (var db = PostgresFixture.Context(world.ConnectionString))
        {
            var provider = await db.Set<CommunicationProvider>().FirstAsync(p => p.Id == world.CalluVoiceId);
            provider.IsEnabled = false;
            await db.SaveChangesAsync();
        }

        var routes = (await world.Providers.GetCapabilityRoutesAsync()).ToList();

        Assert.False(routes.Single(r => r.Capability == CommunicationCapability.VoiceCalls).IsProviderEnabled);
    }

    private sealed record World(
        string ConnectionString, Guid VoximplantId, Guid CalluVoiceId, ICommunicationProviderService Providers);

    private async Task<World> ArrangeAsync()
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid voximplantId, calluVoiceId;
        await using (var db = PostgresFixture.Context(cs))
        {
            var voximplant = new CommunicationProvider
            {
                Name = "Voximplant",
                ProviderType = "voximplant",
                ConfigJson = "{}",
                IsEnabled = true,
                Priority = 1,
                Capabilities = CommunicationCapability.VoiceCalls | CommunicationCapability.VideoConference,
            };
            var calluVoice = new CommunicationProvider
            {
                Name = "Self-hosted voice",
                ProviderType = "callu-voice",
                ConfigJson = "{}",
                IsEnabled = true,
                Priority = 2,
                Capabilities = CommunicationCapability.VoiceCalls,
            };
            db.AddRange(voximplant, calluVoice);
            await db.SaveChangesAsync();
            voximplantId = voximplant.Id;
            calluVoiceId = calluVoice.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;

        var providers = new CommunicationProviderService(
            sp.GetRequiredService<ICommunicationProviderRepository>(),
            sp.GetRequiredService<ICapabilityProviderMappingRepository>(),
            sp.GetRequiredService<ITransactionManager>(),
            Substitute.For<ICommunicationProviderRegistry>(),
            new ProviderSecretProtector(sp.GetRequiredService<IDataProtectionProvider>(), NullLogger<ProviderSecretProtector>.Instance),
            sp.GetRequiredService<IAuditLogService>(),
            NullLogger<CommunicationProviderService>.Instance);

        return new World(cs, voximplantId, calluVoiceId, providers);
    }
}
