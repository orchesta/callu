using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Builds the channel service with every dependency substituted, for tests about what it emits.</summary>
internal static class ChannelServiceHarness
{
    public static NotificationChannelService New() => new(
        Substitute.For<IRepository<NotificationChannel>>(),
        Substitute.For<IRepository<NotificationChannelDelivery>>(),
        Substitute.For<IUnitOfWork>(),
        Substitute.For<IHttpClientFactory>(),
        Substitute.For<IEmailService>(),
        new ProviderSecretProtector(
            new EphemeralDataProtectionProvider(), NullLogger<ProviderSecretProtector>.Instance),
        Substitute.For<IOrganizationSettingsService>(),
        Options.Create(new CommunicationSettingsOptions()),
        Substitute.For<IAuditLogService>(),
            NullLogger<NotificationChannelService>.Instance);
}

/// <summary>What each channel actually puts on the wire, and what the editor promises it will.</summary>
public class NotificationChannelPayloadTests
{
    private static readonly Guid IncidentId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    public static TheoryData<NotificationChannelDispatchEvent> AllEvents() =>
        new(Enum.GetValues<NotificationChannelDispatchEvent>());

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void EveryEvent_CarriesTheIncidentIdAndTheLink(NotificationChannelDispatchEvent ev)
    {
        var message = NotificationChannelService.BuildHumanMessage(
            ev, "Critical", "Payment API is returning 5xx", IncidentId, "https://callu.example.com/incidents/" + IncidentId);

        Assert.Contains(IncidentId.ToString(), message, StringComparison.Ordinal);
        Assert.Contains($"https://callu.example.com/incidents/{IncidentId}", message, StringComparison.Ordinal);
        Assert.Contains("Payment API is returning 5xx", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The id survives a missing link. A base URL nobody configured is common on a fresh install, and
    /// an operator holding a message with no identifier at all cannot find the incident by any route.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoLink_TheIdIsStillThere(string? incidentUrl)
    {
        var message = NotificationChannelService.BuildHumanMessage(
            NotificationChannelDispatchEvent.IncidentCreated, "High", "Disk full", IncidentId, incidentUrl);

        Assert.Contains($"ID: {IncidentId}", message, StringComparison.Ordinal);
        Assert.DoesNotContain("http", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A base URL that is not a usable http(s) address must produce no link rather than a broken one.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-a-url", null)]
    [InlineData("ftp://callu.example.com", null)]
    [InlineData("https://callu.example.com", "https://callu.example.com/incidents/")]
    [InlineData("https://callu.example.com/", "https://callu.example.com/incidents/")]
    [InlineData("https://callu.example.com/callu/", "https://callu.example.com/callu/incidents/")]
    public void OnlyAUsableBaseUrl_BecomesALink(string? baseUrl, string? expectedPrefix)
    {
        var url = NotificationChannelService.BuildIncidentUrl(baseUrl, IncidentId);

        if (expectedPrefix is null)
        {
            Assert.Null(url);
            return;
        }

        Assert.Equal(expectedPrefix + IncidentId, url);
    }

    // ── The payloads themselves ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Slack_SendsPlainTextAndOmitsUnsetOptionalFields()
    {
        var payload = NotificationChannelService.BuildSlackPayload([], "hello");

        Assert.Equal("hello", payload["text"]);
        Assert.DoesNotContain("channel", payload.Keys);
        Assert.DoesNotContain("username", payload.Keys);
        Assert.DoesNotContain("icon_emoji", payload.Keys);
    }

    [Fact]
    public void Slack_PassesTheConfiguredOverridesThrough()
    {
        var payload = NotificationChannelService.BuildSlackPayload(
            new Dictionary<string, string>
            {
                ["channel"] = "#incidents",
                ["username"] = "Callu",
                ["iconEmoji"] = ":rotating_light:",
            },
            "hello");

        Assert.Equal("#incidents", payload["channel"]);
        Assert.Equal("Callu", payload["username"]);
        Assert.Equal(":rotating_light:", payload["icon_emoji"]);
    }

    [Fact]
    public void Teams_SendsAMessageCardCarryingTheText()
    {
        var payload = NotificationChannelService.BuildTeamsPayload("hello");

        Assert.Equal("MessageCard", payload["@type"]);
        var section = Assert.IsType<Dictionary<string, object>>(Assert.IsType<object[]>(payload["sections"])[0]);
        Assert.Equal("hello", section["text"]);
    }

    // ── The editor's preview is the same bytes, not a second implementation ────────────────────────

    /// <summary>
    /// The preview exists so nobody has to save a channel to find out what it sends. It is only worth
    /// having while it is produced by the code that sends — a second formatter would agree today and
    /// drift on the first change to the message.
    /// </summary>
    [Theory]
    [InlineData(NotificationChannelType.Slack)]
    [InlineData(NotificationChannelType.MicrosoftTeams)]
    [InlineData(NotificationChannelType.Webhook)]
    [InlineData(NotificationChannelType.Email)]
    public void EveryTypesSample_IsValidJsonAndCarriesTheSampleIncident(NotificationChannelType type)
    {
        var json = ChannelServiceHarness.New().BuildSamplePayloadJson(type);

        using var parsed = JsonDocument.Parse(json);
        Assert.Contains(IncidentId.ToString(), json, StringComparison.Ordinal);
        Assert.Contains("Payment API is returning 5xx", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSlackSample_IsTheShapeSlackReceives()
    {
        var json = ChannelServiceHarness.New().BuildSamplePayloadJson(NotificationChannelType.Slack);

        using var parsed = JsonDocument.Parse(json);
        var text = parsed.RootElement.GetProperty("text").GetString();

        Assert.Equal(
            NotificationChannelService.BuildHumanMessage(
                NotificationChannelDispatchEvent.IncidentCreated,
                "Critical",
                "Payment API is returning 5xx",
                IncidentId,
                $"https://callu.example.com/incidents/{IncidentId}"),
            text);
    }

    [Fact]
    public void TheTeamsSample_IsAMessageCard()
    {
        var json = ChannelServiceHarness.New().BuildSamplePayloadJson(NotificationChannelType.MicrosoftTeams);

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("MessageCard", parsed.RootElement.GetProperty("@type").GetString());
    }
}
