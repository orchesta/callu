using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Callu.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Callu.Tests.Conventions;

/// <summary>
/// Every controller action taking a caller-supplied row count is bounded, and the inventory says
/// where.
/// </summary>
public class PaginationClampGuardTests
{
    private enum Bound
    {
        ClampedInTheAction,
        RangeAttribute,
        ClampedByTheDto,

        // Not checked: the entry only names the method a reader has to go and look at.
        ClampedByTheSink,

        // Not checked either. The action returns everything the filter matches on purpose, and is
        // bounded by never materialising it rather than by a row cap.
        StreamedInBatches,

        KnownViolation
    }

    private sealed record Paginated(string Controller, string Action, string Parameters, Bound Bound, string Why);

    private static readonly Paginated[] Inventory =
    [
        new("AuditLogsController", "GetLogs", "count", Bound.ClampedInTheAction,
            "Math.Clamp(count, 1, MaxPageSize) on the first line of the action. This is the shape the "
            + "other seven are measured against."),

        new("CallLogsController", "GetAll", "page, pageSize", Bound.ClampedInTheAction,
            "Math.Max(1, page) + Math.Clamp(pageSize, 1, MaxPageSize) on the first two lines, matching "
            + "AuditLogsController. It matters here because CanViewCallLogs is granted to every role "
            + "including Viewer: unbounded, any authenticated user can ask for the whole table with an "
            + "Include on the incident — on the 1-2 GB VPS the quick-start targets, an OOM kill of the "
            + "host that ingests alerts."),

        new("ServicesController", "GetAll", "page, pageSize", Bound.ClampedInTheAction,
            "The same two lines. It delegates to "
            + "ServiceManagementService.GetAllAsync, which Skips and Takes the caller's numbers verbatim, "
            + "so the bound has to be applied before the call."),

        new("DiagnosticsController", "Search", "limit", Bound.ClampedByTheSink,
            "JaegerTracingQueryService.SearchAsync applies Math.Clamp(request.Limit, 1, MaxLimit) with "
            + "MaxLimit = 1000 before it builds the Jaeger query. Unverified by this guard."),

        new("IncidentsController", "GetWebhookDeliveries", "limit", Bound.ClampedByTheSink,
            "IncidentService.GetWebhookDeliveriesAsync applies Take(Math.Clamp(limit, 1, 100)). "
            + "Unverified by this guard."),

        new("NotificationChannelsController", "GetDeliveries", "pageSize", Bound.ClampedByTheSink,
            "NotificationChannelService.GetDeliveriesAsync applies Math.Max(page, 1) and "
            + "Math.Clamp(pageSize, 1, AppConstants.Pagination.MaxPageSize) before it queries. Clamped "
            + "in the service rather than the action because the service is the only caller-facing "
            + "entry point to a table that grows per incident per channel. Unverified by this guard; "
            + "NotificationChannelDeliveryHistoryTests.PageSizeAndPage_AreClampedByTheService pins it."),

        new("AuditLogsController", "Export", "filter", Bound.StreamedInBatches,
            "The export deliberately returns every row the filter matches — that is what an export "
            + "is — so there is no page size to clamp. It is bounded a different way: "
            + "AuditLogService.StreamAsync walks a keyset cursor in batches of 500 and yields each "
            + "row straight to the response, so neither the service nor the action ever holds the "
            + "range in memory. Skip/OFFSET was rejected for the same reason: on a five-year trail "
            + "the last pages would re-walk everything before them. "
            + "Unverified by this guard; AuditLogExportTests.TheWriterNeverPullsTheWholeRangeBeforeWriting "
            + "pins the lazy write and AuditLogStreamTests pins the cursor."),

        new("AuditLogsController", "Search", "filter", Bound.ClampedByTheSink,
            "AuditLogFilter carries plain Page and PageSize ints, but AuditLogService.SearchAsync "
            + "applies Math.Max(filter.Page, 1) and "
            + "Math.Clamp(filter.PageSize, 1, AppConstants.Pagination.MaxPageSize) before it queries. "
            + "Clamped in the service because an Auditor can reach this read and the table grows with "
            + "every audited action, so an unbounded page is an outage rather than a slow response. "
            + "Unverified by this guard; "
            + "AuditLogSearchTests.PageSizeIsClamped_AndPageZeroBecomesPageOne pins it."),

        new("IncidentsController", "GetIncidents", "filter", Bound.ClampedByTheSink,
            "IncidentFilter itself is unbounded — Page and PageSize are plain init-only ints — but "
            + "IncidentService.GetIncidentsPagedAsync applies Math.Max(1, filter.Page) and "
            + "Math.Clamp(filter.PageSize, 1, 100). That the DTO carries no bound of its own is the "
            + "weaker half: a second caller of the filter inherits no protection. Unverified by this guard."),

        new("NotificationsController", "GetRecent", "count", Bound.ClampedByTheSink,
            "NotificationQueryService.GetRecentAsync applies Math.Clamp(count, 1, MaxPageSize) as its "
            + "first statement. Unverified by this guard."),

        new("VoximplantController", "GetCallHistory", "count", Bound.ClampedByTheSink,
            "VoximplantManagementService.GetCallHistoryAsync normalises the request through "
            + "NormalizeHistoryWindow, which applies Math.Clamp(count <= 0 ? 50 : count, 1, 100) and "
            + "refuses a window wider than seven days, so the upstream call is bounded on both the row "
            + "count and the time range. The rows come from Voximplant rather than our own tables, so an "
            + "unbounded count would be a slow upstream call and a large response to hold, not a table "
            + "scan. Unverified by this guard."),

        new("CapturesController", "GetByService", "page, pageSize", Bound.ClampedByTheSink,
            "WebhookCaptureService.GetCapturesAsync clamps through ClampPage: Math.Max(1, page) and "
            + "Math.Clamp(pageSize, 1, WebhookCapture.MaxPageSize). Clamped in the service so the "
            + "integration-scoped twin below cannot drift from it. Unverified by this guard; "
            + "WebhookCaptureScopeTests.Pagination_ClampsPageZeroAndOversizedPageSize pins it."),

        new("CapturesController", "GetByIntegration", "page, pageSize", Bound.ClampedByTheSink,
            "The same ClampPage in WebhookCaptureService.GetCapturesByIntegrationAsync. Capture bodies "
            + "are up to 64 KB each, so the 50-row page bound is also a response-size bound. Unverified "
            + "by this guard; the same scope test pins it."),

        new("VideoConferenceAdminController", "GetConferenceRooms", "filter", Bound.ClampedByTheDto,
            "ConferenceRoomFilter.PageSize has a field-backed setter that clamps to "
            + "AppConstants.Pagination.MaxPageSize, so the bound travels with the DTO rather than with "
            + "one caller. This is the strongest of the five shapes and the one the other filter should "
            + "copy. Exercised by EveryClampedByTheDtoVerdict_ReallyBoundsItself below.")
    ];

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Locating the paginated surface.

    private static readonly string[] RowCountParameters =
        ["page", "pageSize", "count", "limit", "take", "top", "size", "skip", "offset"];

    private static readonly Assembly ApiAssembly = typeof(HealthController).Assembly;

    private static IEnumerable<(Type Controller, MethodInfo Action, List<ParameterInfo> Parameters)> PaginatedActions()
    {
        foreach (var controller in ApiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            foreach (var action in controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
                .OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var paginating = action.GetParameters().Where(IsRowCount).ToList();

                if (paginating.Count > 0)
                    yield return (controller, action, paginating);
            }
        }
    }

    private static bool IsRowCount(ParameterInfo parameter) =>
        (parameter.ParameterType == typeof(int) || parameter.ParameterType == typeof(int?))
            && RowCountParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase)
        || PagedDtoProperties(parameter.ParameterType).Count == 2;

    private static List<PropertyInfo> PagedDtoProperties(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return [];

        return new[] { "Page", "PageSize" }
            .Select(name => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p is { CanWrite: true } && p.PropertyType == typeof(int))
            .Select(p => p!)
            .ToList();
    }

    private static readonly Regex Clamp = new(@"Math\s*\.\s*(?:Clamp|Min|Max)\s*\(", RegexOptions.Compiled);

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // THE GUARD.

    [Fact]
    public void EveryPaginatedAction_IsAccountedForInTheInventory()
    {
        var listed = Inventory.Select(p => (p.Controller, p.Action)).ToHashSet();

        var found = PaginatedActions()
            .Select(x => (Controller: x.Controller.Name, Action: x.Action.Name, Parameters: string.Join(", ", x.Parameters.Select(p => p.Name))))
            .ToList();

        var unlisted = found
            .Where(x => !listed.Contains((x.Controller, x.Action)))
            .Select(x => $"{x.Controller}.{x.Action}({x.Parameters})")
            .Order(StringComparer.Ordinal)
            .ToList();

        var vanished = listed
            .Where(l => !found.Any(f => f.Controller == l.Controller && f.Action == l.Action))
            .Select(l => $"{l.Controller}.{l.Action}")
            .Order(StringComparer.Ordinal)
            .ToList();

        var message = new StringBuilder();

        if (unlisted.Count > 0)
            message.AppendLine(
                "These actions take a caller-supplied row count and are not in this file's inventory:\n  "
                + string.Join("\n  ", unlisted)
                + "\n\nSay where the bound comes from: ClampedInTheAction, RangeAttribute, "
                + "ClampedByTheDto, ClampedByTheSink (name the method), or KnownViolation (say what is "
                + "unbounded and why it still is).\n\nGET /call-logs shipped with neither a clamp nor a "
                + "Range attribute, on an "
                + "endpoint every role can reach, and pageSize=100000000 is one request away from an "
                + "OOM-killed API host on the hardware this product is self-hosted on.\n");

        if (vanished.Count > 0)
            message.AppendLine(
                "These inventory entries no longer name an action that takes a row count — renamed, "
                + "removed, or the parameter went away:\n  " + string.Join("\n  ", vanished) + "\n");

        if (unlisted.Count > 0 || vanished.Count > 0)
        {
            message.AppendLine("The paginated surface as the guard currently sees it:\n");
            foreach (var x in found.OrderBy(f => f.Controller, StringComparer.Ordinal).ThenBy(f => f.Action, StringComparer.Ordinal))
                message.AppendLine($"  {x.Controller}.{x.Action}({x.Parameters})");

            Assert.Fail(message.ToString());
        }
    }

    [Fact]
    public void EveryClampedInTheActionVerdict_ReallyClamps()
    {
        var lying = new List<string>();

        foreach (var entry in Inventory.Where(p => p.Bound == Bound.ClampedInTheAction))
        {
            var bodies = SourceMembers.MethodBodies(SourceMembers.Read($"{entry.Controller}.cs"));

            if (!bodies.TryGetValue(entry.Action, out var body) || !Clamp.IsMatch(body))
                lying.Add($"{entry.Controller}.{entry.Action}");
        }

        Assert.True(lying.Count == 0,
            "These actions are filed as clamping their own row count and their bodies contain no "
            + "Math.Clamp/Min/Max: " + string.Join(", ", lying)
            + ". Either the clamp was removed — in which case the endpoint is now unbounded — or the "
            + "verdict was wrong when it was written.");
    }

    [Fact]
    public void EveryRangeAttributeVerdict_ReallyHasTheAttribute()
    {
        var lying = Inventory
            .Where(p => p.Bound == Bound.RangeAttribute)
            .Where(p => !PaginatedActions().Any(x =>
                x.Controller.Name == p.Controller && x.Action.Name == p.Action
                && x.Parameters.All(param => param.GetCustomAttribute<RangeAttribute>() is not null)))
            .Select(p => $"{p.Controller}.{p.Action}")
            .ToList();

        Assert.True(lying.Count == 0,
            "These actions are filed as bounded by a [Range] attribute and at least one of their "
            + "row-count parameters does not carry one: " + string.Join(", ", lying));
    }

    [Fact]
    public void EveryClampedByTheDtoVerdict_ReallyBoundsItself()
    {
        var lying = new List<string>();

        foreach (var entry in Inventory.Where(p => p.Bound == Bound.ClampedByTheDto))
        {
            var action = PaginatedActions()
                .Single(x => x.Controller.Name == entry.Controller && x.Action.Name == entry.Action);

            foreach (var parameter in action.Parameters)
            {
                var properties = PagedDtoProperties(parameter.ParameterType);
                if (properties.Count != 2) continue;

                var dto = Activator.CreateInstance(parameter.ParameterType);
                var pageSize = properties.Single(p => p.Name == "PageSize");

                pageSize.SetValue(dto, int.MaxValue);

                if ((int)pageSize.GetValue(dto)! == int.MaxValue)
                    lying.Add($"{entry.Controller}.{entry.Action} ({parameter.ParameterType.Name}.PageSize)");
            }
        }

        Assert.True(lying.Count == 0,
            "These filter types are filed as bounding their own page size and they hand back "
            + "int.MaxValue unchanged: " + string.Join(", ", lying)
            + ". The bound was the DTO's, so every caller just lost it.");
    }

    [Fact]
    public void EveryClampedByTheSinkVerdict_NamesTheMethodThatClamps()
    {
        Assert.All(Inventory.Where(p => p.Bound == Bound.ClampedByTheSink), p =>
            Assert.Matches(@"\b\w+(?:Service|Executor|Engine|Repository)\.\w+Async\b", p.Why));
    }

    [Fact]
    public void EveryKnownViolation_NamesTheRiskAndWhyItIsStillOpen()
    {
        Assert.All(Inventory.Where(p => p.Bound == Bound.KnownViolation), p =>
        {
            Assert.True(
                p.Parameters.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Any(name => p.Why.Contains(name, StringComparison.Ordinal)),
                $"{p.Controller}.{p.Action} is filed as an unbounded violation and its justification "
                + $"names none of its own row-count parameters ({p.Parameters}). Say which number is "
                + "unbounded, or nobody can re-check the entry.");

            Assert.True(p.Why.Length >= 100,
                $"{p.Controller}.{p.Action}'s justification is {p.Why.Length} characters. This list is "
                + "what the next person works from; a file name on a list is not an entry.");
        });
    }

    /// <summary>
    /// An entry that outlives its defect is the same decoration as a skipped test nobody tracks, so
    /// a KnownViolation is re-checked against the code rather than trusted.
    /// </summary>
    [Fact]
    public void EveryKnownViolation_IsStillUnclamped()
    {
        var fixedAlready = Inventory
            .Where(p => p.Bound == Bound.KnownViolation)
            .Where(p => Clamp.IsMatch(ActionBody(p)))
            .Select(p => $"{p.Controller}.{p.Action}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(fixedAlready.Count == 0,
            "These are filed as unclamped violations and the action now clamps: "
            + string.Join(", ", fixedAlready)
            + ".\n\nRetag them (ClampedInTheAction) — leaving the entry costs the reader their trust in "
            + "every other entry on the list.");
    }

    private static string ActionBody(Paginated entry) =>
        SourceMembers.MethodBodies(SourceMembers.Read($"{entry.Controller}.cs"))
            .TryGetValue(entry.Action, out var body) ? body : "";

    [Fact]
    public void EveryInventoryEntry_SaysSomethingUseful()
    {
        Assert.All(Inventory, p => Assert.True(p.Why.Length >= 60, $"{p.Controller}.{p.Action}: {p.Why}"));
    }

    // ───────────────────────────────────────────────────────────────────────────────────────────────
    // Premises and control groups.

    [Fact]
    public void TheTwoUnboundedEndpoints_AreStillDetected()
    {
        var found = PaginatedActions().Select(x => $"{x.Controller.Name}.{x.Action.Name}").ToList();

        Assert.Contains($"{nameof(CallLogsController)}.GetAll", found);
        Assert.Contains($"{nameof(ServicesController)}.GetAll", found);

        // ...and the one that does it right, so the guard is comparing against something real.
        Assert.Contains($"{nameof(AuditLogsController)}.GetLogs", found);
    }

    [Theory]
    [InlineData("{ var clamped = Math.Clamp(count, 1, MaxPageSize); }", true)]
    [InlineData("{ var p = Math.Max(1, filter.Page); }", true)]
    [InlineData("{ .Take(Math.Min(limit, 100)) }", true)]
    [InlineData("{ var (items, total) = await callLogService.GetCallLogsPagedAsync(page, pageSize, ct); }", false)]
    public void TheClampDetector_TellsBoundedFromUnbounded(string body, bool expected)
    {
        Assert.Equal(expected, Clamp.IsMatch(body));
    }

    [Fact]
    public void TheClampDetector_ReadsTheActionNotTheFile()
    {
        var bodies = SourceMembers.MethodBodies(SourceMembers.Read($"{nameof(AuditLogsController)}.cs"));

        Assert.True(Clamp.IsMatch(bodies["GetLogs"]),
            "AuditLogsController.GetLogs is the reference implementation and its clamp is gone");

        // A fixture rather than the real tree, so it keeps holding after the real endpoints are fixed.
        const string mixed = """
            public IActionResult Bounded([FromQuery] int count = 10)
            {
                return Ok(Math.Clamp(count, 1, 100));
            }

            public IActionResult Unbounded([FromQuery] int pageSize = 25)
            {
                return Ok(service.Page(pageSize));
            }
            """;

        var mixedBodies = SourceMembers.MethodBodies(mixed);

        Assert.Matches(Clamp, mixedBodies["Bounded"]);
        Assert.DoesNotMatch(Clamp, mixedBodies["Unbounded"]);
    }

    [Fact]
    public void TheDtoProbe_CanTellABoundedFilterFromAnUnboundedOne()
    {
        var unbounded = new Callu.Shared.Models.Incidents.IncidentFilter { PageSize = int.MaxValue };
        Assert.Equal(int.MaxValue, unbounded.PageSize);

        var bounded = new Callu.Shared.Models.Conference.ConferenceRoomFilter { PageSize = int.MaxValue };
        Assert.True(bounded.PageSize < int.MaxValue,
            "ConferenceRoomFilter stopped clamping its own page size — the one filter in the codebase "
            + "that bounds itself, and the shape the other one is supposed to copy");
    }

    [Fact]
    public void ThePaginatedSurface_IsNotEmpty()
    {
        Assert.NotEmpty(PaginatedActions());
    }
}
