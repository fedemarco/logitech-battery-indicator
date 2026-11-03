using LogitechBattery.Battery;

namespace LogitechBattery;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\LogitechBattery-SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(new MockBatteryProvider()));
    }
}
