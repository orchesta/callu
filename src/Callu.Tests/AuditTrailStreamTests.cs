using System.Text.Json;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Callu.Tests;

/// <summary>The audit copy an operator's log collector reads.</summary>
public sealed class AuditTrailStreamTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"callu-audit-{Guid.NewGuid():N}");

    private string Path_ => System.IO.Path.Combine(_directory, "audit-.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    private static AuditLog Entry(AuditAction action = AuditAction.Login, string? user = "u1") => new()
    {
        Id = Guid.NewGuid(),
        Action = action,
        ResourceType = "User",
        ResourceId = Guid.NewGuid(),
        ActorId = user,
        ActorDisplayName = "Ada",
        Summary = "signed in",
        RequestIpAddress = "203.0.113.7",
        CreatedAt = DateTime.UtcNow,
    };

    private Serilog.Core.Logger Build(string? path)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AuditTrailStreamOptions.SectionName}:Path"] = path,
            })
            .Build();

        return new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteToAuditTrailStream(configuration)
            .CreateLogger();
    }

    private List<JsonElement> ReadLines()
    {
        if (!Directory.Exists(_directory)) return [];

        return [.. Directory.EnumerateFiles(_directory)
            .SelectMany(File.ReadAllLines)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];
    }

    [Fact]
    public void WritesOneJsonObjectPerAuditEntry()
    {
        using var serilog = Build(Path_);
        using var factory = new SerilogLoggerFactory(serilog);

        new AuditTrailStream(factory).Emit(Entry());
        serilog.Dispose();

        var lines = ReadLines();
        Assert.Single(lines);
        Assert.Equal("Login", lines[0].GetProperty("Audit").GetProperty("Action").GetString());
        Assert.Equal("Ada", lines[0].GetProperty("Audit").GetProperty("ActorDisplayName").GetString());
        Assert.Equal("203.0.113.7", lines[0].GetProperty("Audit").GetProperty("RequestIpAddress").GetString());
    }

    /// <summary>The hosts run at Warning; an Information-level audit entry must survive that.</summary>
    [Fact]
    public void IsNotFilteredOutByTheHostMinimumLevel()
    {
        using var serilog = Build(Path_);
        using var factory = new SerilogLoggerFactory(serilog);

        new AuditTrailStream(factory).Emit(Entry());
        serilog.Dispose();

        Assert.Single(ReadLines());
    }

    /// <summary>A collector pointed at this file must see audit entries and nothing else.</summary>
    [Fact]
    public void CarriesNothingButAuditEntries()
    {
        using var serilog = Build(Path_);
        using var factory = new SerilogLoggerFactory(serilog);

        factory.CreateLogger("Callu.Something.Else").LogError("a database went away");
        factory.CreateLogger("Microsoft.AspNetCore").LogWarning("a request was slow");
        new AuditTrailStream(factory).Emit(Entry(AuditAction.RoleAssigned));
        serilog.Dispose();

        var lines = ReadLines();
        Assert.Single(lines);
        Assert.Equal("RoleAssigned", lines[0].GetProperty("Audit").GetProperty("Action").GetString());
    }

    [Fact]
    public void WritesNothingAnywhereWhenNoPathIsConfigured()
    {
        using var serilog = Build(path: null);
        using var factory = new SerilogLoggerFactory(serilog);

        new AuditTrailStream(factory).Emit(Entry());
        serilog.Dispose();

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void KeepsEveryEntryWhenSeveralArriveTogether()
    {
        using var serilog = Build(Path_);
        using var factory = new SerilogLoggerFactory(serilog);
        var stream = new AuditTrailStream(factory);

        foreach (var action in new[] { AuditAction.Login, AuditAction.Logout, AuditAction.SettingsChanged })
            stream.Emit(Entry(action));
        serilog.Dispose();

        var actions = ReadLines()
            .Select(l => l.GetProperty("Audit").GetProperty("Action").GetString())
            .ToList();

        Assert.Equal(["Login", "Logout", "SettingsChanged"], actions);
    }
}
