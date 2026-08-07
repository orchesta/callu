using System.Text.RegularExpressions;

namespace Callu.Tests.Conventions;

/// <summary>Shape rules for the transport this repository owns, where getting it wrong loses a page silently.</summary>
public class CalluTransportGuardTests
{
    private const string MessagingDirectory = "Messaging";

    /// <summary>A dotted broker name in a string literal; a bare "callu" is a client label, not a queue.</summary>
    private const string TopologyNameLiteral = @"""callu\.[a-z]";

    private static string Read(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([SourceScanner.Root().FullName, .. relativePath]));

    private static IEnumerable<(string Name, string Text)> ProductFiles() =>
        SourceScanner.ProductFiles(includeMigrations: false)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    // ── The transport is ours, and stays ours ──────────────────────────────────────────────────────

    /// <summary>
    /// The outbox, inbox and consumer loop are written here on the bare client. Pulling a messaging
    /// framework back in would move those guarantees somewhere this repository cannot see them.
    /// </summary>
    [Fact]
    public void NoProjectReferencesAMessagingFramework()
    {
        var offenders = Directory
            .EnumerateFiles(SourceScanner.Root().FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"PackageReference\s+Include=""MassTransit"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These projects reference a messaging framework: " + string.Join(", ", offenders)
            + ". The transport is written in this repository, on RabbitMQ.Client directly.");
    }

    // ── Topology names live in one file, or two hosts declare two different brokers ────────────────

    [Fact]
    public void NoFileThatTalksToTheBroker_SpellsATopologyNameItself()
    {
        // Scoped to files that use the client: `callu.` also prefixes every metric instrument name,
        // and a metric is not a queue.
        var offenders = ProductFiles()
            .Where(f => f.Name != "CalluTopology.cs")
            .Where(f => f.Text.Contains("using RabbitMQ.Client", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(f.Text, TopologyNameLiteral))
            .Select(f => f.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files talk to the broker and spell a topology name out themselves: "
            + string.Join(", ", offenders)
            + ".\n\nOne argument's difference between the two hosts' declarations is a 406 that closes the "
            + "channel, and on the publish side the symptom is silence. The names belong to CalluTopology.");
    }

    [Fact]
    public void TheDetector_WouldSeeAStrayName()
    {
        // Control group: a guard that cannot fail protects nothing.
        Assert.Matches(TopologyNameLiteral, @"await channel.QueueDeclareAsync(""callu.incident.escalation"", durable: true);");
        Assert.DoesNotMatch(TopologyNameLiteral, @"var queue = CalluTopology.EscalationQueue;");

        // A client label is not a topology name; a metric instrument name is not one either.
        Assert.DoesNotMatch(TopologyNameLiteral, @"ClientProvidedName = ""callu"",");
        Assert.Matches(TopologyNameLiteral, @"""callu.incidents.created""");
    }

    [Fact]
    public void EveryMainQueue_DeadLettersAndNeverDropsOnItsOwn()
    {
        var topology = SourceScanner.ProductFiles(includeMigrations: false)
            .Single(f => Path.GetFileName(f) == "CalluTopology.cs");
        var declared = File.ReadAllText(topology);
        var code = SourceScanner.Code(topology);

        Assert.Contains("x-dead-letter-exchange", declared, StringComparison.Ordinal);

        // Read code rather than text: the file explains the absence of these two in prose, and a guard
        // that greps its own documentation flags the thing it is protecting.
        Assert.DoesNotContain("x-message-ttl", code, StringComparison.Ordinal);
        Assert.DoesNotContain("x-max-length", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryQueueAndExchange_IsDeclaredDurable()
    {
        var host = Read("Callu.Infrastructure", MessagingDirectory, "Consuming", "CalluConsumerHost.cs");

        var declarations = Regex.Matches(host, @"(QueueDeclareAsync|ExchangeDeclareAsync)\s*\(([^;]*?)\)\s*;",
            RegexOptions.Singleline);

        Assert.NotEmpty(declarations);
        foreach (Match declaration in declarations)
        {
            Assert.Contains("durable: true", declaration.Value, StringComparison.Ordinal);
        }
    }

    // ── Acking is the transport's business only, and it happens after the commit ───────────────────

    [Fact]
    public void OnlyTheConsumerHost_AcksOrNacksADelivery()
    {
        var offenders = ProductFiles()
            .Where(f => f.Name != "CalluConsumerHost.cs")
            .Where(f => f.Text.Contains("BasicAckAsync", StringComparison.Ordinal)
                        || f.Text.Contains("BasicNackAsync", StringComparison.Ordinal)
                        || f.Text.Contains("BasicRejectAsync", StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files ack a delivery: " + string.Join(", ", offenders)
            + ".\n\nAn ack before its transaction commits loses the message on a crash, so the ordering is "
            + "kept in one place: the processor decides, the host acks.");
    }

    [Fact]
    public void EveryAck_RunsOnAnUncancellableToken()
    {
        var host = Read("Callu.Infrastructure", MessagingDirectory, "Consuming", "CalluConsumerHost.cs");

        var acks = Regex.Matches(host, @"Basic(Ack|Nack|Reject)Async\s*\(([^;]*?)\)", RegexOptions.Singleline);

        Assert.NotEmpty(acks);
        foreach (Match ack in acks)
        {
            Assert.Contains("CancellationToken.None", ack.Value, StringComparison.Ordinal);
        }
    }

    /// <summary>The flush between the handler and the commit is what keeps the handler's writes.</summary>
    [Fact]
    public void TheProcessor_FlushesBetweenTheHandlerAndTheCommit()
    {
        var processor = Read("Callu.Infrastructure", MessagingDirectory, "Consuming", "InboxMessageProcessor.cs");

        var handled = processor.IndexOf("handler.HandleAsync", StringComparison.Ordinal);
        var flushed = processor.IndexOf("SaveChangesAsync", handled, StringComparison.Ordinal);
        var committed = processor.IndexOf("CommitAsync", handled, StringComparison.Ordinal);

        Assert.True(handled > 0, "the processor no longer calls a handler");
        Assert.True(flushed > handled && flushed < committed,
            "Without a flush between the handler and the commit, the inbox row commits and the handler's "
            + "writes leave with the change tracker: the message is acked and can never be redelivered.");
    }

    // ── Staging belongs inside the caller's transaction ────────────────────────────────────────────

    [Fact]
    public void EveryFileThatStagesAMessage_AlsoOpensATransaction()
    {
        var offenders = ProductFiles()
            .Where(f => Regex.IsMatch(f.Text, @"\boutbox\.Stage\s*\("))
            .Where(f => !f.Text.Contains("ExecuteInTransactionAsync", StringComparison.Ordinal))
            .Select(f => f.Name)
            .Where(name => name is not ("OutboxEscalationWorkflowSignal.cs" or "OutboxStatusPageSubscriberNotifier.cs"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files stage a message without opening a transaction: " + string.Join(", ", offenders)
            + ". The adapters are exempt because their callers own the transaction, and the writer throws "
            + "when there is none.");
    }

    [Fact]
    public void TheWriter_NeverFlushesOnItsOwn()
    {
        var writer = Read("Callu.Infrastructure", MessagingDirectory, "Outbox", "OutboxWriter.cs");

        Assert.DoesNotContain("SaveChanges", writer, StringComparison.Ordinal);
        Assert.Contains("CurrentTransaction is null", writer, StringComparison.Ordinal);
    }

    // ── The dispatcher's claim is ordered, bounded and skips locked rows ───────────────────────────

    [Fact]
    public void TheClaim_IsOrderedBoundedAndSkipsLockedRows()
    {
        var dispatcher = Read("Callu.Infrastructure", MessagingDirectory, "Outbox", "OutboxDispatcher.cs");

        Assert.Contains("ORDER BY", dispatcher, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE SKIP LOCKED", dispatcher, StringComparison.Ordinal);
        Assert.Contains("LIMIT {BatchSize}", dispatcher, StringComparison.Ordinal);
        Assert.Matches(@"const int BatchSize = \d+;", dispatcher);
    }

    [Fact]
    public void TheSweepsClaim_IsOrderedBoundedAndSkipsLockedRows()
    {
        var sweep = Read("Callu.Infrastructure", MessagingDirectory, "Consuming", "InboxRetrySweep.cs");

        Assert.Contains("ORDER BY", sweep, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE SKIP LOCKED", sweep, StringComparison.Ordinal);
        Assert.Contains("LIMIT {BatchSize}", sweep, StringComparison.Ordinal);
    }

    /// <summary>The retry bound is taken from the entity, so two places cannot disagree about when to give up.</summary>
    [Fact]
    public void TheLadderBound_ComesFromTheEntity()
    {
        var sweep = Read("Callu.Infrastructure", MessagingDirectory, "Consuming", "InboxRetrySweep.cs");
        var processor = Read("Callu.Infrastructure", MessagingDirectory, "Consuming", "InboxMessageProcessor.cs");

        Assert.Contains("InboxEntry.MaxAttempts", sweep, StringComparison.Ordinal);
        Assert.Contains("InboxEntry.MaxAttempts", processor, StringComparison.Ordinal);
        Assert.Contains("InboxEntry.BackoffFor", sweep, StringComparison.Ordinal);
    }

    /// <summary>Publisher confirms are two flags in 7.x, and one of them alone makes every publish look fine.</summary>
    [Fact]
    public void ThePublishChannel_TracksConfirmations()
    {
        var connection = Read("Callu.Infrastructure", MessagingDirectory, "Broker", "CalluBrokerConnection.cs");

        Assert.Contains("publisherConfirmationsEnabled: true", connection, StringComparison.Ordinal);
        Assert.Contains("publisherConfirmationTrackingEnabled: true", connection, StringComparison.Ordinal);
    }

    /// <summary>Without mandatory, a routing key with no bound queue is discarded by the broker in silence.</summary>
    [Fact]
    public void EveryPublish_IsMandatory()
    {
        var dispatcher = Read("Callu.Infrastructure", MessagingDirectory, "Outbox", "OutboxDispatcher.cs");

        var publishes = Regex.Matches(dispatcher, @"BasicPublishAsync\s*\(([^;]*?)\)\s*;", RegexOptions.Singleline);

        Assert.NotEmpty(publishes);
        foreach (Match publish in publishes)
        {
            Assert.Contains("mandatory: true", publish.Value, StringComparison.Ordinal);
            Assert.Contains("basicProperties", publish.Value, StringComparison.Ordinal);
        }

        Assert.Contains("Persistent = true", dispatcher, StringComparison.Ordinal);
    }
}
