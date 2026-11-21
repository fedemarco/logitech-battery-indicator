using System.Diagnostics;
using System.Reflection;
using LogitechBattery.Battery;
using LogitechBattery.Devices;
using LogitechBattery.Diagnostics;
using LogitechBattery.History;
using LogitechBattery.Logging;
using LogitechBattery.Settings;

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
        Application.ThreadException += OnUiThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnBackgroundThreadException;
        var useMock = args.Contains(MockFlag);
        var version = AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly()) ?? "unknown";
        var mode = useMock ? "mock" : "HID++";
        AppLog.Info("Started LogitechBattery", ("version", version), ("mode", mode));
        Application.Run(CreateTrayContext(useMock));
    }

    // Logging the exception and returning keeps the tray app running.
    private static void OnUiThreadException(object sender, ThreadExceptionEventArgs e) =>
        AppLog.Error("Unhandled UI exception", AppLog.FailureWithStack(e.Exception));

    // The process still ends after this (for example a HID reader thread crashes); the line puts the cause in log.txt
    // instead of only in the Windows event log. AppLog writes and closes the file before it returns, so the line is on disk.
    private static void OnBackgroundThreadException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            AppLog.Error("Unhandled exception", AppLog.FailureWithStack(exception));
        else
            AppLog.Error("Unhandled exception", ("error", e.ExceptionObject?.GetType().Name ?? "unknown"));
    }

    // ThreadException only sees exceptions inside the message loop, so a failing constructor must be logged here.
    private static TrayApplicationContext CreateTrayContext(bool useMock)
    {
        try
        {
            var settingsFile = new SettingsFile(Path.Combine(AppPaths.DataDirectory, "settings.ini"));
            return useMock
                ? new TrayApplicationContext(new MockBatteryProvider(), histories: null, settingsFile) // simulated readings must never reach the real history files
                : new TrayApplicationContext(new HidPpBatteryProvider(), new DeviceHistories(AppPaths.DataDirectory), settingsFile);
        }
        catch (Exception ex)
        {
            AppLog.Error("Startup failed", AppLog.FailureWithStack(ex));
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

    private static void OpenInNotepad(string file) =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
}
