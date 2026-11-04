using System.Diagnostics;
using LogitechBattery.Battery;

namespace LogitechBattery;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--probe"))
        {
            var report = HidPpProbe.Run();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{report}\"") { UseShellExecute = true });
            return;
        }

        using var mutex = new Mutex(true, @"Local\LogitechBattery-SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(args.Contains("--mock") ? new MockBatteryProvider() : new HidPpBatteryProvider()));
    }
}
