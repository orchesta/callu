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
    ITransactionManager transactionManager,
    ILogger<AlertRuleEngine> logger) : IAlertRuleEngine
{
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

    private bool MatchesConditions(AlertRule rule, Incident incident)
    {
        List<AlertRuleConditionDto> conditions;
        try
        {
            conditions = JsonSerializer.Deserialize<List<AlertRuleConditionDto>>(rule.ConditionsJson, JsonOptions) ?? [];
        }
        catch
        {
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
            "source" => incident.ServiceId?.ToString(),
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
        catch
        {
            return;
        }

        foreach (var action in actions)
        {
            try
            {
                await ExecuteAction(action, incident, rule.Name, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to execute action '{ActionType}' for rule '{RuleName}'",
                    action.Type, rule.Name);
            }
        }
    }

    private async Task ExecuteAction(AlertRuleActionDto action, Incident incident, string ruleName, CancellationToken ct)
    {
        switch (action.Type.ToLowerInvariant())
        {
            case "autoescalate":
                if (!string.IsNullOrEmpty(action.Target) && Guid.TryParse(action.Target, out var userId))
                {
                    var incidentService = serviceProvider.GetRequiredService<IIncidentService>();
                    await incidentService.EscalateIncidentAsync(incident.Id, userId.ToString(), $"Auto-escalated by rule: {ruleName}", ct);
                }
                break;

            case "assignteam":
                if (!string.IsNullOrEmpty(action.Target) && Guid.TryParse(action.Target, out var teamId))
                {
                    incident.TeamId = teamId;

                    if (!incident.IsEscalationActive)
                    {
                        var policyRepo = serviceProvider.GetRequiredService<IEscalationPolicyRepository>();
                        var policy = await policyRepo.FindSingleAsync(
                            p => p.TeamId == teamId && p.IsActive && !p.IsDeleted, ct);
                        if (policy != null)
                        {
                            var orchestrator = serviceProvider.GetRequiredService<IEscalationOrchestrator>();
                            await orchestrator.TriggerEscalationAsync(incident.Id, policy.Id, ct);
                        }
                    }
                }
                break;

            case "assignuser":
                if (!string.IsNullOrEmpty(action.Target))
                {
                    var incidentService = serviceProvider.GetRequiredService<IIncidentService>();
                    await incidentService.ReassignIncidentAsync(incident.Id, action.Target, "system", ct);
                }
                break;

            case "setseverity":
                if (!string.IsNullOrEmpty(action.Value) && Enum.TryParse<IncidentSeverity>(action.Value, true, out var severity))
                {
                    incident.Severity = severity;
                }
                break;

            case "addnote":
                if (!string.IsNullOrEmpty(action.Value))
                {
                    await noteService.AddNoteAsync(incident.Id,
                        new Shared.Models.Incidents.CreateIncidentNoteRequest { Content = $"[Auto-Rule: {ruleName}] {action.Value}" },
                        "system", ct);
                }
                break;

            case "setpriority":
                if (!string.IsNullOrEmpty(action.Value) && Enum.TryParse<IncidentSeverity>(action.Value, true, out var prio))
                {
                    incident.Severity = prio;
                }
                break;

            case "suppressnotification":
                incident.IsNotificationSuppressed = true;
                logger.LogInformation("Alert rule '{RuleName}' suppressed notifications for incident {IncidentId}", ruleName, incident.Id);
                break;

            default:
                logger.LogWarning("Unknown alert rule action type: {Type}", action.Type);
                break;
        }
    }
}
