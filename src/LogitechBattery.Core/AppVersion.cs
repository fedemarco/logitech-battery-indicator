using System.Reflection;

namespace LogitechBattery;

public static class AppVersion
{
    private const int ShortShaLength = 7;

    /// <summary>The assembly's informational version (semantic version, plus "-dev.N+sha" on CI builds); null if absent.</summary>
    public static string? InformationalVersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// Releases show the semantic version only; dev builds (prerelease part) also keep a short commit so builds can be told apart.
    /// </summary>
    public static string Display(string? informationalVersion)
    {
        if (informationalVersion is null)
            return "unknown";

        var plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return informationalVersion;

        var isDevBuild = informationalVersion[..plus].Contains('-');
        return isDevBuild
            ? informationalVersion[..Math.Min(informationalVersion.Length, plus + 1 + ShortShaLength)]
            : informationalVersion[..plus];
    }
}
