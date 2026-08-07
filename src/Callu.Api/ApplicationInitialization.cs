using Microsoft.EntityFrameworkCore;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Seeding;

namespace Callu.Api;

public static class ApplicationInitialization
{
    /// <summary>Runs database migrations and seeds initial data, both inside <see cref="MigrationRunner"/>'s advisory lock.</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            using var scope = app.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seeder = scope.ServiceProvider.GetRequiredService<IDbSeeder>();

            await MigrationRunner.RunAsync(dbContext, logger, seeder.SeedAsync);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating or seeding the database.");
            throw;
        }
    }
}
