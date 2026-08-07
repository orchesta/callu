using System.Text.Json;
using Callu.Api.Controllers;
using Callu.Application.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Callu.Tests;

/// <summary>The channel editor is driven entirely by this endpoint, so what it says is what the operator believes.</summary>
public class NotificationChannelTypeDefinitionTests
{
    private static JsonElement ReadTypes()
    {
        var controller = new NotificationChannelsController(
            ChannelServiceHarness.New(),
            Substitute.For<INotificationPushService>());

        var payload = Assert.IsType<OkObjectResult>(controller.GetChannelTypes()).Value;
        return JsonSerializer.SerializeToElement(payload);
    }

    private static JsonElement TypeNamed(string value) =>
        ReadTypes().EnumerateArray().Single(e => e.GetProperty("value").GetString() == value);

    /// <summary>
    /// Every type fires on the five lifecycle events the operator toggles right below the description.
    /// A description promising only one of them contradicts the switches on the same screen.
    /// </summary>
    [Theory]
    [InlineData("Slack")]
    [InlineData("MicrosoftTeams")]
    [InlineData("Email")]
    [InlineData("Webhook")]
    public void NoTypeDescription_ClaimsItOnlyFiresOnCreation(string value)
    {
        var description = TypeNamed(value).GetProperty("description").GetString() ?? string.Empty;

        Assert.DoesNotContain("when a new incident is created", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("on new incident creation", description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Slack")]
    [InlineData("MicrosoftTeams")]
    [InlineData("Email")]
    [InlineData("Webhook")]
    public void EveryType_OffersFieldsAndASample(string value)
    {
        var type = TypeNamed(value);

        Assert.NotEmpty(type.GetProperty("fields").EnumerateArray());

        var sample = type.GetProperty("samplePayload").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sample));
        using var parsed = JsonDocument.Parse(sample!);
    }

    /// <summary>
    /// Teams runs on the Office 365 connector webhook, which is being retired upstream. The day it
    /// stops accepting posts, Teams notifications go silent with no change on our side — so the
    /// warning belongs where the operator configures it, not only in the docs.
    /// </summary>
    [Fact]
    public void Teams_CarriesTheConnectorRetirementNotice()
    {
        var notice = TypeNamed("MicrosoftTeams").GetProperty("notice");

        Assert.Equal("warning", notice.GetProperty("level").GetString());
        Assert.Contains("retiring", notice.GetProperty("text").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Slack ignores channel, username and icon overrides on webhooks created through an app — its own
    /// documentation says so — so the editor must not offer fields the transport will drop on the floor.
    /// </summary>
    [Fact]
    public void Slack_OffersOnlyTheWebhookUrl_AndSaysWhyTheChannelIsFixed()
    {
        var slack = TypeNamed("Slack");

        var keys = slack.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("key").GetString())
            .ToList();
        Assert.Equal(["webhookUrl"], keys);

        var notice = slack.GetProperty("notice");
        Assert.Equal("info", notice.GetProperty("level").GetString());
        Assert.Contains("ignores", notice.GetProperty("text").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The notice is the exception, not the pattern: one on every type is one nobody reads.</summary>
    [Theory]
    [InlineData("Email")]
    [InlineData("Webhook")]
    public void NoOtherType_CarriesANotice(string value)
    {
        Assert.False(TypeNamed(value).TryGetProperty("notice", out _));
    }
}
