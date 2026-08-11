using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.AlertRules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Evaluates alert automation rules against incidents and executes matching actions.
/// Called from IncidentService after incident creation or update.
/// </summary>
public class AlertRuleEngine(
    IRepository<AlertRule> ruleRepo,
    IServiceProvider serviceProvider,
    IIncidentNoteService noteService,
    IAuditLogService auditLogService,
    ITransactionManager transactionManager,
    ILogger<AlertRuleEngine> logger) : IAlertRuleEngine
{
    /// <summary>Who an automation rule acts as.</summary>
    private const string AlertRuleActor = "system:alert-rule";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<int> EvaluateAsync(Incident incident, CancellationToken ct = default)
    {
        var rules = await ruleRepo.GetQueryable()
            .Where(r => r.IsEnabled && !r.IsDeleted)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0) return 0;

        var triggeredCount = 0;

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            foreach (var rule in rules)
            {
                try
                {
                    if (!MatchesConditions(rule, incident))
                        continue;

                    await ExecuteActions(rule, incident, ct);

                    rule.TriggerCount++;
                    rule.LastTriggeredAt = DateTime.UtcNow;
                    ruleRepo.Update(rule);
                    triggeredCount++;

                    logger.LogInformation(
                        "Alert rule '{RuleName}' triggered for incident '{IncidentTitle}' (ID: {IncidentId})",
                        rule.Name, incident.Title, incident.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error evaluating alert rule '{RuleName}' against incident {IncidentId}",
                        rule.Name, incident.Id);
                }
            }
            return triggeredCount;
        }, ct);

        return triggeredCount;
    }

    public async Task<string?> ShouldSuppressPagingAsync(Incident incident, CancellationToken ct = default)
    {
        // Read-only pre-pass: runs in the caller's (creation) transaction, must not execute
        // actions, mutate rule statistics, or open its own transaction. A cheap ActionsJson
        // pre-filter keeps the per-create cost negligible when no rule opts in.
        var rules = await ruleRepo.GetQueryable()
            .Where(r => r.IsEnabled && !r.IsDeleted && r.ActionsJson.Contains("suppressPaging"))
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            if (!MatchesConditions(rule, incident))
                continue;

            List<AlertRuleActionDto> actions;
            try
            {
                actions = JsonSerializer.Deserialize<List<AlertRuleActionDto>>(rule.ActionsJson, JsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                // fail-open per rule: a broken rule must never suppress a page
                logger.LogWarning(ex,
                    "Alert rule {RuleId} ('{RuleName}') has unreadable actions; it cannot be read as a "
                    + "paging suppression, so incident {IncidentId} pages as normal",
                    rule.Id, rule.Name, incident.Id);
                continue;
            }

            if (actions.Any(a =>
                    a.Type.Equals("suppressnotification", StringComparison.OrdinalIgnoreCase) &&
                    a.SuppressPaging))
            {
                return rule.Name;
            }
        }

        return null;
    }

    private bool MatchesConditions(AlertRule rule, Incident incident)
    {
        List<AlertRuleConditionDto> conditions;
        try
        {
            conditions = JsonSerializer.Deserialize<List<AlertRuleConditionDto>>(rule.ConditionsJson, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Alert rule {RuleId} ('{RuleName}') has unreadable conditions, so it matches nothing and "
                + "will never fire against incident {IncidentId} — fix or disable the rule",
                rule.Id, rule.Name, incident.Id);
            return false;
        }

        if (conditions.Count == 0) return false;

        if (rule.ServiceId.HasValue && incident.ServiceId != rule.ServiceId) return false;
        if (rule.TeamId.HasValue && incident.TeamId != rule.TeamId) return false;

        return conditions.All(c => EvaluateCondition(c, incident));
    }

    private static bool EvaluateCondition(AlertRuleConditionDto condition, Incident incident)
    {
        var fieldValue = GetFieldValue(condition.Field, incident);
        if (fieldValue == null) return false;

        var op = condition.Operator.ToLowerInvariant();
        switch (op)
        {
            case "equals":      return string.Equals(fieldValue, condition.Value, StringComparison.OrdinalIgnoreCase);
            case "notequals":   return !string.Equals(fieldValue, condition.Value, StringComparison.OrdinalIgnoreCase);
            case "contains":    return fieldValue.Contains(condition.Value, StringComparison.OrdinalIgnoreCase);
            case "notcontains": return !fieldValue.Contains(condition.Value, StringComparison.OrdinalIgnoreCase);
            case "greaterthan":
            case "lessthan":
                if (string.Equals(condition.Field, "severity", StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<IncidentSeverity>(fieldValue, true, out var actual)
                    && Enum.TryParse<IncidentSeverity>(condition.Value, true, out var threshold))
                {
                    return op == "greaterthan"
                        ? (int)actual > (int)threshold
                        : (int)actual < (int)threshold;
                }
                return false;
            default:
                return false;
        }
    }

    private static string? GetFieldValue(string field, Incident incident)
    {
        return field.ToLowerInvariant() switch
        {
            "severity" => incident.Severity.ToString(),
            "status" => incident.Status.ToString(),
            "title" => incident.Title,
            "description" => incident.Description,
            "service" => incident.ServiceId?.ToString(),
            "team" => incident.TeamId?.ToString(),
            "source" => incident.SourceIntegrationId?.ToString(),
            _ => null
        };
    }

    private async Task ExecuteActions(AlertRule rule, Incident incident, CancellationToken ct)
    {
        List<AlertRuleActionDto> actions;
        try
        {
            actions = JsonSerializer.Deserialize<List<AlertRuleActionDto>>(rule.ActionsJson, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Alert rule {RuleId} ('{RuleName}') matched incident {IncidentId} but its actions are "
                + "unreadable, so none of them ran",
                rule.Id, rule.Name, incident.Id);
            return;
        }

        foreach (var action in actions)
        {
            try
            {
                // Three of the actions delegate to services that record themselves; the rest change
                // the incident here and would otherwise leave nothing behind.
                var outcome = await ExecuteAction(action, incident, rule.Name, ct);
                if (outcome is not null)
                {
                    await auditLogService.LogAsync(
                        AlertRuleActor, outcome.Action, "Incident", incident.Id.ToString(),
                        outcome.Before, outcome.After,
                        description: $"Alert rule '{rule.Name}': {outcome.What}",
                        cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to execute action '{ActionType}' for rule '{RuleName}'",
                    action.Type, rule.Name);
            }
        }
    }

    /// <summary>What an action changed on the incident itself, or null when it delegated.</summary>
    private sealed record ActionOutcome(AuditAction Action, string What, string? Before, string? After);

    private async Task<ActionOutcome?> ExecuteAction(AlertRuleActionDto action, Incident incident, string ruleName, CancellationToken ct)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "autoescalate":
                if (!string.IsNullOrEmpty(action.Target) && Guid.TryParse(action.Target, out var userId))
                {
                    var incidentService = serviceProvider.GetRequiredService<IIncidentService>();
                    await incidentService.EscalateIncidentAsync(incident.Id, userId.ToString(), $"Auto-escalated by rule: {ruleName}", ct);
                }
                return null;

            case "assignteam":
                if (!string.IsNullOrEmpty(action.Target) && Guid.TryParse(action.Target, out var teamId))
                {
                    var teamBefore = incident.TeamId;
                    incident.TeamId = teamId;

                    // A team assignment must not silently re-start paging that a suppression rule
                    // blocked; an explicit "autoescalate" action still wins.
                    if (!incident.IsEscalationActive && !incident.IsPagingSuppressed)
                    {
                        var policyRepo = serviceProvider.GetRequiredService<IEscalationPolicyRepository>();
                        var policy = await policyRepo.GetActiveForTeamAsync(teamId, ct);
                        if (policy != null)
                        {
                            var orchestrator = serviceProvider.GetRequiredService<IEscalationOrchestrator>();
                            await orchestrator.TriggerEscalationAsync(incident.Id, policy.Id, ct);
                        }
                    }

                    if (teamBefore != teamId)
                        return new ActionOutcome(AuditAction.Updated, "team reassigned",
                            $"TeamId: {teamBefore?.ToString() ?? "none"}", $"TeamId: {teamId}");
                }
                return null;

            case "assignuser":
                if (!string.IsNullOrEmpty(action.Target))
                {
                    var incidentService = serviceProvider.GetRequiredService<IIncidentService>();
                    await incidentService.ReassignIncidentAsync(incident.Id, action.Target, AlertRuleActor, ct);
                }
                return null;

            case "setseverity":
                if (!string.IsNullOrEmpty(action.Value) && Enum.TryParse<IncidentSeverity>(action.Value, true, out var severity))
                {
                    var severityBefore = incident.Severity;
                    incident.Severity = severity;
                    if (severityBefore != severity)
                        return new ActionOutcome(AuditAction.Updated, "severity changed",
                            $"Severity: {severityBefore}", $"Severity: {severity}");
                }
                return null;

            case "addnote":
                if (!string.IsNullOrEmpty(action.Value))
                {
                    await noteService.AddNoteAsync(incident.Id,
                        new Shared.Models.Incidents.CreateIncidentNoteRequest { Content = $"[Auto-Rule: {ruleName}] {action.Value}" },
                        AlertRuleActor, ct);
                }
                return null;

            case "setpriority":
                if (!string.IsNullOrEmpty(action.Value) && Enum.TryParse<IncidentSeverity>(action.Value, true, out var prio))
                {
                    var severityBefore = incident.Severity;
                    incident.Severity = prio;
                    if (severityBefore != prio)
                        return new ActionOutcome(AuditAction.Updated, "severity changed",
                            $"Severity: {severityBefore}", $"Severity: {prio}");
                }
                return null;

            case "suppressnotification":
                if (incident.IsNotificationSuppressed) return null;
                incident.IsNotificationSuppressed = true;
                logger.LogInformation("Alert rule '{RuleName}' suppressed notifications for incident {IncidentId}", ruleName, incident.Id);
                return new ActionOutcome(AuditAction.Suppressed, "notifications suppressed",
                    "IsNotificationSuppressed: False", "IsNotificationSuppressed: True");

            default:
                logger.LogWarning("Unknown alert rule action type: {Type}", action.Type);
                return null;
        }
    }
}
