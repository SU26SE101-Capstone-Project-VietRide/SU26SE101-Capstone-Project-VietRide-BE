namespace VietRide.Booking.Application.Abstractions.Jobs;

public interface IScheduleChangeAutoAcceptScheduler
{
    void EnsureScheduled(Guid pendingActionId, DateTimeOffset scheduledAt);
}
