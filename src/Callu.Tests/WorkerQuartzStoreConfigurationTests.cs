using Callu.Worker.Quartz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Tests;

public class WorkerQuartzStoreConfigurationTests
{
    private static IConfiguration Config(string? usePersistentStore = null, string? connectionString = null)
    {
        var values = new Dictionary<string, string?>();

        if (usePersistentStore is not null) values["Quartz:UsePersistentStore"] = usePersistentStore;
        if (connectionString is not null) values["ConnectionStrings:DefaultConnection"] = connectionString;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("true", null)]
    [InlineData("true", "")]
    [InlineData("true", "   ")]
    [InlineData("True", null)]
    public void AskingForTheClusteredStoreWithNoConnectionString_RefusesToStart(string flag, string? connectionString)
    {
        var thrown = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddCalluWorkerQuartzScheduling(Config(flag, connectionString)));

        Assert.Contains("Quartz:UsePersistentStore", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("DefaultConnection", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClusteredStoreWithAConnectionString_IsAccepted()
    {
        var services = new ServiceCollection()
            .AddCalluWorkerQuartzScheduling(Config("true", "Host=db; Database=calludb; Username=callu; Password=x"));

        Assert.NotEmpty(services);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("false", null)]
    [InlineData("false", "Host=db; Database=calludb; Username=callu; Password=x")]
    public void TheInMemoryStore_StaysTheDefaultAndAsksForNothing(string? flag, string? connectionString)
    {
        var services = new ServiceCollection().AddCalluWorkerQuartzScheduling(Config(flag, connectionString));

        Assert.NotEmpty(services);
    }
}
