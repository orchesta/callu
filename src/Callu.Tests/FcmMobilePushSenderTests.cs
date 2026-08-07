using System.Net;
using System.Security.Cryptography;
using System.Text;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Push;
using Callu.Shared.Models.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

public class FcmMobilePushSenderTests
{
    private readonly IFirebaseSettingsRepository _settings = Substitute.For<IFirebaseSettingsRepository>();
    private readonly IUserPushDeviceRepository _devices = Substitute.For<IUserPushDeviceRepository>();
    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();
    private readonly FirebaseCredentialProtector _protector = new(
        new EphemeralDataProtectionProvider(), NullLogger<FirebaseCredentialProtector>.Instance);

    private const string ProjectNotFound = """
        {"error":{"code":404,"status":"NOT_FOUND","message":"Requested entity was not found."}}
        """;

    private const string TokenUnregistered = """
        {"error":{"code":404,"status":"NOT_FOUND","message":"Requested entity was not found.",
        "details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError",
        "errorCode":"UNREGISTERED"}]}}
        """;

    [Fact]
    public async Task ProjectLevelNotFound_DoesNotPruneTheToken()
    {
        await SendAgainst(HttpStatusCode.NotFound, ProjectNotFound);

        _scopes.DidNotReceive().CreateScope();
    }

    /// <summary>A 400 that blames a payload field is about the message, not about this registration.</summary>
    // Every device of every user gets the same payload shape, so pruning on a payload-level
    // INVALID_ARGUMENT would delete the whole fleet on one bad message.
    private const string PayloadFieldInvalid = """
        {"error":{"code":400,"status":"INVALID_ARGUMENT","message":"Invalid JSON payload",
        "details":[{"@type":"type.googleapis.com/google.rpc.BadRequest",
        "fieldViolations":[{"field":"message.android.ttl","description":"Invalid value"}]}]}}
        """;

    private const string TokenFieldInvalid = """
        {"error":{"code":400,"status":"INVALID_ARGUMENT","message":"The registration token is not valid",
        "details":[{"@type":"type.googleapis.com/google.rpc.BadRequest",
        "fieldViolations":[{"field":"message.token","description":"Invalid registration token"}]}]}}
        """;

    /// <summary>Reaching the prune path, which is as far as this harness can see; the row it deletes is pinned in Postgres.</summary>
    [Fact]
    public async Task UnregisteredToken_ReachesThePrunePath()
    {
        await SendAgainst(HttpStatusCode.NotFound, TokenUnregistered);

        _scopes.Received(1).CreateScope();
    }

    [Fact]
    public async Task PayloadLevelBadRequest_DoesNotPruneTheToken()
    {
        await SendAgainst(HttpStatusCode.BadRequest, PayloadFieldInvalid);

        _scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task ABadRequestNamingTheTokenField_ReachesThePrunePath()
    {
        await SendAgainst(HttpStatusCode.BadRequest, TokenFieldInvalid);

        _scopes.Received(1).CreateScope();
    }

    [Fact]
    public async Task ServerError_DoesNotPruneTheToken()
    {
        await SendAgainst(HttpStatusCode.ServiceUnavailable, "upstream unavailable");

        _scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task ProjectIdOutsideTheAllowedShape_IsNotSent()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        await Sut(handler, "../../v1/projects/other").SendToUserAsync("u1", Notification());

        Assert.Empty(handler.SentTo);
    }

    private async Task SendAgainst(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        await Sut(handler, "demo-project").SendToUserAsync("u1", Notification());

        Assert.Contains(handler.SentTo, u => u.Contains("fcm.googleapis.com", StringComparison.Ordinal));
    }

    private FcmMobilePushSender Sut(StubHandler handler, string projectId)
    {
        _settings.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new FirebaseSettings
        {
            Id = FirebaseSettings.SingletonId,
            ProjectId = projectId,
            ServiceAccountJson = _protector.Protect(ServiceAccountJson()),
            IsConfigured = true
        });

        _devices.GetActiveByUserIdAsync("u1", Arg.Any<CancellationToken>()).Returns([
            new UserPushDevice { Id = Guid.NewGuid(), UserId = "u1", Platform = "ios", PushToken = "tok" }
        ]);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("fcm").Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new FcmMobilePushSender(
            _settings,
            _devices,
            _scopes,
            _protector,
            new FcmAccessTokenProvider(factory, NullLogger<FcmAccessTokenProvider>.Instance),
            factory,
            NullLogger<FcmMobilePushSender>.Instance);
    }

    private static NotificationItemDto Notification() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Incident opened",
        Message = "api-gateway is down"
    };

    internal static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n");
        return $$"""
            {"project_id":"demo-project","client_email":"svc@demo.iam.gserviceaccount.com","private_key":"{{pem}}"}
            """;
    }

    /// <summary>Answers the OAuth exchange, then the FCM send with the response under test.</summary>
    internal sealed class StubHandler(HttpStatusCode sendStatus, string sendBody) : HttpMessageHandler
    {
        public List<string> SentTo { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("oauth2.googleapis.com", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"at","expires_in":3600}""", Encoding.UTF8, "application/json")
                });

            SentTo.Add(url);
            return Task.FromResult(new HttpResponseMessage(sendStatus)
            {
                Content = new StringContent(sendBody, Encoding.UTF8, "application/json")
            });
        }
    }
}

public class FcmAccessTokenProviderTests
{
    [Fact]
    public async Task TokenUri_OutsideGoogle_IsRefusedBeforeTheAssertionIsSigned()
    {
        var handler = new FcmMobilePushSenderTests.StubHandler(HttpStatusCode.OK, "{}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sut(handler).ProbeAsync(ServiceAccount("https://attacker.example/token"), CancellationToken.None));

        Assert.Contains("token_uri", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.SentTo);
    }

    [Fact]
    public async Task TokenUri_Absent_FallsBackToGoogle()
    {
        var handler = new FcmMobilePushSenderTests.StubHandler(HttpStatusCode.OK, "{}");

        await Sut(handler).ProbeAsync(ServiceAccount(tokenUri: null), CancellationToken.None);

        Assert.Empty(handler.SentTo);
    }

    private static FcmAccessTokenProvider Sut(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("fcm").Returns(_ => new HttpClient(handler, disposeHandler: false));
        return new FcmAccessTokenProvider(factory, NullLogger<FcmAccessTokenProvider>.Instance);
    }

    private static string ServiceAccount(string? tokenUri)
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n");
        var uri = tokenUri is null ? "" : $$""","token_uri":"{{tokenUri}}" """;
        return $$"""
            {"project_id":"demo-project","client_email":"svc@demo.iam.gserviceaccount.com","private_key":"{{pem}}"{{uri}}}
            """;
    }
}
