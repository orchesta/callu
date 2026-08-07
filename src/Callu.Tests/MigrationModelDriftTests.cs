using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Callu.Tests;

/// <summary>Fails the build the moment the EF model diverges from the last migration snapshot.</summary>
public class MigrationModelDriftTests
{
    [Fact]
    public void Model_HasNoPendingChanges_AgainstLastMigration()
    {
        var factory = new ApplicationDbContextDesignTimeFactory();
        using var db = factory.CreateDbContext(Array.Empty<string>());

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The EF Core model has changes not captured by a migration. Run "
            + "'dotnet ef migrations add <Name> --project Callu.Infrastructure --startup-project Callu.Api' "
            + "and commit the generated migration.");
    }
}
