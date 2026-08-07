using Callu.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Callu.Infrastructure.Messaging.Broker;

/// <summary>The host's single broker connection, opened on first use rather than at registration.</summary>
public interface ICalluBrokerConnection : IAsyncDisposable
{
    Task<IChannel> CreatePublishChannelAsync(CancellationToken cancellationToken);

    Task<IChannel> CreateConsumeChannelAsync(ushort prefetch, CancellationToken cancellationToken);
}

public sealed class CalluBrokerConnection(
    RabbitMqSettings settings,
    ILogger<CalluBrokerConnection> logger) : ICalluBrokerConnection
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IChannel> CreatePublishChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await ConnectAsync(cancellationToken);

        // Both flags default to false in 7.x, and with either one off every publish looks successful.
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        return await connection.CreateChannelAsync(options, cancellationToken);
    }

    public async Task<IChannel> CreateConsumeChannelAsync(ushort prefetch, CancellationToken cancellationToken)
    {
        var connection = await ConnectAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, cancellationToken);
        return channel;
    }

    private async Task<IConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            var factory = new ConnectionFactory
            {
                HostName = settings.Host!,
                UserName = settings.Username ?? "guest",
                Password = settings.Password ?? "guest",
                VirtualHost = string.IsNullOrWhiteSpace(settings.VirtualHost) ? "/" : settings.VirtualHost,
                Port = settings.Port ?? AmqpTcpEndpoint.UseDefaultPort,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = "callu",
            };

            if (_connection is not null) await _connection.DisposeAsync();

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Broker connection opened to {Host}", settings.Host);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
