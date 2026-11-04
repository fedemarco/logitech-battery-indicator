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
        Application.Run(new TrayApplicationContext(args.Contains("--mock") ? new MockBatteryProvider() : new HidPpBatteryProvider()));
    }

    private static void OpenInNotepad(string file) =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
}
