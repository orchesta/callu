using Callu.Domain.Enums;
using Callu.Shared.Models.Audit;

namespace Callu.Tests;

public class OpenAuditEventMapperTests
{
    private static AuditLogDto Row() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        ActorId = "user-1",
        ActorDisplayName = "Ali Gören",
        ActorType = AuditActorType.User,
        Action = AuditAction.Acknowledged,
        EventName = "incident.acknowledged",
        EventCategory = "incident-management",
        Outcome = AuditOutcome.Success,
        ResourceType = "Incident",
        ResourceId = Guid.NewGuid(),
    };

    // ASP.NET's own environment names ("Production", "Development") are not lowercase, and the
    // schema's application.environment pattern requires lowercase kebab-case.
    [Theory]
    [InlineData("Production", "production")]
    [InlineData("Development", "development")]
    [InlineData("Staging", "staging")]
    public void ToEnvelope_LowercasesTheEnvironmentName(string hostEnvironment, string expected)
    {
        var envelope = OpenAuditEventMapper.ToEnvelope(Row(), "Callu", hostEnvironment);

        Assert.Equal(expected, envelope.Application.Environment);
    }

    [Fact]
    public void ToEnvelope_FallsBackToUnscopedWhenThereIsNoResourceId()
    {
        var row = Row();
        row.ResourceId = null;

        var envelope = OpenAuditEventMapper.ToEnvelope(row, "Callu", "production");

        Assert.Equal("unscoped", envelope.Resource.Id);
    }

    [Fact]
    public void ToEnvelope_FallsBackToSystemWhenThereIsNoActorId()
    {
        var row = Row();
        row.ActorId = null;

        var envelope = OpenAuditEventMapper.ToEnvelope(row, "Callu", "production");

        Assert.Equal("system", envelope.Actor.Id);
    }

    [Fact]
    public void ToEnvelope_AddsAStructuredErrorOnlyOnFailureOrPartial()
    {
        var success = Row();
        success.Outcome = AuditOutcome.Success;
        Assert.Null(OpenAuditEventMapper.ToEnvelope(success, "Callu", "production").Event.Error);

        var failure = Row();
        failure.Outcome = AuditOutcome.Failure;
        var failureEnvelope = OpenAuditEventMapper.ToEnvelope(failure, "Callu", "production");
        Assert.NotNull(failureEnvelope.Event.Error);
        Assert.Equal("Acknowledged", failureEnvelope.Event.Error!.Code);
    }

    // The chain stores base64; every hash the schema accepts is lowercase hex.
    [Fact]
    public void ToEnvelope_CarriesTheInternalChainAsHexUnderExtensions()
    {
        var row = Row();
        row.Sequence = 1;
        row.RowHash = Convert.ToBase64String(Enumerable.Repeat((byte)0xAB, 32).ToArray());
        row.PrevHash = Convert.ToBase64String(Enumerable.Repeat((byte)0xCD, 32).ToArray());

        var extensions = OpenAuditEventMapper.ToEnvelope(row, "Callu", "production").Extensions;

        Assert.Equal(string.Concat(Enumerable.Repeat("ab", 32)), extensions!["com.callu.audit.internal-chain.hash"]);
        Assert.Equal(string.Concat(Enumerable.Repeat("cd", 32)), extensions["com.callu.audit.internal-chain.previous-hash"]);
        Assert.Equal("HMAC-SHA256", extensions["com.callu.audit.internal-chain.algorithm"]);
    }

    // Keyed, and taken over a differently shaped document, so nobody outside the deployment could
    // ever recompute it. Declaring it as this event's integrity claimed a verifiability we do not have.
    [Fact]
    public void ToEnvelope_DoesNotPresentTheInternalChainHashAsTheEventDigest()
    {
        var row = Row();
        row.Sequence = 1;
        row.RowHash = Convert.ToBase64String(Enumerable.Repeat((byte)0xAB, 32).ToArray());

        var integrity = OpenAuditEventMapper.ToEnvelope(row, "Callu", "production").Integrity;

        Assert.Equal("SHA-256", integrity!.HashAlgorithm);
        Assert.Equal("RFC8785", integrity.Canonicalization);
    }

    [Fact]
    public void ToEnvelope_OmitsIntegrityWhenTheRowIsNotSealedYet()
    {
        var row = Row();
        row.RowHash = null;

        Assert.Null(OpenAuditEventMapper.ToEnvelope(row, "Callu", "production").Integrity);
    }

    // A decoded request path can hold a space; the schema's route pattern rejects one, and an event
    // that fails validation is never checked for integrity at all.
    [Theory]
    [InlineData("/api/v1/incidents/3f25", "/api/v1/incidents/3f25")]
    [InlineData("/api/v1/files/quarterly report.pdf", null)]
    [InlineData("/api/v1/search#top", null)]
    public void ToEnvelope_DropsARouteTheSchemaWouldReject(string route, string? expected)
    {
        var row = Row();
        row.RequestRoute = route;

        Assert.Equal(expected, OpenAuditEventMapper.ToEnvelope(row, "Callu", "production").Request?.Route);
    }
}
