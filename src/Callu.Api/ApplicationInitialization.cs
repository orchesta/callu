using Microsoft.EntityFrameworkCore;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Seeding;

namespace Callu.Api;

public static class ApplicationInitialization
{
    /// <summary>
    /// Run database migrations and seed initial data. Migration is wrapped in a
    /// Postgres advisory lock (see <see cref="MigrationRunner"/>) so the Worker
    /// host can safely call its own migrate-on-startup in parallel without crashing
    /// on a duplicate-row collision in <c>__EFMigrationsHistory</c>.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await MigrationRunner.RunAsync(dbContext, logger);

            var seeder = scope.ServiceProvider.GetRequiredService<IDbSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating or seeding the database.");
            throw;
        }
    }
}
