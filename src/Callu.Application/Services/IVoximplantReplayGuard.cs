namespace Callu.Application.Services;

/// <summary>
/// Replay protection for VoxEngine callbacks: reject any (timestamp, nonce) pair seen
/// twice inside <see cref="WindowSeconds"/>. Callers must also reject requests whose
/// timestamp is outside that window.
/// </summary>
public interface IVoximplantReplayGuard
{
    /// <summary>Returns false if the nonce was already seen in the window, true otherwise.</summary>
    bool TryRegister(long unixTimestampSeconds, string nonce);

    int WindowSeconds { get; }
}

public sealed class VoximplantReplayGuardOptions
{
    /// <summary>Allowed clock skew between VoxEngine and the server, in seconds.</summary>
    public int WindowSeconds { get; set; } = 300;
}
