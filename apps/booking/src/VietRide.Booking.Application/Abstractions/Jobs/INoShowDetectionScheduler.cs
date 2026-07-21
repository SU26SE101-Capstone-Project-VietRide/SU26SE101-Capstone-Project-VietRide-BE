namespace VietRide.Booking.Application.Abstractions.Jobs;

public interface INoShowDetectionScheduler
{
    void EnsureScheduled();
}
