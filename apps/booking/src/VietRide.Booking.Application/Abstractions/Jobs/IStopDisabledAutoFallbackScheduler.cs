namespace VietRide.Booking.Application.Abstractions.Jobs;

public interface IStopDisabledAutoFallbackScheduler
{
    void EnsureScheduled();
}
