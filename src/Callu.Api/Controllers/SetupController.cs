using System.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Seeding;
using Callu.Shared.Localization;
using Callu.Shared.Models.Settings;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

/// <summary>
/// Initial setup endpoint — only works when no admin user exists.
/// Used for first-time installation to create the administrator account.
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/setup")]
[EnableRateLimiting("auth")]
public class SetupController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IDbSeeder dbSeeder,
    ILogger<SetupController> logger) : ControllerBase
{
    private const long SetupAdvisoryLockKey = 72_345_678_123_456L;

    /// <summary>
    /// Check if initial setup is required (no admin exists)
    /// </summary>
    [HttpGet("status")]
    [DisableRateLimiting]
    public async Task<IActionResult> GetSetupStatus(CancellationToken ct)
    {
        var admins = await userManager.GetUsersInRoleAsync("Admin");
        var isSetupRequired = admins == null || admins.Count == 0;

        return Ok(new
        {
            setupRequired = isSetupRequired,
            message = isSetupRequired
                ? "Initial setup is required. Please configure your admin account."
                : "System is already configured."
        });
    }

    /// <summary>
    /// Perform initial setup — create the administrator user.
    /// This endpoint is automatically disabled once an admin exists.
    /// Concurrency-safe across replicas: a PostgreSQL transaction-scoped
    /// advisory lock serializes concurrent first-boot requests, and the
    /// admin-existence check runs inside a Serializable transaction so
    /// two replicas cannot both see "no admin" and both create one.
    /// </summary>
    [HttpPost("initial")]
    public async Task<IActionResult> InitialSetup([FromBody] InitialSetupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse.Fail(Messages.Get("setup.fieldsRequired")));
        }

        IActionResult? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_xact_lock({SetupAdvisoryLockKey})", ct);

                var adminExists = await (from u in db.Users
                                         join ur in db.UserRoles on u.Id equals ur.UserId
                                         join r in db.Roles on ur.RoleId equals r.Id
                                         where r.Name == "Admin"
                                         select u.Id).AnyAsync(ct);

                if (adminExists)
                {
                    await tx.RollbackAsync(ct);
                    result = BadRequest(ApiResponse.Fail(Messages.Get("setup.alreadyConfigured")));
                    return;
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
                    result = BadRequest(ApiResponse.Fail($"Failed to create admin user: {errors}"));
                    return;
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
                        CreatedAt = DateTime.UtcNow
                    };
                    db.OrganizationSettings.Add(settings);
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                logger.LogInformation("Initial setup completed. Admin: {Email}", request.Email);

                result = Ok(new
                {
                    message = Messages.Get("setup.completed"),
                    admin = new { email = request.Email }
                });
            });

            return result ?? StatusCode(500, new { message = Messages.Get("setup.unexpectedError") });
        }
        catch (DbUpdateException ex) when (IsSerializationFailure(ex))
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

    private static bool IsSerializationFailure(Exception ex) =>
        ex.GetBaseException() is Npgsql.PostgresException pg && pg.SqlState == "40001";
}
