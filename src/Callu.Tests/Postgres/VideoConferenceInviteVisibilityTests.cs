using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.DI;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Communication;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Callu.Tests;

/// <summary>
/// ConferenceRoomResult.InvitesSentCount is the only signal that says whether a conference invite
/// actually reached anyone — ParticipantCount alone cannot, since a room can carry participant rows
/// nobody could actually be sent a link on.
/// </summary>
[Collection(PostgresCollection.Name)]
public class VideoConferenceInviteVisibilityTests(PostgresFixture pg)
{
    private const string UserId = "responder-1";
    private const string Phone = "+905321234567";

    [PostgresFact]
    public async Task NoTeamAssigned_InvitesSentCountIsZero()
    {
        var world = await ArrangeAsync(assignTeam: false);

        var result = await world.Service.CreateRoomAsync(world.IncidentId);

        Assert.True(result.Success);
        Assert.Equal(0, result.ParticipantCount);
        Assert.Equal(0, result.InvitesSentCount);
    }

    [PostgresFact]
    public async Task ATeamWithNoUsableChannel_InvitesSentCountIsZeroDespiteHavingParticipants()
    {
        var world = await ArrangeAsync(assignTeam: true, smsProvider: null, smtpConfigured: false);

        var result = await world.Service.CreateRoomAsync(world.IncidentId);

        Assert.True(result.Success);
        Assert.Equal(1, result.ParticipantCount);
        Assert.Equal(0, result.InvitesSentCount);
    }

    [PostgresFact]
    public async Task AParticipantReachedBySms_CountsTowardInvitesSent()
    {
        var sms = Substitute.For<ICommunicationProvider>();
        sms.SendSmsAsync(Arg.Any<SendSmsRequest>()).Returns(new SmsResult { Success = true });

        var world = await ArrangeAsync(assignTeam: true, smsProvider: sms, smtpConfigured: false);

        var result = await world.Service.CreateRoomAsync(world.IncidentId);

        Assert.Equal(1, result.ParticipantCount);
        Assert.Equal(1, result.InvitesSentCount);
    }

    [PostgresFact]
    public async Task AnSmsProviderThatRejectsEveryMessage_LeavesInvitesSentCountZero()
    {
        var sms = Substitute.For<ICommunicationProvider>();
        sms.SendSmsAsync(Arg.Any<SendSmsRequest>()).Returns(new SmsResult { Success = false, ErrorMessage = "balance exhausted" });

        var world = await ArrangeAsync(assignTeam: true, smsProvider: sms, smtpConfigured: false);

        var result = await world.Service.CreateRoomAsync(world.IncidentId);

        Assert.Equal(1, result.ParticipantCount);
        Assert.Equal(0, result.InvitesSentCount);
    }

    // ---------------------------------------------------------------- harness

    private sealed record World(Guid IncidentId, IVideoConferenceService Service);

    private async Task<World> ArrangeAsync(
        bool assignTeam, ICommunicationProvider? smsProvider = null, bool smtpConfigured = false)
    {
        var cs = await pg.CreateDatabaseAsync();
        await PostgresFixture.MigrateToHeadAsync(cs);

        Guid incidentId;
        await using (var db = PostgresFixture.Context(cs))
        {
            db.Users.Add(new ApplicationUser
            {
                Id = UserId,
                UserName = "ada",
                Email = "ada@example.com",
                PhoneNumber = Phone,
                FirstName = "Ada",
                LastName = "Çelik",
            });

            Guid? teamId = null;
            if (assignTeam)
            {
                var team = new Team { Name = "on-call" };
                db.Teams.Add(team);
                await db.SaveChangesAsync();

                db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = UserId });
                teamId = team.Id;
            }

            var incident = new Incident
            {
                Title = "Checkout failing",
                Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.Open,
                StartedAt = DateTime.UtcNow,
                TeamId = teamId
            };
            db.Add(incident);
            await db.SaveChangesAsync();
            incidentId = incident.Id;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICurrentUserService>());
        services.AddPersistenceModule(cs, enableParameterLogging: false);

        var registry = Substitute.For<ICommunicationProviderRegistry>();
        registry.GetProvider(CommunicationCapability.Sms).Returns(smsProvider);
        services.AddSingleton(registry);

        var contacts = Substitute.For<IUserContactRepository>();
        contacts.GetContactsByIdsAsync(Arg.Any<List<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserContactSnapshot> { new(UserId, "Ada", Phone, "ada@example.com") });
        services.AddSingleton(contacts);

        var email = Substitute.For<IEmailService>();
        email.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(smtpConfigured);
        services.AddSingleton(email);

        var orgSettings = Substitute.For<IOrganizationSettingsService>();
        orgSettings.GetPublicBaseUrlAsync(Arg.Any<CancellationToken>()).Returns("https://callu.example.com");
        services.AddSingleton(orgSettings);

        services.AddScoped<IVideoConferenceService, VideoConferenceService>();

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IVideoConferenceService>();

        return new World(incidentId, service);
    }
}
