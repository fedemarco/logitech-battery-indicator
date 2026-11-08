using System.Diagnostics;
using System.Reflection;
using LogitechBattery.Battery;

namespace LogitechBattery;

internal static class Program
{
    private const string ProbeFlag = "--probe";
    private const string ListenFlag = "--listen";
    private const string MockFlag = "--mock";

    [STAThread]
    private static void Main(string[] args)
    {
        if (RunDiagnostic(args))
            return;

        using var mutex = new Mutex(true, @"Local\LogitechBattery-SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Write($"Unhandled UI exception: {e.Exception}"); // keep the tray app running
        var useMock = args.Contains(MockFlag);
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

    /// <summary>Runs <c>--probe</c> or <c>--listen</c> when asked and opens the report; returns false for a normal start.</summary>
    private static bool RunDiagnostic(string[] args)
    {
        if (args.Contains(ProbeFlag))
        {
            OpenInNotepad(HidPpProbe.Run());
            return true;
        }
        if (args.Contains(ListenFlag))
        {
            ApplicationConfiguration.Initialize();
            OpenInNotepad(HidPpListener.Run(() => MessageBox.Show(
                "Listening to the mouse and receiver.\n\nUnplug and replug, let the mouse sleep and wake it, then click OK to stop.",
                "Logitech Battery — listening")));
            return true;
        }
        return false;
    }

    private static BatteryHistory OpenHistory()
    {
        var history = new BatteryHistory(Path.Combine(AppPaths.DataDirectory, "history.csv"));
        history.PruneOlderThan(BatteryHistory.Retention, DateTimeOffset.Now);
        return history;
    }

    private static void OpenInNotepad(string file) =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
}
