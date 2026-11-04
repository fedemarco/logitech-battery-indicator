using System.Reflection;

namespace LogitechBattery;

public static class AppVersion
{
    /// <summary>The assembly's informational version (semantic version, plus "-dev.N+sha" on CI builds); null if absent.</summary>
    public static string? InformationalVersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
