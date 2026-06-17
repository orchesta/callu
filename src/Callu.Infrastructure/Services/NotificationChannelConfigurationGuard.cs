using Callu.Domain.Entities;
using Callu.Infrastructure.Utilities;
using Callu.Shared.Exceptions;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Central rules for notification channel <see cref="NotificationChannel.ConfigurationJson"/> (create + update).
/// Mirrors API FluentValidation so direct service calls cannot persist invalid config.
/// </summary>
internal static class NotificationChannelConfigurationGuard
{
    private const string ConfigurationKey = "Configuration";

    internal static void EnsureValid(
        NotificationChannelType type,
        IReadOnlyDictionary<string, string> configuration,
        bool notifyOnCreated,
        bool notifyOnAcknowledged,
        bool notifyOnResolved)
    {
        if (!notifyOnCreated && !notifyOnAcknowledged && !notifyOnResolved)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Lifecycle"] = ["Select at least one lifecycle trigger (created, acknowledged, or resolved)."],
            });
        }

        var failures = new List<(string Property, string Message)>();

        switch (type)
        {
            case NotificationChannelType.Slack:
            case NotificationChannelType.MicrosoftTeams:
                RequireHttpUrl(configuration, "webhookUrl", failures);
                break;
            case NotificationChannelType.Email:
                if (!configuration.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
                    failures.Add((ConfigurationKey, "Configuration 'to' (email address) is required."));
                else if (!to.Contains('@'))
                    failures.Add((ConfigurationKey, "Configuration 'to' must be a valid email address."));
                break;
            case NotificationChannelType.Webhook:
                RequireHttpUrl(configuration, "url", failures);
                if (configuration.TryGetValue("method", out var m) &&
                    !string.IsNullOrWhiteSpace(m) &&
                    !m.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                    !m.Equals("PUT", StringComparison.OrdinalIgnoreCase))
                    failures.Add((ConfigurationKey, "Configuration 'method' must be POST or PUT."));
                break;
            default:
                failures.Add((ConfigurationKey, "Unsupported channel type."));
                break;
        }

        if (failures.Count == 0) return;

        var dict = failures
            .GroupBy(f => f.Property)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Message).Distinct().ToArray());

        throw new ValidationException(dict);
    }

    private static void RequireHttpUrl(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        List<(string Property, string Message)> failures)
    {
        if (!configuration.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
        {
            failures.Add((ConfigurationKey, $"{key} is required."));
            return;
        }

        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add((ConfigurationKey, $"{key} must be a valid http(s) URL."));
            return;
        }

        if (!UrlSanitizer.IsValidHealthCheckUrl(v))
            failures.Add((ConfigurationKey, $"{key} must point to an external host (internal/loopback/metadata addresses are not allowed)."));
    }
}
