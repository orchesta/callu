namespace Callu.Infrastructure.Persistence.Seeding;

/// <summary>
/// Database seeder interface for initial data setup
/// </summary>
public interface IDbSeeder
{
    /// <summary>
    /// Run all seeding operations
    /// </summary>
    Task SeedAsync();
    
    /// <summary>
    /// Seed identity roles
    /// </summary>
    Task SeedRolesAsync();
    
    /// <summary>
    /// Seed role claims (permissions) for each role
    /// </summary>
    Task SeedRoleClaimsAsync();
    
    /// <summary>
    /// Seed default application settings
    /// </summary>
    Task SeedDefaultSettingsAsync();
}
