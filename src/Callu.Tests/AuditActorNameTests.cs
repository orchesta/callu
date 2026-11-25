using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;

namespace Callu.Tests;

/// <summary>Who an audit row says acted, by name.</summary>
// A keypress on a call is recorded on a token-authenticated callback with no signed-in user. Without a
// lookup those rows carry a bare identifier — and they are the ones an auditor reads first. The signed-in
// name, in turn, belongs only to the signed-in user's own rows.
public class AuditActorNameTests
{
    private const string UserId = "11111111-2222-3333-4444-555555555555";

    private static (AuditLogService Service, List<AuditLog> Written) Build(
        HttpContext? http, IUserContactRepository? contacts)
    {
        var written = new List<AuditLog>();

        var repo = Substitute.For<IAuditLogRepository>();
        repo.AddAsync(Arg.Do<AuditLog>(written.Add), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<AuditLog>()));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);

        return (new AuditLogService(
            repo, accessor, new RunsInline(), sinks: null,
            logger: NullLogger<AuditLogService>.Instance, userContacts: contacts), written);
    }

    /// <summary>Runs the operation where it stands; the transaction itself is not what these test.</summary>
    private sealed class RunsInline : ITransactionManager
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation, CancellationToken cancellationToken = default) => operation();

        public Task ExecuteInTransactionAsync(
            Func<Task> operation, CancellationToken cancellationToken = default) => operation();

        public bool IsInTransaction() => false;
    }

    private static IUserContactRepository ContactsNaming(string displayName)
    {
        var contacts = Substitute.For<IUserContactRepository>();
        contacts.GetContactByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserContactSnapshot(UserId, displayName, "+905550000000", "a@b.c"));
        return contacts;
    }

    private static HttpContext SignedInAs(string name, string id = UserId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, id)], "test"));
        return context;
    }

    [Fact]
    public async Task AKeypressWithNobodySignedIn_StillRecordsAName()
    {
        var (service, written) = Build(http: null, ContactsNaming("On-call Responder"));

        await service.LogAsync(UserId, AuditAction.Acknowledged, "Incident", Guid.NewGuid().ToString());

        Assert.Equal("On-call Responder", Assert.Single(written).ActorDisplayName);
    }

    /// <summary>The signed-in name wins: it is who the request was actually made as.</summary>
    [Fact]
    public async Task TheSignedInNameIsPreferredOverALookup()
    {
        var (service, written) = Build(SignedInAs("Signed-in Operator"), ContactsNaming("Someone Else"));

        await service.LogAsync(UserId, AuditAction.Acknowledged, "Incident", Guid.NewGuid().ToString());

        Assert.Equal("Signed-in Operator", Assert.Single(written).ActorDisplayName);
    }

    /// <summary>A system actor has no account to look up, and that is not a failure.</summary>
    [Fact]
    public async Task ASystemActorRecordsNoName()
    {
        var (service, written) = Build(http: null, ContactsNaming("Nobody"));

        await service.LogAsync(userId: null, AuditAction.Escalated, "Incident", Guid.NewGuid().ToString());

        Assert.Null(Assert.Single(written).ActorDisplayName);
    }

    /// <summary>A system row written while an administrator's request is being served is still the system's.</summary>
    // It used to carry the administrator's name, and a sentinel has no account to look up either.
    [Fact]
    public async Task ASystemSentinelWrittenInsideASignedInRequest_HasNoDisplayName()
    {
        var contacts = ContactsNaming("Nobody");
        var (service, written) = Build(SignedInAs("Signed-in Operator"), contacts);

        await service.LogAsync("system:scheduler", AuditAction.Escalated, "Incident", Guid.NewGuid().ToString());

        Assert.Null(Assert.Single(written).ActorDisplayName);
        await contacts.DidNotReceive().GetContactByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The signed-in name is the signed-in user's; a row about someone else is looked up like any other.</summary>
    [Fact]
    public async Task ARowForADifferentUserThanTheSignedInOne_IsLookedUp_NotGivenTheSignedInName()
    {
        var (service, written) = Build(
            SignedInAs("Signed-in Operator", id: "another-user"), ContactsNaming("On-call Responder"));

        await service.LogAsync(UserId, AuditAction.Acknowledged, "Incident", Guid.NewGuid().ToString());

        Assert.Equal("On-call Responder", Assert.Single(written).ActorDisplayName);
    }

    // A name is worth having; it is never worth losing the audit row for.
    [Fact]
    public async Task ALookupThatThrows_StillWritesTheRow()
    {
        var contacts = Substitute.For<IUserContactRepository>();
        contacts.GetContactByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<UserContactSnapshot?>(_ => throw new InvalidOperationException("directory is down"));

        var (service, written) = Build(http: null, contacts);

        await service.LogAsync(UserId, AuditAction.Acknowledged, "Incident", Guid.NewGuid().ToString());

        var row = Assert.Single(written);
        Assert.Null(row.ActorDisplayName);
        Assert.Equal(UserId, row.ActorId);
    }
}
