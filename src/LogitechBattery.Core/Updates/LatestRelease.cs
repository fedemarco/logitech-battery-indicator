using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogitechBattery.Updates;

/// <summary>A published release read from the GitHub API answer.</summary>
public sealed record LatestRelease(Version Version, string PageUrl)
{
    // The page address is built here from the validated tag and never taken from the answer: it is opened in the browser.
    internal const string ReleasePagePrefix = "https://github.com/fedemarco/logitech-battery-indicator/releases/tag/";

    // ASCII digits only (\d also matches Arabic-Indic digits) and \z, because $ also matches before a final newline.
    private static readonly Regex TagPattern = new(@"^v([0-9]{1,9})\.([0-9]{1,9})\.([0-9]{1,9})\z");

    /// <summary>Accepts only a published, final release with a vX.Y.Z tag. Never throws.</summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out LatestRelease? release, [NotNullWhen(false)] out string? problem)
    {
        release = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryRead(document.RootElement, out release, out problem);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            problem = "the answer is not valid JSON";
            return false;
        }
    }

    private static bool TryRead(JsonElement root, out LatestRelease? release, [NotNullWhen(false)] out string? problem)
    {
        release = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            problem = "the answer is not a JSON object";
            return false;
        }

        if (!TryGetString(root, "tag_name", out var tag) || !TryReadVersion(tag, out var version))
        {
            problem = "the release has no version tag like v1.2.3";
            return false;
        }

        if (!IsFalse(root, "draft") || !IsFalse(root, "prerelease"))
        {
            problem = "the release is a draft or a prerelease";
            return false;
        }

        release = new LatestRelease(version, ReleasePagePrefix + tag);
        problem = null;
        return true;
    }

    private static bool TryReadVersion(string tag, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        var match = TagPattern.Match(tag);
        if (!match.Success || !TryReadNumber(match, 1, out var major) || !TryReadNumber(match, 2, out var minor) || !TryReadNumber(match, 3, out var patch))
            return false;

        version = new Version(major, minor, patch);
        return true;
    }

    private static bool TryReadNumber(Match tagMatch, int groupIndex, out int number) =>
        int.TryParse(tagMatch.Groups[groupIndex].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static bool TryGetString(JsonElement root, string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return value is not null;
    }

    private static bool IsFalse(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.False;
}
