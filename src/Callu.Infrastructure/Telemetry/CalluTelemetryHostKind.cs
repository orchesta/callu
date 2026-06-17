namespace Callu.Infrastructure.Telemetry;

/// <summary>
/// Selects host-specific OpenTelemetry instrumentation (API vs background worker).
/// </summary>
public enum CalluTelemetryHostKind
{
    Api,
    Worker
}
