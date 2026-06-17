using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Shared.Models.AlertRules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// CRUD service for alert automation rules
/// </summary>
public class AlertRuleService(
    IRepository<AlertRule> ruleRepo,
    IUnitOfWork unitOfWork,
    ILogger<AlertRuleService> logger) : IAlertRuleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<List<AlertRuleDto>> GetRulesAsync(CancellationToken ct = default)
    {
        var rules = await ruleRepo.GetQueryable()
            .Include(r => r.Service)
            .Include(r => r.Team)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);

        return rules.Select(MapToDto).ToList();
    }

    public async Task<AlertRuleDto?> GetRuleAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await ruleRepo.GetQueryable()
            .Include(r => r.Service)
            .Include(r => r.Team)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return rule == null ? null : MapToDto(rule);
    }

    public async Task<AlertRuleDto> CreateRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default)
    {
        var rule = new AlertRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            IsEnabled = request.IsEnabled,
            Priority = request.Priority,
            ConditionsJson = JsonSerializer.Serialize(request.Conditions, JsonOptions),
            ActionsJson = JsonSerializer.Serialize(request.Actions, JsonOptions),
            ServiceId = request.ServiceId,
            TeamId = request.TeamId,
            CreatedAt = DateTime.UtcNow
        };

        await ruleRepo.AddAsync(rule, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Alert rule created: {RuleName} (ID: {RuleId})", rule.Name, rule.Id);
        return MapToDto(rule);
    }

    public async Task<bool> UpdateRuleAsync(Guid id, UpdateAlertRuleRequest request, CancellationToken ct = default)
    {
        var rule = await ruleRepo.GetByIdAsync(id, ct);
        if (rule == null) return false;

        rule.Name = request.Name;
        rule.Description = request.Description;
        rule.IsEnabled = request.IsEnabled;
        rule.Priority = request.Priority;
        rule.ConditionsJson = JsonSerializer.Serialize(request.Conditions, JsonOptions);
        rule.ActionsJson = JsonSerializer.Serialize(request.Actions, JsonOptions);
        rule.ServiceId = request.ServiceId;
        rule.TeamId = request.TeamId;
        rule.UpdatedAt = DateTime.UtcNow;

        ruleRepo.Update(rule);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Alert rule updated: {RuleName} (ID: {RuleId})", rule.Name, rule.Id);
        return true;
    }

    public async Task<bool> DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await ruleRepo.GetByIdAsync(id, ct);
        if (rule == null) return false;

        rule.IsDeleted = true;
        rule.UpdatedAt = DateTime.UtcNow;
        ruleRepo.Update(rule);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Alert rule deleted: {RuleName} (ID: {RuleId})", rule.Name, rule.Id);
        return true;
    }

    public async Task<bool> ToggleRuleAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await ruleRepo.GetByIdAsync(id, ct);
        if (rule == null) return false;

        rule.IsEnabled = !rule.IsEnabled;
        rule.UpdatedAt = DateTime.UtcNow;
        ruleRepo.Update(rule);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Alert rule toggled: {RuleName} → {State}", rule.Name, rule.IsEnabled ? "Enabled" : "Disabled");
        return true;
    }

    private static AlertRuleDto MapToDto(AlertRule rule)
    {
        var conditions = new List<AlertRuleConditionDto>();
        var actions = new List<AlertRuleActionDto>();

        try
        {
            conditions = JsonSerializer.Deserialize<List<AlertRuleConditionDto>>(rule.ConditionsJson, JsonOptions) ?? [];
        }
        catch { }

        try
        {
            actions = JsonSerializer.Deserialize<List<AlertRuleActionDto>>(rule.ActionsJson, JsonOptions) ?? [];
        }
        catch { }

        return new AlertRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            Description = rule.Description,
            IsEnabled = rule.IsEnabled,
            Priority = rule.Priority,
            Conditions = conditions,
            Actions = actions,
            ServiceId = rule.ServiceId,
            ServiceName = rule.Service?.Name,
            TeamId = rule.TeamId,
            TeamName = rule.Team?.Name,
            TriggerCount = rule.TriggerCount,
            LastTriggeredAt = rule.LastTriggeredAt,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }
}
