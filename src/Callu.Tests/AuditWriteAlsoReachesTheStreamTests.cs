using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Repositories;
using Callu.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>An audited action reaches both the table and the stream, and the stream cannot stop it.</summary>
public sealed class AuditWriteAlsoReachesTheStreamTests : IDisposable
{
    private readonly ApplicationDbContext _ctx = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"audit-stream-{Guid.NewGuid():N}").Options);

    public void Dispose() => _ctx.Dispose();

    private AuditLogService Sut(ILoggerFactory factory) => new(
        new AuditLogRepository(_ctx, NullLogger<AuditLogRepository>.Instance),
        Substitute.For<IHttpContextAccessor>(),
        new SavingTransactionManager(_ctx),
        [new AuditTrailStream(factory)],
        NullLogger<AuditLogService>.Instance);

    [Fact]
    public async Task TheEntryIsWrittenToBoth()
    {
        var captured = new CapturingLoggerFactory();

        await Sut(captured).LogAsync("u1", AuditAction.RoleAssigned, "User", Guid.NewGuid().ToString());

        Assert.Single(await _ctx.AuditLogs.ToListAsync());
        Assert.Single(captured.Events);
        Assert.Equal(AuditTrailStream.SourceContext, captured.Events[0]);
    }

    [Fact]
    public async Task AStreamThatThrowsDoesNotFailTheAuditedAction()
    {
        var exploding = new CapturingLoggerFactory { Throw = true };

        await Sut(exploding).LogAsync("u1", AuditAction.Login, "User", Guid.NewGuid().ToString());

        Assert.Single(await _ctx.AuditLogs.ToListAsync());
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<string> Events { get; } = [];
        public bool Throw { get; init; }

        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class Recorder(CapturingLoggerFactory owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (owner.Throw) throw new InvalidOperationException("the sink is gone");
                owner.Events.Add(category);
            }
        }
    }
}
