using Callu.Api.Controllers;
using Callu.Application.Services;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Conference;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Drives the forged-ack attack through the callback controller: no token, and a token that disagrees with the body.</summary>
public class VoximplantCallbackControllerTests
{
    private const string ScenarioKey = "shared-scenario-key";

    private readonly ICallDataService _callData = Substitute.For<ICallDataService>();
    private readonly IVideoConferenceService _conference = Substitute.For<IVideoConferenceService>();
    private readonly IVoximplantReplayGuard _replayGuard = Substitute.For<IVoximplantReplayGuard>();
    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    public VoximplantCallbackControllerTests()
    {
        _callData.ValidateScenarioApiKeyAsync(ScenarioKey, Arg.Any<CancellationToken>()).Returns(true);
        _callData.ValidateScenarioApiKeyAsync(
            Arg.Is<string>(k => k != ScenarioKey), Arg.Any<CancellationToken>()).Returns(false);

        _replayGuard.WindowSeconds.Returns(300);
        _replayGuard.TryRegister(Arg.Any<long>(), Arg.Any<string>()).Returns(true);

        _conference.CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConferenceRoomResult { Success = true, RoomId = Guid.NewGuid() });
    }

    private VoximplantCallbackController Sut(bool allowLegacyUnbound = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Voximplant:AllowLegacyUnboundCallbacks"] = allowLegacyUnbound.ToString()
            })
            .Build();

        return new VoximplantCallbackController(
            _callData,
            _conference,
            _replayGuard,
            _dataProtection,
            configuration,
            NullLogger<VoximplantCallbackController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>A token this host really minted, for the incident the call was really placed for.</summary>
    private string TokenFor(Guid incidentId) =>
        new VoxCallbackTokenProtector(_dataProtection).Issue(incidentId.ToString())!;

    private static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private static VoxCallbackRequest Ack(string? incidentId, string status = "acknowledged") => new()
    {
        IncidentId = incidentId ?? string.Empty,
        CallSessionId = "session-1",
        Status = status
    };

    private Task<IActionResult> PostCallbackAsync(
        VoxCallbackRequest body,
        string? scenarioKey = ScenarioKey,
        string? callToken = null,
        string? timestamp = null,
        string? nonce = "nonce-1",
        bool allowLegacyUnbound = false)
        => Sut(allowLegacyUnbound).ReceiveCallback(body, scenarioKey, callToken, timestamp ?? Now(), nonce);

    private Task NoCallbackWasProcessed() =>
        _callData.DidNotReceive().ProcessCallbackAsync(
            Arg.Any<VoxCallbackRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

    // ---- the forged ack ------------------------------------------------------

    /// <summary>
    /// The whole attack in one test: attacker holds the scenario key, posts "acknowledged" for an
    /// incident of their choosing, and has no per-call token because they never received a call.
    /// </summary>
    [Fact]
    public async Task ForgedAck_WithTheScenarioKeyButNoCallToken_IsRefused()
    {
        var victim = Guid.NewGuid();

        var result = await PostCallbackAsync(Ack(victim.ToString()), callToken: null);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    [Fact]
    public async Task ForgedAck_WithAGarbageCallToken_IsRefused()
    {
        var result = await PostCallbackAsync(Ack(Guid.NewGuid().ToString()), callToken: "not-a-real-token");

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    /// <summary>
    /// A token minted by some other host (or an older key ring) decrypts nowhere here, so it is no
    /// better than no token at all.
    /// </summary>
    [Fact]
    public async Task ForgedAck_WithATokenFromAnotherKeyring_IsRefused()
    {
        var foreignToken = new VoxCallbackTokenProtector(new EphemeralDataProtectionProvider())
            .Issue(Guid.NewGuid().ToString())!;

        var result = await PostCallbackAsync(Ack(Guid.NewGuid().ToString()), callToken: foreignToken);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    /// <summary>
    /// The lateral move: the attacker WAS called about incident A (so holds a legitimate token) and
    /// tries to acknowledge incident B with it. The body is not allowed to widen the token's scope.
    /// </summary>
    [Fact]
    public async Task ATokenForOneIncident_CannotAcknowledgeAnother()
    {
        var mine = Guid.NewGuid();
        var someoneElses = Guid.NewGuid();

        var result = await PostCallbackAsync(Ack(someoneElses.ToString()), callToken: TokenFor(mine));

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    [Fact]
    public async Task AMissingScenarioKey_IsRefused_EvenWithAValidCallToken()
    {
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString()), scenarioKey: null, callToken: TokenFor(incidentId));

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    [Fact]
    public async Task AWrongScenarioKey_IsRefused_EvenWithAValidCallToken()
    {
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString()), scenarioKey: "stolen-but-wrong", callToken: TokenFor(incidentId));

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    // ---- the real scenario still gets through --------------------------------

    [Fact]
    public async Task ARealAck_WithItsOwnCallToken_IsProcessed()
    {
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(Ack(incidentId.ToString()), callToken: TokenFor(incidentId));

        Assert.IsType<OkObjectResult>(result);
        await _callData.Received(1).ProcessCallbackAsync(
            Arg.Is<VoxCallbackRequest>(c => c.IncidentId == incidentId.ToString()),
            ScenarioKey,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The incident is taken FROM the token, so a scenario that reports no incident_id at all still
    /// acknowledges the right one — and there is no body field left for an attacker to steer.
    /// </summary>
    [Fact]
    public async Task WithNoIncidentInTheBody_TheIncidentComesFromTheToken()
    {
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(Ack(incidentId: null), callToken: TokenFor(incidentId));

        Assert.IsType<OkObjectResult>(result);
        await _callData.Received(1).ProcessCallbackAsync(
            Arg.Is<VoxCallbackRequest>(c => c.IncidentId == incidentId.ToString()),
            ScenarioKey,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Conference-room lifecycle events hold no token and are inert server-side, so they stay callable.</summary>
    [Theory]
    [InlineData("participant_joined")]
    [InlineData("participant_left")]
    [InlineData("conference_started")]
    [InlineData("conference_ended")]
    public async Task InertConferenceLifecycleStatuses_AreStillAcceptedWithoutAToken(string status)
    {
        var result = await PostCallbackAsync(Ack(Guid.NewGuid().ToString(), status), callToken: null);

        Assert.IsType<OkObjectResult>(result);
        await _callData.Received(1).ProcessCallbackAsync(
            Arg.Any<VoxCallbackRequest>(), ScenarioKey, Arg.Any<CancellationToken>());
    }

    /// <summary>An unknown status is not inert (it maps to Connected and writes a call log), so it needs a token.</summary>
    [Fact]
    public async Task AnUnknownStatus_IsNotExemptFromTheToken()
    {
        var result = await PostCallbackAsync(Ack(Guid.NewGuid().ToString(), "something-unexpected"), callToken: null);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    // ---- what the responder is TOLD, out loud, while still holding the phone --

    // The response body's `escalation_paged` is what the scenario picks its announcement from, so it is
    // read back here rather than trusting the 200.

    /// <summary>
    /// The escalation paged NOBODY. The body must say so — this is the only thing standing between the
    /// last human who knows about the incident and hanging up reassured while nothing whatsoever happens.
    /// </summary>
    [Fact]
    public async Task APressTwoThatPagedNobody_ComesBackAsEscalationPagedFalse()
    {
        var incidentId = Guid.NewGuid();
        _callData.ProcessCallbackAsync(
                Arg.Any<VoxCallbackRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new VoxCallbackResult(EscalationRequested: true, EscalationPagedSomeone: false));

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString(), "escalated"), callToken: TokenFor(incidentId));

        Assert.False(EscalationPaged(result),
            "the responder pressed 2, nobody was paged, and the scenario is about to tell them help is on "
            + "the way — the honest prompt is chosen from this field and nothing else");
    }

    /// <summary>...and when somebody WAS paged it says that, or the honest prompt cries wolf on every call.</summary>
    [Fact]
    public async Task APressTwoThatPagedSomeone_ComesBackAsEscalationPagedTrue()
    {
        var incidentId = Guid.NewGuid();
        _callData.ProcessCallbackAsync(
                Arg.Any<VoxCallbackRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new VoxCallbackResult(EscalationRequested: true, EscalationPagedSomeone: true));

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString(), "escalated"), callToken: TokenFor(incidentId));

        Assert.True(EscalationPaged(result));
    }

    /// <summary>An ordinary callback claims no escalation — the scenario ignores the field, but it must not lie.</summary>
    [Fact]
    public async Task ACallbackThatIsNotAKeypress_ClaimsNoEscalation()
    {
        var incidentId = Guid.NewGuid();
        _callData.ProcessCallbackAsync(
                Arg.Any<VoxCallbackRequest>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(VoxCallbackResult.None);

        var result = await PostCallbackAsync(Ack(incidentId.ToString()), callToken: TokenFor(incidentId));

        Assert.False(EscalationPaged(result));
    }

    /// <summary>
    /// Reads the field the VoxEngine scenario reads, BY NAME. Renaming it server-side is a silent break:
    /// the scenario asks for `escalation_paged`, gets undefined, and plays the reassuring prompt.
    /// </summary>
    private static bool EscalationPaged(IActionResult result)
    {
        var body = Assert.IsType<OkObjectResult>(result).Value;
        Assert.NotNull(body);

        var field = body!.GetType().GetProperty("escalation_paged");
        Assert.True(field is not null,
            "the callback response has no 'escalation_paged' field — that is the one the scenario branches "
            + "on to decide what the responder hears, and without it the phone always says help is coming");

        return Assert.IsType<bool>(field!.GetValue(body));
    }

    // ---- the legacy escape hatch --------------------------------------------

    /// <summary>The legacy flag is the escape hatch for an instance that has not re-provisioned its scenarios.</summary>
    [Fact]
    public async Task WithTheLegacyFlagOn_AnUnboundAck_IsAccepted()
    {
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString()), callToken: null, allowLegacyUnbound: true);

        Assert.IsType<OkObjectResult>(result);
        await _callData.Received(1).ProcessCallbackAsync(
            Arg.Is<VoxCallbackRequest>(c => c.IncidentId == incidentId.ToString()),
            ScenarioKey,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Even with the flag on, a token that IS presented still binds: legacy tolerance must not become
    /// a way to re-point a real call's callback at another incident.
    /// </summary>
    [Fact]
    public async Task WithTheLegacyFlagOn_ATokenThatDisagreesWithTheBody_IsStillRefused()
    {
        var result = await PostCallbackAsync(
            Ack(Guid.NewGuid().ToString()),
            callToken: TokenFor(Guid.NewGuid()),
            allowLegacyUnbound: true);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    // ---- replay --------------------------------------------------------------

    [Fact]
    public async Task ACallbackOutsideTheTimestampWindow_IsRefused()
    {
        var incidentId = Guid.NewGuid();
        var stale = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString();

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString()), callToken: TokenFor(incidentId), timestamp: stale);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    [Fact]
    public async Task ACallbackWithAReplayedNonce_IsRefused()
    {
        _replayGuard.TryRegister(Arg.Any<long>(), "seen-before").Returns(false);
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString()), callToken: TokenFor(incidentId), nonce: "seen-before");

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ACallbackWithNoNonce_IsRefused(string? nonce)
    {
        var incidentId = Guid.NewGuid();

        var result = await PostCallbackAsync(
            Ack(incidentId.ToString()), callToken: TokenFor(incidentId), nonce: nonce);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await NoCallbackWasProcessed();
    }

    // ---- conference room -----------------------------------------------------

    /// <summary>
    /// The 9 keypress opens a bridge on an incident. Unbound it is a free conference room against
    /// any incident id an attacker cares to name, so there is no inert-status exemption here at all.
    /// </summary>
    [Fact]
    public async Task AConferenceRoomRequest_WithoutACallToken_IsRefused()
    {
        var result = await Sut().CreateConferenceRoom(
            new CreateConferenceRoomRequest { IncidentId = Guid.NewGuid() },
            ScenarioKey, callbackToken: null, timestamp: Now(), nonce: "n", ct: default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _conference.DidNotReceive().CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConferenceRoomRequest_ForAnIncidentTheTokenIsNotBoundTo_IsRefused()
    {
        var result = await Sut().CreateConferenceRoom(
            new CreateConferenceRoomRequest { IncidentId = Guid.NewGuid() },
            ScenarioKey, TokenFor(Guid.NewGuid()), Now(), "n", default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _conference.DidNotReceive().CreateRoomAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConferenceRoomRequest_UsesTheIncidentFromTheToken_NotTheBody()
    {
        var boundIncident = Guid.NewGuid();

        // Body says nothing; the token is the only source of the incident.
        var result = await Sut().CreateConferenceRoom(
            new CreateConferenceRoomRequest { IncidentId = Guid.Empty },
            ScenarioKey, TokenFor(boundIncident), Now(), "n", default);

        Assert.IsType<OkObjectResult>(result);
        await _conference.Received(1).CreateRoomAsync(boundIncident, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConferenceRoomRequest_WithTheLegacyFlagOn_FallsBackToTheBodyIncident()
    {
        var incidentId = Guid.NewGuid();

        var result = await Sut(allowLegacyUnbound: true).CreateConferenceRoom(
            new CreateConferenceRoomRequest { IncidentId = incidentId },
            ScenarioKey, callbackToken: null, timestamp: Now(), nonce: "n", ct: default);

        Assert.IsType<OkObjectResult>(result);
        await _conference.Received(1).CreateRoomAsync(incidentId, Arg.Any<CancellationToken>());
    }
}
