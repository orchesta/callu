using System.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Seeding;
using Callu.Shared.Localization;
using Callu.Shared.Models.Settings;
using Callu.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Callu.Shared.Logging;

namespace Callu.Api.Controllers;

/// <summary>
/// Initial setup endpoint — only works when no admin user exists.
/// Used for first-time installation to create the administrator account.
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/setup")]
[EnableRateLimiting("auth")]
[AllowAnonymous]
public class SetupController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IDbSeeder dbSeeder,
    Callu.Api.Services.SetupCompletionLatch setupLatch,
    ILogger<SetupController> logger) : ControllerBase
{
    private const long SetupAdvisoryLockKey = 72_345_678_123_456L;

    private async Task<bool> IsSetupCompleteAsync(CancellationToken ct)
    {
        if (setupLatch.IsComplete)
            return true;

        var adminExists = await (from u in db.Users.IgnoreQueryFilters()
                                 join ur in db.UserRoles on u.Id equals ur.UserId
                                 join r in db.Roles on ur.RoleId equals r.Id
                                 where r.Name == "Admin"
                                 select u.Id).AnyAsync(ct);

        var complete = adminExists || await db.OrganizationSettings
            .AnyAsync(s => s.Id == Callu.Domain.Entities.OrganizationSettings.SingletonId, ct);

        if (complete)
            setupLatch.MarkComplete();

        return complete;
    }

    /// <summary>
    /// Check if initial setup is required (no admin exists).
    /// </summary>
    [HttpGet("status")]
    [EnableRateLimiting("statuspage_view")]
    public async Task<IActionResult> GetSetupStatus(CancellationToken ct)
    {
        var isSetupRequired = !await IsSetupCompleteAsync(ct);

        return Ok(new
        {
            setupRequired = isSetupRequired,
            message = isSetupRequired
                ? "Initial setup is required. Please configure your admin account."
                : "System is already configured."
        });
    }

    /// <summary>Creates the administrator user on first boot, then self-disables; an advisory lock plus a
    /// Serializable transaction stop two replicas both seeing "no admin" and both creating one.</summary>
    [HttpPost("initial")]
    public async Task<IActionResult> InitialSetup([FromBody] InitialSetupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse.Fail(Messages.Get("setup.fieldsRequired")));
        }

        // No execution strategy on purpose: the concurrency guard is the advisory lock plus
        // Serializable below, and this context is registered without EnableRetryOnFailure anyway.
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            await db.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_xact_lock({SetupAdvisoryLockKey})", ct);

            if (await IsSetupCompleteAsync(ct))
            {
                await tx.RollbackAsync(ct);
                return BadRequest(ApiResponse.Fail(Messages.Get("setup.alreadyConfigured")));
            }

            await dbSeeder.SeedRolesAsync();
            await dbSeeder.SeedRoleClaimsAsync();

            var admin = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.Name ?? "Administrator",
                FirstName = request.Name?.Split(' ').FirstOrDefault() ?? "System",
                LastName = request.Name?.Split(' ').Skip(1).FirstOrDefault() ?? "Administrator",
                Timezone = request.DefaultTimezone ?? "UTC",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(admin, request.Password);
            if (!createResult.Succeeded)
            {
                await tx.RollbackAsync(ct);
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                logger.LogWarning("Initial setup failed: {Errors}", errors);
                return BadRequest(ApiResponse.Fail($"Failed to create admin user: {errors}"));
            }

            await userManager.AddToRoleAsync(admin, "Admin");

            var settings = await db.OrganizationSettings
                .FirstOrDefaultAsync(s => s.Id == Callu.Domain.Entities.OrganizationSettings.SingletonId, ct);
            if (settings is null)
            {
                settings = new Callu.Domain.Entities.OrganizationSettings
                {
                    Id = Callu.Domain.Entities.OrganizationSettings.SingletonId,
                    DefaultTimezone = request.DefaultTimezone ?? "UTC",
                    BaseUrl = ResolveBaseUrl(request.BaseUrl),
                    CreatedAt = DateTime.UtcNow
                };
                db.OrganizationSettings.Add(settings);
                await db.SaveChangesAsync(ct);
            }
            else if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                settings.BaseUrl = ResolveBaseUrl(request.BaseUrl);
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            setupLatch.MarkComplete();

            logger.LogInformation("Initial setup completed. Admin: {Email}", PiiRedactor.Email(request.Email));

            return Ok(new
            {
                message = Messages.Get("setup.completed"),
                admin = new { email = request.Email }
            });
        }
        // Losing the Serializable race means the other replica created the admin — "already configured",
        // not a 500. Matched on the base exception: the 40001 often arrives unwrapped, from the COMMIT.
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            logger.LogWarning(ex, "Initial setup serialization conflict — another replica won");
            return BadRequest(ApiResponse.Fail(Messages.Get("setup.alreadyConfigured")));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Initial setup failed unexpectedly");
            return StatusCode(500, new { message = Messages.Get("setup.unexpectedError") });
        }
    }

    /// <summary>True for the two Postgres states that mean "another transaction won": 40001 and 40P01.</summary>
    /// <summary>The public address to put in notification links, from the operator or the request.</summary>
    // Taken once, here, rather than from the Host header on every request: behind a proxy that
    // header is attacker-controlled, and it ends up in links we email out.
    private string? ResolveBaseUrl(string? supplied)
    {
        if (!string.IsNullOrWhiteSpace(supplied))
            return supplied.Trim().TrimEnd('/');

        var request = HttpContext?.Request;
        if (request is null || !request.Host.HasValue) return null;

        // Clipped rather than rejected: this value is derived, not typed, so an over-long Host header
        // must not be the reason first-run setup fails.
        var derived = $"{request.Scheme}://{request.Host.Value}";
        return derived.Length <= OrganizationSettings.MaxBaseUrlLength
            ? derived
            : derived[..OrganizationSettings.MaxBaseUrlLength];
    }

    private static bool IsSerializationFailure(Exception ex) =>
        ex.GetBaseException() is Npgsql.PostgresException { SqlState: "40001" or "40P01" };
}
