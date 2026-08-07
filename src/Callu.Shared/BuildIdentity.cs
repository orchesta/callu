using System.Reflection;

namespace Callu.Shared;

/// <summary>Product version stamped at build time (Directory.Build.props + optional git SHA).</summary>
public static class BuildIdentity
{
    public static BuildIdentityInfo Current { get; } =
        From(Assembly.GetEntryAssembly() ?? typeof(BuildIdentity).Assembly);

    public static BuildIdentityInfo From(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var assemblyVersion = assembly.GetName().Version;
        var version = assemblyVersion is null
            ? "0.0.0"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            informational = version;

        return new BuildIdentityInfo(version, informational);
    }
}

public sealed record BuildIdentityInfo(string Version, string InformationalVersion);
