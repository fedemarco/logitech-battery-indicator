using System.Text.Json.Nodes;

namespace LogitechBattery.Tests;

/// <summary>A real answer of the GitHub "latest release" API (release v0.7.1), and copies of it with one field changed.</summary>
internal static class LatestReleaseSample
{
    public const string Tag = "v0.7.1";
    public const string PageUrl = "https://github.com/fedemarco/logitech-battery-indicator/releases/tag/v0.7.1";

    public static string Json { get; } = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Updates", "latest-release.json"));

    public static string JsonWith(string field, JsonNode? value)
    {
        var release = JsonNode.Parse(Json)!.AsObject();
        release[field] = value;
        return release.ToJsonString();
    }

    public static string JsonWithout(string field)
    {
        var release = JsonNode.Parse(Json)!.AsObject();
        release.Remove(field);
        return release.ToJsonString();
    }
}
