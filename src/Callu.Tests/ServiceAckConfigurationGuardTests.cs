using Callu.Infrastructure.Services;
using Callu.Shared.Exceptions;

namespace Callu.Tests;

public class ServiceAckConfigurationGuardTests
{
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0177.0.0.1/")]
    [InlineData("ftp://example.com/")]
    [InlineData("not a url")]
    public void ADisallowedAckUrl_IsRejectedAtSaveTime(string url)
    {
        var ex = Assert.Throws<ValidationException>(() =>
            ServiceAckConfigurationGuard.EnsureValid(url, null, allowPrivate: false));

        Assert.NotNull(ex.Errors);
        Assert.Contains("AckUrl", ex.Errors.Keys);
    }

    [Fact]
    public void APublicAckUrl_IsAccepted()
    {
        ServiceAckConfigurationGuard.EnsureValid("http://93.184.216.34/ack", null, allowPrivate: false);
    }

    [Fact]
    public void WithThePrivateOptIn_AnInternalUrlIsAccepted_ButMetadataStaysBlocked()
    {
        ServiceAckConfigurationGuard.EnsureValid("http://10.0.0.5/ack", null, allowPrivate: true);

        Assert.Throws<ValidationException>(() =>
            ServiceAckConfigurationGuard.EnsureValid("http://169.254.169.254/", null, allowPrivate: true));
    }

    [Fact]
    public void ABrokenScribanTemplate_IsRejectedAtSaveTime_NotDiscoveredAtSendTime()
    {
        // Scriban forgives an unterminated `{{ incident.id`; an unclosed block it does reject.
        var ex = Assert.Throws<ValidationException>(() =>
            ServiceAckConfigurationGuard.EnsureValid(null, "{{ if incident.id }}{\"id\":\"{{ incident.id }}\"}", allowPrivate: false));

        Assert.NotNull(ex.Errors);
        Assert.Contains("AckPayloadTemplate", ex.Errors.Keys);
    }

    [Fact]
    public void AValidTemplate_IsAccepted()
    {
        ServiceAckConfigurationGuard.EnsureValid(null, """{"id": "{{incident.id}}", "state": "{{ack_type}}"}""", allowPrivate: false);
    }

    [Fact]
    public void NullAndEmptyFields_AreNotChecked_SoAPartialPutNeverTripsOnStoredConfig()
    {
        ServiceAckConfigurationGuard.EnsureValid(null, null, allowPrivate: false);
        ServiceAckConfigurationGuard.EnsureValid("", "", allowPrivate: false);
    }

    /// <summary>A DNS name the resolver cannot answer for is accepted at save; the send-time guard re-checks.</summary>
    [Fact]
    public void AnUnresolvableDnsName_IsAcceptedAtSaveTime()
    {
        ServiceAckConfigurationGuard.EnsureValid(
            "https://monitoring.acks.invalid/callback", null, allowPrivate: false);
    }
}
