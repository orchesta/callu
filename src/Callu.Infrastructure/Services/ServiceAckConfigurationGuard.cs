using Callu.Infrastructure.Utilities;
using Callu.Shared.Exceptions;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Save-time rules for a service's outbound ACK configuration.
/// Only fields the request actually carries are checked, so stored config predating these
/// rules never blocks an unrelated edit.
/// </summary>
internal static class ServiceAckConfigurationGuard
{
    internal static void EnsureValid(
        string? ackUrl, string? ackPayloadTemplate, bool allowPrivate,
        string urlProperty = "AckUrl", string templateProperty = "AckPayloadTemplate")
    {
        var failures = new List<(string Property, string Message)>();

        if (!string.IsNullOrEmpty(ackUrl) && !UrlPassesSaveTimeCheck(ackUrl, allowPrivate))
            failures.Add((urlProperty, "URL must be a valid http(s) URL pointing to an allowed host (internal/loopback/metadata addresses are not allowed)."));

        if (!string.IsNullOrEmpty(ackPayloadTemplate))
        {
            var template = Scriban.Template.Parse(ackPayloadTemplate);
            if (template.HasErrors)
            {
                var detail = string.Join("; ", template.Messages.Select(m => m.Message));
                failures.Add((templateProperty, $"Payload template does not parse: {detail}"));
            }
        }

        if (failures.Count == 0) return;

        throw new ValidationException(failures
            .GroupBy(f => f.Property)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Message).Distinct().ToArray()));
    }

    // A DNS name the resolver cannot answer for right now is accepted — the send-time guard
    // re-checks every dispatch, and a save must not fail because a resolver blinked.
    private static bool UrlPassesSaveTimeCheck(string url, bool allowPrivate)
    {
        if (UrlSanitizer.IsValidHealthCheckUrl(url, allowPrivate)) return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (uri.HostNameType != UriHostNameType.Dns) return false;
        if (!uri.Host.Any(char.IsLetter)) return false;

        try
        {
            _ = System.Net.Dns.GetHostAddresses(uri.Host);
            return false;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return true;
        }
    }
}
