using LogitechBattery.Battery;
using LogitechBattery.HidPp;
using LogitechBattery.Logging;

namespace LogitechBattery.Devices;

/// <summary>One getStatus request on one route, and what its answer means.</summary>
internal static class BatteryStatusRequest
{
    internal enum RouteOutcome { Answered, NoAnswer, Failed, ChannelLost }

    /// <summary>What reading one route gave; <see cref="Status"/> is set only for <see cref="RouteOutcome.Answered"/>.</summary>
    internal readonly record struct RouteRead(RouteOutcome Outcome, BatteryStatus? Status = null);

    internal static RouteRead ReadStatus(BatteryTarget route, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var request = HidPpMessage.BuildRequest(route.DeviceIndex, route.FeatureIndex, UnifiedBattery.GetStatusFunction, HidPpMessage.SoftwareId);
            var answer = route.Channel.Exchange(request, HidPpChannel.ResponseTimeout, cancellationToken);
            return ReadAnswer(route, answer, now);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return new RouteRead(RouteOutcome.ChannelLost);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // one faulty device must not fail the reading of the others
            // shortcut: logged at every poll while the device keeps failing (bounded by log rotation); log once per device if a real device ever does this
            AppLog.Warn("getStatus failed", [("device", route.Device.Name), .. AppLog.Failure(ex)]);
            return new RouteRead(RouteOutcome.Failed);
        }
    }

    internal static RouteRead ReadAnswer(BatteryTarget route, HidPpAnswer answer, DateTimeOffset now)
    {
        if (answer.ErrorCode is { } errorCode)
        {
            // The device answered with an error: not asleep.
            AppLog.Warn("getStatus returned an error", ("device", route.Device.Name), ("errorCode", $"0x{errorCode:X2}"));
            return new RouteRead(RouteOutcome.Failed);
        }
        if (answer.Response is not { } response)
            return new RouteRead(RouteOutcome.NoAnswer);

        try
        {
            return new RouteRead(RouteOutcome.Answered, UnifiedBattery.ParseStatus(HidPpMessage.ParamsOf(response), now));
        }
        catch (FormatException ex)
        {
            AppLog.Warn("getStatus answer is malformed", [("device", route.Device.Name), .. AppLog.Failure(ex)]);
            return new RouteRead(RouteOutcome.Failed);
        }
    }
}
