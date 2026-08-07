using Testcontainers.RabbitMq;

namespace Callu.Tests;

/// <summary>One broker container for the tests that exercise the wire, not just the database.</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    /// <summary>Pinned so a broker upgrade is a deliberate change rather than a surprise on a rebuild.</summary>
    public const string Image = "rabbitmq:4.1-alpine";

    private RabbitMqContainer? _container;

    /// <summary>The loopback address, not the name.</summary>
    // "localhost" resolves to ::1 first on some hosts, and Docker's IPv6 publish accepts the
    // connection and then resets it, which surfaces as a broker that is up but unreachable.
    public string Host => "127.0.0.1";

    public int Port { get; private set; }

    public string Username => "callu";

    public string Password => "callu-test";

    public async Task InitializeAsync()
    {
        if (DockerAvailability.IsSkipping) return;

        _container = new RabbitMqBuilder(Image)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();

        await _container.StartAsync();
        Port = _container.GetMappedPublicPort(5672);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>, ICollectionFixture<PostgresFixture>
{
    public const string Name = "rabbitmq";
}

/// <summary>Skips when Docker is unavailable and not required, like the Postgres attribute.</summary>
public sealed class RabbitMqFactAttribute : FactAttribute
{
    public RabbitMqFactAttribute()
    {
        if (DockerAvailability.IsSkipping)
            Skip = DockerAvailability.SkipReason;
    }
}
