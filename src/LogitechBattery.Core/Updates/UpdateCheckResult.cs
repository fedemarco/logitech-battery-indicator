namespace LogitechBattery.Updates;

/// <summary>The outcome of one update check.</summary>
public abstract record UpdateCheckResult
{
    public sealed record UpToDate(Version Current) : UpdateCheckResult;

    public sealed record Available(Version Version, string PageUrl) : UpdateCheckResult;

    /// <summary>The reason is a short phrase without a final period, for the log and for a message to the user.</summary>
    public sealed record Failed(string Reason) : UpdateCheckResult;
}
