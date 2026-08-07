using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Callu.Infrastructure.Messaging.Broker;

/// <summary>Reports whether the broker can be reached, by opening a channel and closing it.</summary>
public sealed class BrokerHealthCheck(ICalluBrokerConnection broker) : IHealthCheck
{
    public const string Name = "rabbitmq";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var channel = await broker.CreatePublishChannelAsync(cancellationToken);

            return channel.IsOpen
                ? HealthCheckResult.Healthy("Broker reachable")
                : HealthCheckResult.Unhealthy("Broker channel closed immediately after opening");
        }
        catch (Exception ex)
        {
            // Never "ready": the outbox holds messages while the broker is away, so the API keeps serving.
            return HealthCheckResult.Unhealthy("Broker unreachable", ex);
        }
    }
}
