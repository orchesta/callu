using Callu.Shared.Models.Audit;

namespace Callu.Infrastructure.Audit;

/// <summary>An authorization decision every audit row written inside the scope records.</summary>
// Ambient for the same reason as AuditCorrelationScope: an inbound webhook writes rows through
// several services, and threading the decision through each call is how one gets forgotten.
public static class AuditAuthorizationScope
{
    private static readonly AsyncLocal<AuditAuthorization?> Ambient = new();

    public static AuditAuthorization? Current => Ambient.Value;

    public static IDisposable Begin(AuditAuthorization authorization)
    {
        var previous = Ambient.Value;
        Ambient.Value = authorization;
        return new Scope(previous);
    }

    private sealed class Scope(AuditAuthorization? previous) : IDisposable
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
