using LogitechBattery.Logging;

namespace LogitechBattery;

internal static class BackgroundWork
{
    /// <summary>
    /// Starts the work and does not wait for it. A failure is logged with the given message.
    /// It is async void because the caller gets no task, so every exception is caught here: one escaping would end the app.
    /// </summary>
    public static async void RunWithoutWaiting(Func<Task> work, string failureMessage)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            AppLog.Error(failureMessage, AppLog.FailureWithStack(ex));
        }
    }
}
