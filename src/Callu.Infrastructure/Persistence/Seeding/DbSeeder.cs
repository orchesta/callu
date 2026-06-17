using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Services;

namespace Callu.Infrastructure.Persistence.Seeding;

/// <summary>
/// Database seeder implementation for initial data setup
/// </summary>
public class DbSeeder : IDbSeeder
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IWebhookTemplateRepository _webhookTemplateRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DbSeeder> _logger;

    private static readonly string[] Roles = { "Admin", "TeamLead", "Member", "Viewer" };

    private static readonly Dictionary<string, string[]> RoleClaims = new()
    {
        ["Admin"] =
        [
            "CanManageSettings", "CanManageUsers",
            "CanManageBilling", "CanManageIntegrations", "CanViewAuditLog",
            "CanManageServices", "CanViewServices", "CanManageWebhooks",
            "CanManageTeams", "CanViewTeams",
            "CanManageIncidents", "CanViewIncidents",
            "CanAcknowledgeIncidents", "CanResolveIncidents", "CanViewCallLogs",
            "CanManageEscalations", "CanViewEscalations",
            "CanManageSchedules", "CanViewSchedules",
            "CanManageRunbooks", "CanViewRunbooks",
            "CanManagePostmortems", "CanViewPostmortems",
            "CanViewReports"
        ],
        ["TeamLead"] = new[]
        {
            "CanManageServices", "CanViewServices", "CanManageWebhooks",
            "CanManageTeams", "CanViewTeams",
            "CanManageIncidents", "CanViewIncidents",
            "CanAcknowledgeIncidents", "CanResolveIncidents", "CanViewCallLogs",
            "CanManageEscalations", "CanViewEscalations",
            "CanManageSchedules", "CanViewSchedules",
            "CanManageRunbooks", "CanViewRunbooks",
            "CanManagePostmortems", "CanViewPostmortems",
            "CanViewReports"
        },
        ["Member"] = new[]
        {
            "CanViewServices", "CanViewTeams", "CanViewIncidents",
            "CanViewEscalations", "CanViewSchedules", "CanViewCallLogs",
            "CanAcknowledgeIncidents", "CanResolveIncidents",
            "CanViewRunbooks", "CanViewPostmortems",
            "CanViewReports"
        },
        ["Viewer"] = new[]
        {
            "CanViewServices", "CanViewTeams", "CanViewIncidents",
            "CanViewEscalations", "CanViewSchedules", "CanViewCallLogs",
            "CanViewRunbooks", "CanViewPostmortems",
            "CanViewReports"
        }
    };

    public DbSeeder(
        RoleManager<ApplicationRole> roleManager,
        IWebhookTemplateRepository webhookTemplateRepo,
        IUnitOfWork unitOfWork,
        ILogger<DbSeeder> logger)
    {
        _roleManager = roleManager;
        _webhookTemplateRepo = webhookTemplateRepo;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SeedAsync()
    {
        _logger.LogInformation("Starting database seeding...");

        await SeedRolesAsync();
        await SeedRoleClaimsAsync();
        await SeedDefaultSettingsAsync();
        await SeedWebhookTemplatesAsync();

        _logger.LogInformation("Database seeding completed.");
    }

    /// <summary>
    /// Upsert the built-in Prometheus / Grafana / Generic templates so the UI
    /// dropdown isn't empty on a fresh install. Idempotent: existing built-in
    /// rows have their content refreshed (FieldMappings / StateMapping /
    /// SamplePayload / Description), but custom rows (<see
    /// cref="WebhookTemplate.IsBuiltIn"/> = false) are never touched.
    /// Fix 10.P1-1.
    /// </summary>
    public async Task SeedWebhookTemplatesAsync()
    {
        var defaults = BuiltInWebhookTemplates.GetAll().ToList();
        var touched = 0;
        var added = 0;

        foreach (var t in defaults)
        {
            var existing = await _webhookTemplateRepo.GetByNameAsync(t.Name);
            if (existing is null)
            {
                await _webhookTemplateRepo.AddAsync(new WebhookTemplate
                {
                    Id = Guid.NewGuid(),
                    Name = t.Name,
                    Description = t.Description,
                    FieldMappings = t.FieldMappings,
                    StateMapping = t.StateMapping,
                    SamplePayload = t.SamplePayload,
                    IsBuiltIn = true,
                    IsActive = true,
                    DataLanguage = "en-US",
                    CreatedAt = DateTime.UtcNow
                });
                added++;
            }
            else if (existing.IsBuiltIn)
            {
                existing.FieldMappings = t.FieldMappings;
                existing.StateMapping = t.StateMapping;
                existing.SamplePayload = t.SamplePayload;
                existing.Description = t.Description;
                existing.UpdatedAt = DateTime.UtcNow;
                _webhookTemplateRepo.Update(existing);
                touched++;
            }
        }

        if (added > 0 || touched > 0)
            await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation(
            "Webhook template seeding: added {Added}, refreshed {Touched}", added, touched);
    }

    /// <inheritdoc />
    public async Task SeedRolesAsync()
    {
        foreach (var role in Roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                var result = await _roleManager.CreateAsync(new ApplicationRole(role));
                if (result.Succeeded)
                {
                    _logger.LogInformation("Created role: {Role}", role);
                }
                else
                {
                    _logger.LogWarning("Failed to create role {Role}: {Errors}", 
                        role, string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task SeedRoleClaimsAsync()
    {
        foreach (var (roleName, claims) in RoleClaims)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null) continue;

            var existingClaims = await _roleManager.GetClaimsAsync(role);
            
            foreach (var claimType in claims)
            {
                if (existingClaims.Any(c => c.Type == claimType))
                    continue;
                
                var result = await _roleManager.AddClaimAsync(role, new Claim(claimType, "true"));
                if (result.Succeeded)
                {
                    _logger.LogInformation("Added claim {Claim} to role {Role}", claimType, roleName);
                }
                else
                {
                    _logger.LogWarning("Failed to add claim {Claim} to role {Role}: {Errors}", 
                        claimType, roleName, string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    /// <inheritdoc />
    public Task SeedDefaultSettingsAsync()
    {
        _logger.LogInformation("Default settings seeding completed.");
        return Task.CompletedTask;
    }
}
