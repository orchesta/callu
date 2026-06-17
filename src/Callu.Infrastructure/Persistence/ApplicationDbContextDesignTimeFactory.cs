using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Callu.Infrastructure.Persistence;

/// <summary>
/// Design-time factory consumed only by EF Core tooling (<c>dotnet ef migrations add</c>,
/// <c>dotnet ef database update</c>). Runtime DI uses
/// <see cref="DI.PersistenceModule.AddPersistenceModule"/> — this factory does not
/// affect production behaviour.
///
/// Connection string lookup order:
///   1. <c>CALLU_DESIGN_CONNECTION</c> env var (explicit override for CI/scripts)
///   2. <c>ConnectionStrings__DefaultConnection</c> env var (matches runtime config)
///   3. A no-op localhost fallback so migration generation works without any env vars set.
/// </summary>
public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CALLU_DESIGN_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=calludb;Username=callu;Password=design-time-only";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsql.UseNodaTime();
            })
            .Options;

        return new ApplicationDbContext(options);
    }
}
