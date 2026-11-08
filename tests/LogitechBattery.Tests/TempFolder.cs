namespace LogitechBattery.Tests;

/// <summary>A folder of its own for one test, deleted afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LogitechBattery.Tests-" + Guid.NewGuid());

    public string FileNamed(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
