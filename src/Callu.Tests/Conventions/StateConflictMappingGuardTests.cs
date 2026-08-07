using System.Reflection;
using System.Text.RegularExpressions;
using Callu.Application.Services;

namespace Callu.Tests.Conventions;

/// <summary>An illegal incident state transition surfaces as 409, not 500.</summary>
public class StateConflictMappingGuardTests
{
    private const string ServiceFile = "IncidentService.cs";
    private const string DomainFile = "Incident.cs";
    private const string HandlerFile = "GlobalExceptionHandler.cs";

    private static string Read(string fileName) => SourceMembers.Read(fileName);

    private static Dictionary<string, string> MethodBodies(string code) => SourceMembers.MethodBodies(code);

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Deriving the surface rather than listing it, so a transition added later is covered by default.

    private static readonly Lazy<HashSet<string>> DomainTransitions = new(() =>
        MethodBodies(Read(DomainFile))
            .Where(m => m.Value.Contains("throw new InvalidOperationException", StringComparison.Ordinal))
            .Select(m => m.Key)
            .ToHashSet(StringComparer.Ordinal));

    private static IEnumerable<(string Method, string Body)> StateTransitionMethods()
    {
        var bodies = MethodBodies(Read(ServiceFile));

        foreach (var method in typeof(IIncidentService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)
            .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (!bodies.TryGetValue(method.Name, out var body)) continue;

            if (DomainTransitions.Value.Any(t =>
                    Regex.IsMatch(body, $@"\.\s*{Regex.Escape(t)}\s*\(")))
                yield return (method.Name, body);
        }
    }

    private static bool CatchesItself(string body) =>
        Regex.IsMatch(body, @"catch\s*\(\s*InvalidOperationException\b");

    private static readonly Dictionary<string, string> NotARace = new(StringComparer.Ordinal)
    {
        ["CreateIncidentAsync"] =
            "Its Acknowledge call is the maintenance-window auto-ack, on an Incident constructed Open a "
            + "few lines above in the same scope — no other actor can have moved it, so the guard cannot "
            + "fire. Translating it would also be actively wrong: this is the alert-ingest path, where a "
            + "4xx makes Alertmanager drop the alert outright instead of retrying. If the "
            + "throw were ever reachable here it would be a bug in our own construction, and 500 is the "
            + "honest answer to that."
    };

    // Coarse on purpose: the arms span several lines, so this looks for the type name with
    // HttpStatusCode.Conflict close behind it rather than parsing the switch.
    private static bool HandlerMapsInvalidOperationToConflict(string handlerCode) =>
        Regex.IsMatch(
            handlerCode,
            @"InvalidOperationException\b(?:(?!InvalidOperationException).){0,200}?HttpStatusCode\s*\.\s*Conflict",
            RegexOptions.Singleline);

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // THE GUARD.

    [Fact]
    public void EveryStateTransition_TurnsAnIllegalTransitionIntoAConflict()
    {
        if (HandlerMapsInvalidOperationToConflict(Read(HandlerFile)))
            return;   // one arm covers the lot; nothing left to check per method

        var unmapped = StateTransitionMethods()
            .Where(m => !CatchesItself(m.Body))
            .Where(m => !NotARace.ContainsKey(m.Method))
            .Select(m => m.Method)
            .ToList();

        Assert.True(unmapped.Count == 0,
            "These IIncidentService methods drive a domain state transition and let its "
            + "InvalidOperationException escape untranslated: " + string.Join(", ", unmapped)
            + ".\n\nGlobalExceptionHandler's switch has no arm for that type, so the operator gets 500 "
            + "'an unexpected error occurred' where they should get 409 and the reason. Acknowledge and "
            + "Resolve shipped exactly like this — the two endpoints two responders actually race on, so "
            + "the second person to press the button at 3am is told the system is broken rather than that "
            + "somebody already took the incident.\n\nAdd the arm (it covers every method at once, and "
            + "the next transition method too) or catch it here and throw ConflictException.");
    }

    [Fact]
    public void EveryExemption_IsStillAStateTransitionMethod()
    {
        var transitions = StateTransitionMethods().Select(m => m.Method).ToHashSet(StringComparer.Ordinal);

        var stale = NotARace.Keys.Where(k => !transitions.Contains(k)).Order(StringComparer.Ordinal).ToList();

        Assert.True(stale.Count == 0,
            "These methods are exempted in NotARace but no longer call a domain state guard: "
            + string.Join(", ", stale)
            + ".\n\nDelete the entry. An exemption whose reason has stopped being true is worse than no "
            + "exemption, because it keeps covering the method after the premise is gone.");

        Assert.All(NotARace, e => Assert.True(e.Value.Length >= 80,
            $"{e.Key}'s exemption reason is too short to judge — say why a race is impossible there."));
    }

    [Fact]
    public void TheDomainReallyDefendsItsStateMachine_WithInvalidOperationException()
    {
        var transitions = DomainTransitions.Value;

        // The five transitions, named out loud: this list going quiet IS the bug.
        Assert.Contains("Acknowledge", transitions);
        Assert.Contains("Resolve", transitions);
        Assert.Contains("Close", transitions);
        Assert.Contains("Reopen", transitions);
        Assert.Contains("ChangeStatus", transitions);
    }

    [Fact]
    public void TheGuardedSurface_IsTheMethodsThatActuallyTransition()
    {
        var found = StateTransitionMethods().Select(m => m.Method).ToList();

        Assert.NotEmpty(found);

        Assert.Contains(nameof(IIncidentService.AcknowledgeIncidentAsync), found);
        Assert.Contains(nameof(IIncidentService.ResolveIncidentAsync), found);
        Assert.Contains(nameof(IIncidentService.CloseIncidentAsync), found);
        Assert.Contains(nameof(IIncidentService.ReopenIncidentAsync), found);

        // A read-only query cannot transition anything; if it shows up, the extraction overran.
        Assert.DoesNotContain(nameof(IIncidentService.GetIncidentByIdAsync), found);
    }

    [Theory]
    [InlineData(nameof(IIncidentService.CloseIncidentAsync))]
    [InlineData(nameof(IIncidentService.ReopenIncidentAsync))]
    [InlineData(nameof(IIncidentService.UpdateIncidentAsync))]
    [InlineData(nameof(IIncidentService.ReassignIncidentAsync))]
    public void TheMethodsThatAlreadyTranslateIt_StillDo(string method)
    {
        var body = MethodBodies(Read(ServiceFile))[method];

        Assert.True(CatchesItself(body),
            $"{method} no longer catches InvalidOperationException. It used to translate an illegal "
            + "state transition into a 409; without it the operator gets 500 'an unexpected error "
            + "occurred' where they should get 409 and the reason.");
    }

    [Fact]
    public void TheDetectors_CanTellCoveredFromUncovered()
    {
        const string uncovered = """
            {
                var incident = await FindIncidentForMutationAsync(incidentId, cancellationToken);
                incident.Acknowledge(userId);
                return true;
            }
            """;

        const string covered = """
            {
                try { incident.Acknowledge(userId); }
                catch (InvalidOperationException ex) { throw new ConflictException(ex.Message); }
            }
            """;

        Assert.False(CatchesItself(uncovered));
        Assert.True(CatchesItself(covered));

        // The handler arm in the shape the fix takes, asserted on a snippet and not the real file:
        // "the handler has no arm today" was the defect, so asserting it would invert on the fix.
        Assert.True(HandlerMapsInvalidOperationToConflict(
            """
            ConflictException ex => (HttpStatusCode.Conflict, ApiResponse.Fail<object>(ex.Message)),

            InvalidOperationException ex => (HttpStatusCode.Conflict, ApiResponse.Fail<object>(ex.Message)),
            """));

        // An arm sending the type somewhere else must NOT read as mapped, or a 500 passes as a 409.
        Assert.False(HandlerMapsInvalidOperationToConflict(
            "InvalidOperationException => (HttpStatusCode.InternalServerError, ApiResponse.Fail<object>(m)),"));

        // ConflictException really is mapped in that file today, which is the premise underneath.
        Assert.Matches(
            @"ConflictException[^\r\n]*HttpStatusCode\s*\.\s*Conflict",
            Read(HandlerFile));
    }
}
