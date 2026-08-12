using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Infrastructure.Email;
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
    private readonly IEmailTemplateRepository _emailTemplateRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DbSeeder> _logger;

    private static readonly string[] Roles = { "Admin", "TeamLead", "Member", "Viewer", "Auditor" };

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
            "CanExecuteServiceActions",
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
            "CanExecuteServiceActions",
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
            "CanExecuteServiceActions",
            "CanViewRunbooks", "CanViewPostmortems",
            "CanViewReports"
        },
        ["Viewer"] = new[]
        {
            "CanViewServices", "CanViewTeams", "CanViewIncidents",
            "CanViewEscalations", "CanViewSchedules", "CanViewCallLogs",
            "CanViewRunbooks", "CanViewPostmortems",
            "CanViewReports"
        },
        // Reads everything, changes nothing — acknowledge and resolve are changes, so an auditor
        // does not get them. Showing an auditor the audit log used to require making them an Admin.
        ["Auditor"] = new[]
        {
            "CanViewAuditLog",
            "CanViewServices", "CanViewTeams", "CanViewIncidents",
            "CanViewEscalations", "CanViewSchedules", "CanViewCallLogs",
            "CanViewRunbooks", "CanViewPostmortems",
            "CanViewReports"
        }
    };

    private static readonly string[] RetiredPermissionClaims = Array.Empty<string>();

    private static readonly HashSet<string> ManagedPermissionClaims =
        RoleClaims.Values
            .SelectMany(c => c)
            .Concat(RetiredPermissionClaims)
            .ToHashSet(StringComparer.Ordinal);

    public DbSeeder(
        RoleManager<ApplicationRole> roleManager,
        IWebhookTemplateRepository webhookTemplateRepo,
        IEmailTemplateRepository emailTemplateRepo,
        IUnitOfWork unitOfWork,
        ILogger<DbSeeder> logger)
    {
        _roleManager = roleManager;
        _webhookTemplateRepo = webhookTemplateRepo;
        _emailTemplateRepo = emailTemplateRepo;
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
        await SeedEmailTemplatesAsync();

        _logger.LogInformation("Database seeding completed.");
    }

    /// <summary>
    /// Upsert the built-in Prometheus / Grafana / Generic webhook templates; custom rows
    /// (<see cref="WebhookTemplate.IsBuiltIn"/> = false) are never touched.
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
        {
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _logger.LogWarning(ex,
                    "Webhook template seeding hit a unique-key conflict (concurrent seeder?); templates already exist, continuing.");
                return;
            }
        }

        _logger.LogInformation(
            "Webhook template seeding: added {Added}, refreshed {Touched}", added, touched);
    }

    /// <summary>
    /// Seed the built-in email templates, insert-if-missing only so operator edits survive an upgrade.
    /// </summary>
    public async Task SeedEmailTemplatesAsync()
    {
        var added = 0;
        var renamed = 0;

        foreach (var seed in EmailTemplates.GetSeedTemplates())
        {
            var existing = await _emailTemplateRepo.GetByKeyAsync(seed.Key);

            if (existing is not null && !existing.IsSystem)
            {
                // Unique suffix: a plain "{key}-legacy" could itself collide with the
                // filtered unique index (e.g. renamed on a previous upgrade attempt).
                var legacyKey = $"{seed.Key}-legacy-{Guid.NewGuid():N}"[..Math.Min(100, seed.Key.Length + 40)];
                _logger.LogWarning(
                    "Email template key '{Key}' is occupied by a non-system row (legacy test-send artifact); renaming it to '{LegacyKey}' and deactivating it.",
                    seed.Key, legacyKey);
                existing.Key = legacyKey;
                existing.IsActive = false;
                existing.UpdatedAt = DateTime.UtcNow;
                _emailTemplateRepo.Update(existing);
                renamed++;
                existing = null;
            }

            if (existing is null)
            {
                await _emailTemplateRepo.AddAsync(new EmailTemplate
                {
                    Id = Guid.NewGuid(),
                    Name = seed.Name,
                    Key = seed.Key,
                    Subject = seed.Subject,
                    HtmlBody = seed.HtmlBody,
                    Description = seed.Description,
                    IsSystem = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                added++;
            }
        }

        if (added > 0 || renamed > 0)
        {
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another host seeded concurrently (unique Key index) — the templates exist,
                // which is the desired end state. Never crash-loop startup over seeding.
                _logger.LogWarning(ex,
                    "Email template seeding hit a unique-key conflict (concurrent seeder?); templates already exist, continuing.");
                return;
            }
        }

        _logger.LogInformation(
            "Email template seeding: added {Added}, legacy rows renamed {Renamed}", added, renamed);
    }

    /// <inheritdoc />
    public async Task SeedRolesAsync()
    {
        foreach (var role in Roles)
        {
            if (await _roleManager.RoleExistsAsync(role))
                continue;

            try
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
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another host created the role between the check and the insert
                // (AspNetRoles.NormalizedName is unique). Desired end state either way.
                _logger.LogWarning(ex,
                    "Role {Role} was created concurrently by another host; continuing.", role);
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    /// <inheritdoc />
    public async Task SeedRoleClaimsAsync()
    {
        foreach (var (roleName, claims) in RoleClaims)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null) continue;

            var existingClaims = await _roleManager.GetClaimsAsync(role);
            var desired = new HashSet<string>(claims, StringComparer.Ordinal);

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

            foreach (var claim in existingClaims)
            {
                if (desired.Contains(claim.Type))
                    continue;
                if (!ManagedPermissionClaims.Contains(claim.Type))
                    continue;

                var result = await _roleManager.RemoveClaimAsync(role, claim);
                if (result.Succeeded)
                {
                    _logger.LogInformation("Removed stale claim {Claim} from role {Role}", claim.Type, roleName);
                }
                else
                {
                    _logger.LogWarning("Failed to remove claim {Claim} from role {Role}: {Errors}",
                        claim.Type, roleName, string.Join(", ", result.Errors.Select(e => e.Description)));
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
