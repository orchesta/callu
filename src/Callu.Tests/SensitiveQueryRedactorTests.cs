using Callu.Infrastructure.Telemetry;

namespace Callu.Tests;

/// <summary>
/// HttpClient telemetry must not record provider secrets (Voximplant api_key etc.) that ride in the
/// URL query string.
/// </summary>
public class SensitiveQueryRedactorTests
{
    [Fact]
    public void Redacts_api_key_value_keeps_other_params()
    {
        var uri = new Uri("https://api.voximplant.com/platform_api/StartScenarios?account_id=42&api_key=super-secret-123");

        var redacted = SensitiveQueryRedactor.Redact(uri);

        Assert.DoesNotContain("super-secret-123", redacted);
        Assert.Contains("account_id=42", redacted);
        Assert.Contains("api_key=REDACTED", redacted);
    }

    [Fact]
    public void HasSensitiveQuery_true_only_when_denylisted_key_present()
    {
        Assert.True(SensitiveQueryRedactor.HasSensitiveQuery(
            new Uri("https://x.test/y?api_key=abc")));
        Assert.False(SensitiveQueryRedactor.HasSensitiveQuery(
            new Uri("https://x.test/y?account_id=42")));
        Assert.False(SensitiveQueryRedactor.HasSensitiveQuery(
            new Uri("https://x.test/y")));
    }

    [Fact]
    public void Redacts_multiple_sensitive_keys_case_insensitive()
    {
        var uri = new Uri("https://x.test/y?Token=aaa&foo=bar&Signature=bbb");

        var redacted = SensitiveQueryRedactor.Redact(uri);

        Assert.DoesNotContain("aaa", redacted);
        Assert.DoesNotContain("bbb", redacted);
        Assert.Contains("foo=bar", redacted);
        Assert.Contains("Token=REDACTED", redacted);
        Assert.Contains("Signature=REDACTED", redacted);
    }

    [Fact]
    public void Redacts_the_voximplant_application_user_password()
    {
        // AddUser rides in the query string because the Management API takes no header, and this is
        // a SIP account password that outlives the trace it was written into.
        var uri = new Uri(
            "https://api.voximplant.com/platform_api/AddUser?application_id=7&user_name=callu"
            + "&user_password=Str0ng-SIP-secret&user_display_name=Callu");

        Assert.True(SensitiveQueryRedactor.HasSensitiveQuery(uri));

        var redacted = SensitiveQueryRedactor.Redact(uri);

        Assert.DoesNotContain("Str0ng-SIP-secret", redacted);
        Assert.Contains("user_password=REDACTED", redacted);
        Assert.Contains("user_name=callu", redacted);
    }

    [Theory]
    [InlineData("child_account_api_key")]
    [InlineData("auth_hash")]
    [InlineData("refresh_token")]
    [InlineData("call_token")]
    [InlineData("scenario_key")]
    public void Redacts_the_other_secret_shaped_parameters(string parameter)
    {
        var redacted = SensitiveQueryRedactor.Redact(
            new Uri($"https://x.test/y?{parameter}=leaked-value&ok=1"));

        Assert.DoesNotContain("leaked-value", redacted);
        Assert.Contains("ok=1", redacted);
    }

    [Fact]
    public void Preserves_valueless_and_no_query()
    {
        Assert.Equal("https://x.test/y", SensitiveQueryRedactor.Redact(new Uri("https://x.test/y")));

        var flag = SensitiveQueryRedactor.Redact(new Uri("https://x.test/y?debug&api_key=z"));
        Assert.Contains("debug", flag);
        Assert.Contains("api_key=REDACTED", flag);
    }

    /// <summary>
    /// The API's request log writes the raw request target — path, query and all — and it writes it at
    /// Error level for every 5xx, which is exactly when a live callback token would be in it.
    /// </summary>
    [Fact]
    public void RedactTarget_strips_the_voice_callback_token_from_a_request_line()
    {
        var target = "/api/callu-voice/callback?token=CfDJ8L-live-callback-token";

        var redacted = SensitiveQueryRedactor.RedactTarget(target);

        Assert.DoesNotContain("CfDJ8L-live-callback-token", redacted, StringComparison.Ordinal);
        Assert.Equal("/api/callu-voice/callback?token=REDACTED", redacted);
    }

    [Theory]
    [InlineData("/api/incidents", "/api/incidents")]
    [InlineData("/api/incidents?page=2", "/api/incidents?page=2")]
    [InlineData("/api/x?", "/api/x?")]
    [InlineData("/hubs/notifications?access_token=jwt", "/hubs/notifications?access_token=REDACTED")]
    [InlineData("/api/webhooks/in?apiKey=k&source=grafana", "/api/webhooks/in?apiKey=REDACTED&source=grafana")]
    public void RedactTarget_leaves_everything_that_is_not_a_secret_alone(string target, string expected) =>
        Assert.Equal(expected, SensitiveQueryRedactor.RedactTarget(target));

    /// <summary>The trace tag is the query on its own, without the '?' the request line carries.</summary>
    [Fact]
    public void RedactQuery_takes_a_query_with_or_without_its_question_mark()
    {
        Assert.Equal("token=REDACTED&x=1", SensitiveQueryRedactor.RedactQuery("token=abc&x=1"));
        Assert.Equal("token=REDACTED&x=1", SensitiveQueryRedactor.RedactQuery("?token=abc&x=1"));
    }
}
