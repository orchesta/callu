using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Callu.Domain.Entities;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>The carrier callu-voice is given, and the fingerprint taken over exactly the fields sent.</summary>
// One type on purpose: the body and the digest are built from the same properties, so the two
// sides cannot drift into computing different answers for the same carrier.
public sealed record CalluVoiceTrunk
{
    /// <summary>Version tag, so a change to what the fingerprint covers cannot be mistaken for a match.</summary>
    private const string Version = "callu-voice/trunk/v1";

    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string AuthUsername { get; init; } = string.Empty;
    public string CallerId { get; init; } = string.Empty;
    public string Transport { get; init; } = string.Empty;

    /// <summary>No carrier at all — what an explicit removal sends.</summary>
    public static readonly CalluVoiceTrunk None = new();

    /// <summary>The carrier a trunk row describes; a switched-off row is no carrier.</summary>
    public static CalluVoiceTrunk From(SipTrunkSettings? trunk, string password) =>
        trunk is null || !trunk.IsEnabled
            ? None
            : new CalluVoiceTrunk
            {
                Enabled = true,
                Host = Clean(trunk.Server),
                Port = trunk.Port,
                Username = Clean(trunk.Username),
                Password = Clean(password),
                AuthUsername = Clean(trunk.AuthUser),
                CallerId = Clean(trunk.CallerId),
                Transport = TransportFor(trunk),
            };

    /// <summary>The transport callu-voice is asked to use, mirroring what the panel displays.</summary>
    public static string TransportFor(SipTrunkSettings trunk) =>
        trunk.UseTls ? "tls" : trunk.UseTcp ? "tcp" : "udp";

    /// <summary>The request body, whose property names are callu-voice's own.</summary>
    // register, dtmf_mode and codecs are left unstated: Callu has no field for them, and naming
    // them would reset a keypress mode somebody tuned for their carrier.
    public object ToRequestBody() => Enabled
        ? new
        {
            enabled = true,
            host = Host,
            port = Port,
            username = Username,
            password = Password,
            auth_username = AuthUsername,
            caller_id = CallerId,
            transport = Transport,
        }
        : new { enabled = false };

    /// <summary>Identifies this carrier without disclosing it; callu-voice reports the same digest over what it was sent.</summary>
    public string Fingerprint()
    {
        if (!Enabled)
            return Digest($"{Version}\nenabled=false");

        var canonical = string.Join('\n',
            Version,
            "enabled=true",
            $"host={Host}",
            $"port={Port.ToString(CultureInfo.InvariantCulture)}",
            $"username={Username}",
            $"auth_username={AuthUsername}",
            $"caller_id={CallerId}",
            $"transport={Transport}",
            $"password={Digest(Password)}");

        return Digest(canonical);
    }

    // Asterisk strips whitespace off a config value anyway, so an untrimmed host would be stored
    // as one thing and dialled as another — and hash as a third.
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string Digest(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
