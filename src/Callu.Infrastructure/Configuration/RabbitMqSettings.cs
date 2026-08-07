namespace Callu.Infrastructure.Configuration;

/// <summary>
/// RabbitMQ connection for the message transport. When <see cref="Host"/> is empty, messaging is disabled.
/// </summary>
public sealed class RabbitMqSettings
{
    public const string SectionName = "RabbitMQ";

    /// <summary>Hostname or Docker service name (e.g. callu-rabbitmq). Empty = no broker.</summary>
    public string? Host { get; set; }

    /// <summary>Virtual host, default "/"</summary>
    public string? VirtualHost { get; set; }

    /// <summary>AMQP port; unset means the protocol default.</summary>
    public int? Port { get; set; }

    public string? Username { get; set; }
    public string? Password { get; set; }
}
