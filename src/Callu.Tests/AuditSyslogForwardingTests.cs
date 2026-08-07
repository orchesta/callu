using System.Net;
using System.Net.Sockets;
using System.Text;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Callu.Tests;

/// <summary>The CEF line a collector parses, and the frame it is delivered in.</summary>
public class AuditCefFormatterTests
{
    private static readonly DateTime At = new(2026, 7, 29, 10, 30, 0, DateTimeKind.Utc);

    private static AuditLog Entry(AuditAction action = AuditAction.Login, string? description = null) => new()
    {
        Id = new Guid("11111111-2222-3333-4444-555555555555"),
        CreatedAt = At,
        ActorId = "u-1",
        ActorDisplayName = "Ada Lovelace",
        Action = action,
        ResourceType = "User",
        ResourceId = new Guid("66666666-7777-8888-9999-000000000000"),
        Summary = description,
        RequestIpAddress = "203.0.113.7",
        Sequence = 42,
    };

    private static AuditSyslogOptions Options() => new() { Host = "collector", Facility = 13, AppName = "callu" };

    // ---- the CEF message ---------------------------------------------------

    [Fact]
    public void HasTheSevenHeaderFieldsInOrder()
    {
        var message = AuditCefFormatter.Message(Entry(), "1.0.0");

        var header = message.Split('|');
        Assert.Equal("CEF:0", header[0]);
        Assert.Equal("Callu", header[1]);
        Assert.Equal("Callu", header[2]);
        Assert.Equal("1.0.0", header[3]);
        Assert.Equal("Login", header[4]);
        Assert.Equal("Login User", header[5]);
    }

    [Fact]
    public void CarriesTheActorTheAddressAndTheEntity()
    {
        var message = AuditCefFormatter.Message(Entry(), "1.0.0");

        Assert.Contains("suser=Ada Lovelace", message);
        Assert.Contains("suid=u-1", message);
        Assert.Contains("src=203.0.113.7", message);
        Assert.Contains("cs1=User", message);
        Assert.Contains("cs2=66666666-7777-8888-9999-000000000000", message);
    }

    /// <summary>Without the sequence a reviewer cannot tie a forwarded line back to the chain.</summary>
    [Fact]
    public void CarriesTheChainSequence()
    {
        Assert.Contains("cs3=42", AuditCefFormatter.Message(Entry(), "1.0.0"));
    }

    [Fact]
    public void LeavesOutWhatTheEntryDoesNotHave()
    {
        var bare = new AuditLog { Id = Guid.NewGuid(), CreatedAt = At, Action = AuditAction.Login, ResourceType = "User" };

        var message = AuditCefFormatter.Message(bare, "1.0.0");

        Assert.DoesNotContain("suser=", message);
        Assert.DoesNotContain("src=", message);
    }

    /// <summary>An unescaped separator inside a value would move every field after it.</summary>
    [Fact]
    public void EscapesTheSeparatorsThatWouldSplitAFieldInTwo()
    {
        Assert.Equal("a\\=b", AuditCefFormatter.ExtensionValue("a=b"));
        Assert.Equal("a\\\\b", AuditCefFormatter.ExtensionValue("a\\b"));
        Assert.Equal("a\\|b", AuditCefFormatter.Header("a|b"));
        Assert.Equal("a\\\\b", AuditCefFormatter.Header("a\\b"));
    }

    /// <summary>A newline in operator-entered text would end the frame early.</summary>
    [Fact]
    public void TurnsANewlineIntoAnEscapeRatherThanEndingTheLine()
    {
        var message = AuditCefFormatter.Message(Entry(description: "first\nsecond"), "1.0.0");

        Assert.Contains("msg=first\\nsecond", message);
        Assert.DoesNotContain('\n', message);
    }

    [Fact]
    public void RanksTheSecurityRelevantEntriesAboveOrdinaryActivity()
    {
        int SeverityOf(AuditAction action) =>
            int.Parse(AuditCefFormatter.Message(Entry(action), "1.0.0").Split('|')[6]);

        Assert.True(SeverityOf(AuditAction.IntegrityBroken) > SeverityOf(AuditAction.LoginFailed));
        Assert.True(SeverityOf(AuditAction.LoginFailed) > SeverityOf(AuditAction.Viewed));
        Assert.True(SeverityOf(AuditAction.RoleAssigned) > SeverityOf(AuditAction.Created));
    }

    // ---- the syslog frame --------------------------------------------------

    [Fact]
    public void OpensWithThePriorityComputedFromTheFacility()
    {
        var frame = AuditCefFormatter.Frame(Entry(), Options(), "callu-api", "1.0.0");

        // facility 13 (log audit) * 8 + severity 6 (informational)
        Assert.StartsWith("<110>1 ", frame);
    }

    [Fact]
    public void CarriesTheTimestampTheHostAndTheAppName()
    {
        var frame = AuditCefFormatter.Frame(Entry(), Options(), "callu-api", "1.0.0");

        Assert.Contains("2026-07-29T10:30:00.000000Z", frame);
        Assert.Contains(" callu-api callu ", frame);
    }

    [Fact]
    public void PutsTheCefMessageAfterTheHeader()
    {
        var frame = AuditCefFormatter.Frame(Entry(), Options(), "callu-api", "1.0.0");

        Assert.Contains("CEF:0|Callu|Callu|", frame);
        Assert.Contains("AUDIT", frame);
    }

    /// <summary>A host name with a space would be read as the next syslog field.</summary>
    [Fact]
    public void KeepsTheHostNameToOneField()
    {
        var frame = AuditCefFormatter.Frame(Entry(), Options(), "my host", "1.0.0");

        Assert.Contains(" my_host ", frame);
    }
}

/// <summary>What the forwarder does with entries when the collector is or is not there.</summary>
public class AuditSyslogSinkTests
{
    private static AuditLog Entry() => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        Action = AuditAction.Login,
        ResourceType = "User",
        ActorDisplayName = "Ada",
    };

    private static AuditSyslogSink Sink(AuditSyslogOptions options) =>
        new(Options.Create(options), NullLogger<AuditSyslogSink>.Instance);

    [Fact]
    public void EmitDoesNothingWhenNoCollectorIsConfigured()
    {
        using var sink = Sink(new AuditSyslogOptions());

        var error = Record.Exception(() => sink.Emit(Entry()));

        Assert.Null(error);
    }

    /// <summary>The audited operation has already succeeded; the forwarder must never be why it fails.</summary>
    [Fact]
    public void EmitDoesNotThrowWhenTheCollectorIsUnreachable()
    {
        using var sink = Sink(new AuditSyslogOptions { Host = "127.0.0.1", Port = 1, UseTls = false });

        var error = Record.Exception(() =>
        {
            for (var i = 0; i < 100; i++) sink.Emit(Entry());
        });

        Assert.Null(error);
    }

    /// <summary>A collector that stays down must not grow the queue without bound; what no longer
    /// fits is dropped and counted, rather than held until the host runs out of memory.</summary>
    [Fact]
    public void EmitDropsAndCountsOnceTheQueueIsFull()
    {
        using var sink = Sink(new AuditSyslogOptions { Host = "127.0.0.1", Port = 1, UseTls = false, QueueCapacity = 8 });

        for (var i = 0; i < 1_000; i++) sink.Emit(Entry());

        Assert.Equal(1_000 - 8, sink.DroppedCount);
    }

    [Fact]
    public void NothingIsDroppedWhileTheQueueHasRoom()
    {
        using var sink = Sink(new AuditSyslogOptions { Host = "127.0.0.1", Port = 1, UseTls = false, QueueCapacity = 64 });

        for (var i = 0; i < 8; i++) sink.Emit(Entry());

        Assert.Equal(0, sink.DroppedCount);
    }

    [Fact]
    public async Task DeliversAnEntryToAListeningCollector()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepted = listener.AcceptTcpClientAsync();

        using var sink = Sink(new AuditSyslogOptions
        {
            Host = "127.0.0.1",
            Port = port,
            UseTls = false,
            AppName = "callu",
        });

        await sink.StartAsync(CancellationToken.None);
        sink.Emit(Entry());

        using var client = await accepted.WaitAsync(TimeSpan.FromSeconds(10));
        var received = await ReadFrameAsync(client.GetStream());

        await sink.StopAsync(CancellationToken.None);
        listener.Stop();

        Assert.Contains("CEF:0|Callu|Callu|", received);
        Assert.Contains("suser=Ada", received);

        // Octet counting: the frame is prefixed with its byte length and a space.
        var space = received.IndexOf(' ');
        var declared = int.Parse(received[..space]);
        Assert.Equal(declared, Encoding.UTF8.GetByteCount(received[(space + 1)..]));
    }

    /// <summary>Reads one octet-counted frame, however TCP happened to segment it.</summary>
    private static async Task<string> ReadFrameAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total))
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            if (read == 0) break;

            total += read;
            var text = Encoding.UTF8.GetString(buffer, 0, total);
            var space = text.IndexOf(' ');

            if (space > 0 && int.TryParse(text[..space], out var declared)
                && Encoding.UTF8.GetByteCount(text[(space + 1)..]) >= declared)
                return text;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}
