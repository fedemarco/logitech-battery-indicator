using System.Runtime.CompilerServices;

namespace LogitechBattery.Tests;

internal static class TestDataFolder
{
    // Failure paths log through AppLog; that must never reach the user's real log.
    // Only this initializer sets DataDirectory: xUnit runs test classes in parallel, so a test that changed it would race.
    [ModuleInitializer]
    internal static void RedirectAppData() =>
        AppPaths.DataDirectory = Path.Combine(Path.GetTempPath(), "LogitechBattery.Tests");
}
