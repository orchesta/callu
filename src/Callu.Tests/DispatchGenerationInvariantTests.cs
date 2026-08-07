using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services;
using Callu.Shared.Models.Notifications;

namespace Callu.Tests;

/// <summary>Pins the invariant that every NotificationPayload in product code carries a derived DispatchGeneration.</summary>
public class DispatchGenerationInvariantTests
{
    /// <summary>NotificationPayload's required properties, read off the type rather than hand-maintained.</summary>
    private static readonly string[] RequiredPayloadProperties =
        typeof(NotificationPayload)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            .Select(p => p.Name)
            .Order()
            .ToArray();

    /// <summary>Every object-creation or <c>with</c> expression carrying an initializer block, brace-matched over code.</summary>
    private static IEnumerable<Initializer> Initializers(string rawSource, string file)
    {
        var source = SourceScanner.Mask(rawSource);

        foreach (Match keyword in Regex.Matches(source, @"\b(?<kw>new|with)\b"))
        {
            var i = keyword.Index + keyword.Length;
            var type = string.Empty;

            if (keyword.Groups["kw"].Value == "new")
            {
                i = SkipWhitespace(source, i);

                var typeStart = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] is '_' or '.' or '<' or '>' or '?'))
                    i++;
                type = source[typeStart..i];

                i = SkipWhitespace(source, i);
                if (i < source.Length && source[i] == '(')
                {
                    i = SkipBalanced(source, i, '(', ')');
                    if (i < 0) continue;
                    i = SkipWhitespace(source, i);
                }
            }
            else
            {
                i = SkipWhitespace(source, i);
            }

            if (i >= source.Length || source[i] != '{') continue;

            var close = SkipBalanced(source, i, '{', '}');
            if (close < 0) continue;

            yield return new Initializer(
                file,
                type.Trim(),
                source[i..close],
                IsWithCopy: keyword.Groups["kw"].Value == "with");
        }
    }

    private static int SkipWhitespace(string source, int i)
    {
        while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
        return i;
    }

    /// <summary>Index just past the matching close, or -1 if it never closes.</summary>
    private static int SkipBalanced(string source, int open, char opening, char closing)
    {
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == opening) depth++;
            else if (source[i] == closing && --depth == 0) return i + 1;
        }
        return -1;
    }

    private sealed record Initializer(string File, string TypeName, string Body, bool IsWithCopy)
    {
        /// <summary>Sets a property (rather than merely mentioning it in a nested expression).</summary>
        public bool Sets(string property) =>
            Regex.IsMatch(Body, $@"(?<![\w\.]){Regex.Escape(property)}\s*=[^=]");

        /// <summary>A NotificationPayload by name or by shape; a <c>with</c> copy is excluded because it inherits the generation.</summary>
        public bool IsPayloadConstruction =>
            !IsWithCopy &&
            (TypeName == nameof(NotificationPayload)
             || TypeName.EndsWith($".{nameof(NotificationPayload)}", StringComparison.Ordinal)
             || (RequiredPayloadProperties.Length > 0 && RequiredPayloadProperties.All(Sets)));
    }

    private static List<Initializer> ProductPayloadConstructions() =>
        SourceScanner.ProductFiles()
            .SelectMany(file => Initializers(SourceScanner.Code(file), file))
            .Where(i => i.IsPayloadConstruction)
            .ToList();

    // ── The premises the detector rests on ────────────────────────────────────────────────────────

    /// <summary>The detector's premise: no constructor may set the required members and so skip the initializer it reads.</summary>
    [Fact]
    public void APayload_CannotBeBuilt_WithoutAnInitializerTheDetectorCanSee()
    {
        var skipsTheInitializer = typeof(NotificationPayload)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            // The compiler's own copy constructor (what `with` runs) sets the required members from the
            // original. That is precisely why a `with` copy cannot lose a generation — not a hole.
            .Where(c => c.GetParameters() is not [{ } only] || only.ParameterType != typeof(NotificationPayload))
            .Where(c => c.GetCustomAttribute<SetsRequiredMembersAttribute>() is not null)
            .Select(c => $"({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))})")
            .ToList();

        Assert.True(skipsTheInitializer.Count == 0,
            "NotificationPayload has gained a [SetsRequiredMembers] constructor: "
            + string.Join(", ", skipsTheInitializer)
            + ". A payload can now be built with no object initializer — and the detector in this file "
            + "recognises a payload BY its required properties being set, so it cannot see one. Teach "
            + "Initializers() to recognise a bare `new NotificationPayload(...)` before landing this, "
            + "or the dispatch-generation guard is switched off without anything going red.");
    }

    /// <summary>The payload's shape is pinned, because the detector fails safe in neither direction if it changes.</summary>
    [Fact]
    public void ThePayloadsShape_IsTheOneTheDetectorLooksFor() =>
        Assert.Equal(["EventType", "IncidentId", "Severity", "Title"], RequiredPayloadProperties);

    // ── The guards ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A dispatch path that forgets DispatchGeneration fails here rather than in production.</summary>
    [Fact]
    public void EveryNotificationPayload_InProductCode_SetsDispatchGeneration()
    {
        var constructions = ProductPayloadConstructions();

        // Guard the premise: a scanner that found nothing would pass vacuously.
        Assert.True(constructions.Count >= 1,
            $"Expected to find the known NotificationPayload construction site; found {constructions.Count}.");

        var missing = constructions
            .Where(c => !c.Sets(nameof(NotificationPayload.DispatchGeneration)))
            .Select(c => Path.GetFileName(c.File))
            .ToList();

        Assert.True(missing.Count == 0,
            "These files build a NotificationPayload without a DispatchGeneration, so their page "
            + "reuses the previous escalation run's dedupe key and is dropped as a duplicate after a "
            + "reopen: " + string.Join(", ", missing)
            + ". Derive it with NotificationPayload.GenerationFor(incident.EscalationStartedAt).");
    }

    /// <summary>Exactly one place in the product builds a page; a second copy of the dispatch logic fails here.</summary>
    [Fact]
    public void TheOrchestrator_IsTheOnlyPlaceThatBuildsAPage()
    {
        var files = ProductPayloadConstructions()
            .Select(c => Path.GetFileName(c.File))
            .Distinct()
            .Order()
            .ToList();

        Assert.True(
            files is ["EscalationOrchestrator.cs"],
            "A NotificationPayload is built outside EscalationOrchestrator: "
            + string.Join(", ", files)
            + ". That is a second copy of the dispatch logic, and it is how the same paging bug has "
            + "been re-introduced three times. Call IEscalationOrchestrator.EscalateNowAsync (or add a "
            + "parameter to the shared path) instead of rebuilding the payload here.");
    }

    /// <summary>The same rule from the other end: no other product file may call the dispatcher's paging methods.</summary>
    [Fact]
    public void TheOrchestrator_IsTheOnlyPlaceThatPagesAResponder()
    {
        string[] pagingMethods = ["NotifyOnCallAsync", "NotifyUsersAsync", "NotifyTeamAsync"];
        string[] allowed = ["EscalationOrchestrator.cs", "INotificationDispatcher.cs", "NotificationDispatcher.cs"];

        var callers = SourceScanner.ProductFiles()
            .Where(f => !allowed.Contains(Path.GetFileName(f)))
            .Where(f => pagingMethods.Any(m => Regex.IsMatch(SourceScanner.Code(f), $@"\b{m}\s*\(")))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(callers.Count == 0,
            "These files page responders directly instead of going through EscalationOrchestrator: "
            + string.Join(", ", callers)
            + ". Every escalation page must run the one dispatch/commit path, or the next fix to it "
            + "will miss this one — as it did three releases running.");
    }

    /// <summary>Every assignment of the generation comes from the shared function, not a second formula.</summary>
    [Fact]
    public void EveryDispatchPath_DerivesTheGeneration_FromTheSharedFunction()
    {
        var offenders = SourceScanner.ProductFiles()
            .Where(file => Regex.Matches(
                    SourceScanner.Code(file),
                    @"(?<![\w\.])DispatchGeneration\s*=\s*(?<source>[^,;\r\n]+)")
                .Any(m => !Regex.IsMatch(
                    m.Groups["source"].Value,
                    @"^\s*(NotificationPayload\.GenerationFor|ComputeDispatchGeneration)\s*\(")))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "DispatchGeneration must come from NotificationPayload.GenerationFor (directly or via "
            + "EscalationOrchestrator.ComputeDispatchGeneration), never from a literal, a second "
            + "formula, or a value carried in from elsewhere: " + string.Join(", ", offenders));
    }

    // ── The detector must still be able to go red ─────────────────────────────────────────────────

    /// <summary>Every syntax a third dispatch path could plausibly use must be caught by the detector.</summary>
    [Theory]
    [InlineData("""
        var payload = new NotificationPayload
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
            EscalationLevel = currentStep.Level,
        };
        """)]
    [InlineData("""
        NotificationPayload payload = new()
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
        };
        """)]
    [InlineData("""
        private static NotificationPayload Build(Incident incident) => new()
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
        };
        """)]
    [InlineData("""
        return new NotificationPayload
        {
            IncidentId = incident.Id, Title = incident.Title,
            Severity = incident.Severity.ToString(), EventType = NotificationEventType.EscalationStep
        };
        """)]
    // A brace inside a string literal, which a raw-text brace match closes the initializer on.
    [InlineData("""
        var payload = new NotificationPayload
        {
            IncidentId = incident.Id,
            Title = "checkout } failing",
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
        };
        """)]
    // The same, in a COMMENT inside the block.
    [InlineData("""
        var payload = new NotificationPayload
        {
            IncidentId = incident.Id,
            // one day, set { DispatchGeneration } here }
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
        };
        """)]
    // Constructor arguments that nest, which a single-level parenthesis skip cannot see past.
    [InlineData("""
        var payload = new NotificationPayload(Defaults(incident.Id))
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
        };
        """)]
    public void TheGuard_Detects_ADispatchPathThatForgetsTheGeneration(string forgetful)
    {
        var flagged = Assert.Single(
            Initializers(forgetful, "Forgetful.cs"), i => i.IsPayloadConstruction);

        Assert.False(flagged.Sets(nameof(NotificationPayload.DispatchGeneration)));
    }

    [Theory]
    [InlineData("""
        var payload = new NotificationPayload
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
            DispatchGeneration = NotificationPayload.GenerationFor(incident.EscalationStartedAt)
        };
        """)]
    [InlineData("""
        NotificationPayload payload = new()
        {
            IncidentId = incident.Id,
            Title = incident.Title,
            Severity = incident.Severity.ToString(),
            EventType = NotificationEventType.EscalationStep,
            DispatchGeneration = ComputeDispatchGeneration(incident.EscalationStartedAt)
        };
        """)]
    public void TheGuard_Accepts_ADispatchPathThatDerivesTheGeneration(string correct)
    {
        var accepted = Assert.Single(
            Initializers(correct, "Correct.cs"), i => i.IsPayloadConstruction);

        Assert.True(accepted.Sets(nameof(NotificationPayload.DispatchGeneration)));
    }

    /// <summary>The shared-function rule reads code, so neither a log line nor a doc comment counts as a second formula.</summary>
    [Fact]
    public void TheSharedFunctionRule_ReadsCode_NotProse()
    {
        const string innocent = """
            /// Always derive it: DispatchGeneration = NotificationPayload.GenerationFor(startedAt).
            logger.LogDebug("DispatchGeneration = {Generation}", payload.DispatchGeneration);
            """;

        Assert.DoesNotMatch(
            @"(?<![\w\.])DispatchGeneration\s*=\s*[^,;\r\n]+",
            SourceScanner.Mask(innocent));
    }

    /// <summary>A <c>with</c> copy is not mistaken for a fresh payload that forgot the generation.</summary>
    [Fact]
    public void TheGuard_DoesNotMistake_AWithCopy_ForAForgetfulConstruction()
    {
        const string copy = """
            var forSecondary = payload with { EscalationLevel = step.Level + 1 };
            """;

        Assert.DoesNotContain(Initializers(copy, "Copy.cs"), i => i.IsPayloadConstruction);
    }

    /// <summary>The detector must not drag unrelated object initializers into the invariant.</summary>
    [Fact]
    public void TheGuard_IgnoresInitializers_ThatAreNotPayloads()
    {
        const string unrelated = """
            var evt = new IncidentTimelineEvent
            {
                IncidentId = incident.Id,
                EventType = TimelineEventType.Escalated,
                Title = "Escalation step 1 triggered",
                ActorUserId = "system"
            };
            """;

        Assert.DoesNotContain(Initializers(unrelated, "Unrelated.cs"), i => i.IsPayloadConstruction);
    }

    [Fact]
    public void ComputeDispatchGeneration_IsGenerationFor() =>
        Assert.Equal(
            NotificationPayload.GenerationFor(new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Utc)),
            EscalationOrchestrator.ComputeDispatchGeneration(new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Utc)));

    /// <summary>Every pass a repeating policy may run gets its own generation, and none reaches the next run's.</summary>
    [Fact]
    public void EachRepeatPass_HasItsOwnGeneration()
    {
        var startedAt = new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Utc);

        var generations = Enumerable
            .Range(0, EscalationPolicy.MaxRepeatCyclesCap)
            .Select(cycle => NotificationPayload.GenerationFor(startedAt, cycle))
            .ToList();

        Assert.Equal(generations.Count, generations.Distinct().Count());

        Assert.NotEqual(
            NotificationPayload.GenerationFor(startedAt, EscalationPolicy.MaxRepeatCyclesCap - 1),
            NotificationPayload.GenerationFor(startedAt.AddTicks(TimeSpan.TicksPerMicrosecond), 0));
    }

    /// <summary>The pass number rides in the ticks the microsecond truncation frees, so the cap has to fit there.</summary>
    [Fact]
    public void TheRepeatCycleCap_FitsInTheRoomTheTruncationLeaves() =>
        Assert.True(EscalationPolicy.MaxRepeatCyclesCap <= TimeSpan.TicksPerMicrosecond,
            $"MaxRepeatCyclesCap is {EscalationPolicy.MaxRepeatCyclesCap}, but only "
            + $"{TimeSpan.TicksPerMicrosecond} generations fit between two microseconds. Passes past that "
            + "clamp onto the last one's generation, reuse its dedupe key, and page nobody — silently.");

    /// <summary>The second pass of a repeating policy must not reuse the first pass's dedupe key.</summary>
    [Fact]
    public void TheSecondRepeatPass_DoesNotReuseTheFirstPassesDedupeKey()
    {
        var incidentId = Guid.NewGuid();
        var startedAt = new DateTime(2026, 7, 13, 4, 5, 6, DateTimeKind.Utc);

        NotificationPayload Pass(int cycle) => new()
        {
            IncidentId = incidentId,
            Title = "Checkout failing",
            Severity = "Critical",
            EventType = NotificationEventType.EscalationStep,
            EscalationLevel = 1,
            DispatchGeneration = EscalationOrchestrator.ComputeDispatchGeneration(startedAt, cycle)
        };

        string Key(NotificationPayload p) =>
            NotificationFactory.ComputeDedupeKey("responder-1", p, NotificationType.VoiceCall, p.DispatchGeneration);

        // Same incident, same responder, same level: only the pass differs, and that has to be enough.
        Assert.NotEqual(Key(Pass(0)), Key(Pass(1)));
    }

    /// <summary>A payload left at the default generation lands on the dedupe key a generation-0 run would use.</summary>
    [Fact]
    public void APayloadWithoutAGeneration_ReproducesThePreviousRunsDedupeKey()
    {
        var incidentId = Guid.NewGuid();

        NotificationPayload Payload(long generation) => new()
        {
            IncidentId = incidentId,
            Title = "Checkout failing",
            Severity = "Critical",
            EventType = NotificationEventType.EscalationStep,
            EscalationLevel = 1,
            DispatchGeneration = generation
        };

        var firstRun = Payload(NotificationPayload.GenerationFor(new DateTime(2026, 7, 13, 1, 0, 0, DateTimeKind.Utc)));
        var afterReopen = Payload(NotificationPayload.GenerationFor(new DateTime(2026, 7, 13, 2, 0, 0, DateTimeKind.Utc)));
        var forgotten = Payload(0);

        string Key(NotificationPayload p) =>
            NotificationFactory.ComputeDedupeKey("responder-1", p, NotificationType.VoiceCall, p.DispatchGeneration);

        Assert.NotEqual(Key(firstRun), Key(afterReopen));

        // The bug: a path that forgets the generation lands on the key a generation-0 run would use,
        // so it collides with anything else that forgot it — including the pre-fix rows.
        Assert.Equal(
            NotificationFactory.ComputeDedupeKey("responder-1", forgotten, NotificationType.VoiceCall),
            Key(forgotten));
        Assert.NotEqual(Key(firstRun), Key(forgotten));
    }
}
