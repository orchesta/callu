using System.Net;
using System.Net.Sockets;

namespace Callu.Infrastructure.Utilities;

/// <summary>
/// Validates and sanitizes URLs for health check probing.
/// Prevents SSRF attacks by blocking internal/private/metadata endpoints.
/// Covers: RFC 1918, RFC 6598 (CGNAT), loopback, link-local, cloud metadata,
/// IPv4-mapped IPv6, decimal/octal IP encoding, DNS rebinding, K8s internal DNS.
/// </summary>
public static class UrlSanitizer
{
    private static readonly string[] BlockedHosts =
    [
        "localhost",
        "metadata.google.internal",
        "metadata.azure.com",
        "metadata.google",
    ];

    private static readonly string[] BlockedTlds =
    [
        ".local",
        ".internal",
        ".svc.cluster.local",
        ".pod.cluster.local",
    ];

    private static readonly string[] BlockedIpPrefixes =
    [
        "169.254.169.254",
        "fd00:ec2::",
    ];

    /// <summary>
    /// Validates a URL for use as a health check endpoint.
    /// Blocks private IPs, loopback, cloud metadata, and non-HTTP schemes.
    /// </summary>
    public static bool IsValidHealthCheckUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;

        var host = uri.Host.ToLowerInvariant();

        if (host is "localhost" or "0.0.0.0" or "[::1]" or "[::]")
            return false;

        if (BlockedHosts.Any(h => host.Contains(h, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (BlockedTlds.Any(tld => host.EndsWith(tld, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IsDecimalOrOctalIp(host))
            return false;

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (!IsPublicIp(ip))
                return false;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            if (addresses.Length == 0)
                return false;

            if (addresses.All(a => !IsPublicIp(a)))
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a sanitized error message for blocked URLs (no sensitive info leaked).
    /// </summary>
    public static string GetBlockedReason(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "URL is empty";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Invalid URL format";

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return "Only HTTP and HTTPS schemes are allowed";

        return "URL points to a restricted or internal address";
    }

    /// <summary>
    /// Detects decimal (2130706433) and octal (0177.0.0.1) IP encoding bypasses.
    /// These resolve to internal IPs but bypass naive string-based checks.
    /// </summary>
    private static bool IsDecimalOrOctalIp(string host)
    {
        if (long.TryParse(host, out var decimalIp))
        {
            if (decimalIp >= 0 && decimalIp <= uint.MaxValue)
            {
                var bytes = BitConverter.GetBytes((uint)decimalIp);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                var ip = new IPAddress(bytes);
                return !IsPublicIp(ip);
            }
        }

        var parts = host.Split('.');
        if (parts.Length == 4 && parts.Any(p => p.Length > 1 && p.StartsWith('0') && p.All(char.IsDigit)))
        {
            try
            {
                var bytes = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    bytes[i] = (byte)Convert.ToInt32(parts[i], parts[i].StartsWith('0') ? 8 : 10);
                }
                var ip = new IPAddress(bytes);
                return !IsPublicIp(ip);
            }
            catch
            {
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an IP address is publicly routable.
    /// Handles IPv4, IPv6, and IPv4-mapped IPv6 (::ffff:127.0.0.1).
    /// Public so the health-check HttpClient can pin its connection to a vetted IP (CAS-4).
    /// </summary>
    public static bool IsPublicIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
            return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            if (bytes[0] == 10)
                return false;

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return false;

            if (bytes[0] == 192 && bytes[1] == 168)
                return false;

            if (bytes[0] == 169 && bytes[1] == 254)
                return false;

            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return false;

            if (bytes[0] == 127)
                return false;

            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback))
                return false;

            if (ip.IsIPv6LinkLocal)
                return false;

            if (ip.IsIPv6SiteLocal)
                return false;

            var bytes = ip.GetAddressBytes();

            if (bytes[0] == 0xfd || bytes[0] == 0xfc)
                return false;
        }

        var ipStr = ip.ToString();
        if (BlockedIpPrefixes.Any(prefix => ipStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
