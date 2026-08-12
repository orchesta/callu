using Callu.Shared;
using System.Reflection;

namespace Callu.Tests;

public class BuildIdentityTests
{
    [Fact]
    public void From_UsesAssemblyVersionAndInformationalVersion()
    {
        var info = BuildIdentity.From(typeof(BuildIdentity).Assembly);

        Assert.Equal("1.2.0", info.Version);
        Assert.StartsWith("1.2.0", info.InformationalVersion);
    }

    [Fact]
    public void ApiEntryAssembly_CarriesTheReleasedVersion()
    {
        var api = typeof(Callu.Api.Controllers.HealthController).Assembly;
        var info = BuildIdentity.From(api);

        Assert.Equal("1.2.0", info.Version);
        Assert.False(string.IsNullOrWhiteSpace(info.InformationalVersion));
        Assert.StartsWith(info.Version, info.InformationalVersion);
    }

    [Fact]
    public void InformationalVersionAttribute_IsPresentOnShared()
    {
        var attr = typeof(BuildIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        Assert.NotNull(attr);
        Assert.False(string.IsNullOrWhiteSpace(attr!.InformationalVersion));
    }
}
