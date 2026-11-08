using System.Diagnostics;
using System.Reflection;
using LogitechBattery.Battery;

namespace LogitechBattery;

internal static class Program
{
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(365);

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--probe"))
        {
            OpenInNotepad(HidPpProbe.Run());
            return;
        }
        if (args.Contains("--listen"))
        {
            ApplicationConfiguration.Initialize();
            OpenInNotepad(HidPpListener.Run(() => MessageBox.Show(
                "Listening to the mouse and receiver.\n\nUnplug and replug, let the mouse sleep and wake it, then click OK to stop.",
                "Logitech Battery — listening")));
            return;
        }

        using var mutex = new Mutex(true, @"Local\LogitechBattery-SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Write($"Unhandled UI exception: {e.Exception}"); // keep the tray app running
        var useMock = args.Contains("--mock");
        var version = AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly()) ?? "unknown";
        var mode = useMock ? "mock" : "HID++";
        AppLog.Write($"Started LogitechBattery {version} ({mode})");
        Application.Run(CreateTrayContext(useMock));
    }

    // ThreadException only sees exceptions inside the message loop, so a failing constructor must be logged here.
    private static TrayApplicationContext CreateTrayContext(bool useMock)
    {
        try
        {
            return useMock
                ? new TrayApplicationContext(new MockBatteryProvider(), history: null) // simulated readings must never reach the real file
                : new TrayApplicationContext(new HidPpBatteryProvider(), OpenHistory());
        }
        catch (Exception ex)
        {
            AppLog.Write($"Startup failed: {ex}");
            throw;
        }
    }

    private static BatteryHistory OpenHistory()
    {
        var history = new BatteryHistory(Path.Combine(AppPaths.DataDirectory, "history.csv"));
        history.PruneOlderThan(HistoryRetention, DateTimeOffset.Now);
        return history;
    }

    private static void OpenInNotepad(string file) =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
}
