namespace Callu.Infrastructure.Audit;

/// <summary>The actor every audit row written inside the scope is attributed to when the caller names none.</summary>
// An inbound webhook opens its incident through the incident service, which reads the actor off the
// request principal; a webhook has no principal, so without this the row would say the system did it.
// Ambient for the same reason as the authorization scope: the write happens several calls down.
public static class AuditActorScope
{
    private static readonly AsyncLocal<string?> Ambient = new();

    public static string? Current => Ambient.Value;

    public static IDisposable Begin(string actorId)
    {
        var previous = Ambient.Value;
        Ambient.Value = actorId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _restored;

        public void Dispose()
        {
            if (_restored) return;
            _restored = true;
            Ambient.Value = previous;
        }
    }
}
