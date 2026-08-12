using System.Text.RegularExpressions;
using Callu.Domain.Enums;

namespace Callu.Tests;

/// <summary>Config screens are read by fewer people than the audit trail is.</summary>
// Recording that a credential moved is the point; recording the credential would turn the trail
// into a second, less guarded copy of every secret the product holds.
public class SecretsNeverReachTheAuditTrailTests
{
    private static readonly string[] SecretHandlingServices =
    {
        "CommunicationProviderService.cs",
        "SipTrunkService.cs",
        "NotificationChannelService.cs",
        "WebhookConfigService.cs",
        "ProfileService.cs",
        "ServiceManagementService.cs",
        "ServiceActionService.cs",
    };

    /// <summary>Names that hold a credential in these services.</summary>
    private static readonly string[] SecretExpressions =
    {
        "request.Password",
        "trunk.Password",
        "entity.ConfigurationJson",
        "provider.ConfigJson",
        "request.Configuration",
        "effectiveConfig",
        "mergedConfig",
        "service.WebhookToken",
        "service.WebhookApiKey",
        "service.WebhookSecret",
        "currentPassword",
        "newPassword",
        "Serialize(dto)",
        "dto.AckSecret",
        "request.Secret",
        "action.Secret",
    };

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CalluApp.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, "solution root not found");
        return dir!.FullName;
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(SolutionRoot(), "Callu.Infrastructure", "Services", file));

    /// <summary>Every LogAsync( … ) call in a file, matched by balancing parentheses.</summary>
    private static IEnumerable<string> AuditCalls(string source)
    {
        foreach (Match m in Regex.Matches(source, @"LogAsync\("))
        {
            var i = m.Index + m.Length;
            var depth = 1;
            var inString = false;
            var start = i;

            while (i < source.Length && depth > 0)
            {
                var c = source[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '(') depth++;
                else if (c == ')') depth--;
                i++;
            }

            yield return source[start..(i - 1)];
        }
    }

    [Fact]
    public void TheScanFindsTheAuditCalls()
    {
        var total = SecretHandlingServices.Sum(f => AuditCalls(Read(f)).Count());

        Assert.True(total >= 8, $"expected the secret-handling services to audit their changes, found {total}");
    }

    [Theory]
    [InlineData("CommunicationProviderService.cs")]
    [InlineData("SipTrunkService.cs")]
    [InlineData("NotificationChannelService.cs")]
    [InlineData("WebhookConfigService.cs")]
    [InlineData("ProfileService.cs")]
    [InlineData("ServiceManagementService.cs")]
    [InlineData("ServiceActionService.cs")]
    public void NoAuditCallCarriesACredential(string file)
    {
        var offenders = AuditCalls(Read(file))
            .SelectMany(call => SecretExpressions
                .Where(secret => call.Contains(secret, StringComparison.Ordinal))
                .Select(secret => $"{file}: {secret}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "an audit row must say a secret changed, not what it changed to: " + string.Join(", ", offenders));
    }

    /// <summary>They still have to record that something changed, or the guard above is trivially met.</summary>
    [Theory]
    [InlineData("CommunicationProviderService.cs")]
    [InlineData("SipTrunkService.cs")]
    [InlineData("NotificationChannelService.cs")]
    public void EachSecretHandlingServiceStillRecordsTheChange(string file)
    {
        var source = Read(file);

        Assert.Contains($"AuditAction.{AuditAction.SettingsChanged}", source, StringComparison.Ordinal);
        Assert.Contains($"AuditAction.{AuditAction.Deleted}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookConfig_RecordsSettingsChanged_WithoutSecrets()
    {
        var source = Read("WebhookConfigService.cs");
        Assert.Contains($"AuditAction.{AuditAction.SettingsChanged}", source, StringComparison.Ordinal);
        Assert.Contains("webhookToken=rotated", source, StringComparison.Ordinal);
        Assert.DoesNotContain("newValues: service.WebhookToken", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_RecordsPasswordChanged_WithoutThePassword()
    {
        var source = Read("ProfileService.cs");
        Assert.Contains($"AuditAction.{AuditAction.PasswordChanged}", source, StringComparison.Ordinal);
    }
}
