using System.Runtime.ExceptionServices;

namespace LogitechBattery.App.Tests;

/// <summary>WinForms controls need a single-threaded apartment; xunit threads are multi-threaded. The body's exception is rethrown on the calling thread.</summary>
internal static class StaThread
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        void RunAndCaptureFailure()
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        }

        var thread = new Thread(RunAndCaptureFailure);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        // Throw (not "throw failure") keeps the original stack trace, so a failed assertion points at the test body.
        failure?.Throw();
    }
}
