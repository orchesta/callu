using Callu.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Callu.Tests;

/// <summary>Guards the startup warnings that are the only signal a misconfigured Redis gives an operator.</summary>
public class RedisConfigurationGuardTests
{
    private const string BundledActive = "callu-redis:6379,password=s3cret";

    private static IConfiguration Config(string? active) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = active })
            .Build();

    private static (Exception? Error, CapturingLogger Logger) Run(string? active)
    {
        var logger = new CapturingLogger();
        var error = Record.Exception(() => CalluRedisSetup.WarnAboutRedisConfiguration(Config(active), logger));
        return (error, logger);
    }

    [Fact]
    public void NoRedisAtAll_DoesNotThrow()
    {
        var (error, logger) = Run(active: null);

        Assert.Null(error);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ConnectionWithoutAPassword_Warns()
    {
        var (error, logger) = Run("callu-redis:6379");

        Assert.Null(error);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("NOAUTH"));
    }

    [Fact]
    public void ConnectionWithAPassword_DoesNotWarnAboutNoAuth()
    {
        var (_, logger) = Run(BundledActive);

        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("NOAUTH"));
    }

    [Fact]
    public void PasswordContainingAComma_WarnsThatItSplitIntoASecondEndpoint()
    {
        // A comma separates options in a Redis connection string and cannot be escaped, so part of
        // the password is parsed as an extra server. Warn rather than throw: the operator may have
        // configured a cluster on purpose.
        var (error, logger) = Run("callu-redis:6379,password=has,comma");

        Assert.Null(error);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("2 endpoints"));
    }

    [Fact]
    public void UnparseableConnection_DoesNotThrowHere()
    {
        // CreateMultiplexer is where a broken connection string fails loudly; this warning pass must
        // not add a second, earlier failure mode pointing at the wrong variable.
        var (error, _) = Run("::::");

        Assert.Null(error);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
