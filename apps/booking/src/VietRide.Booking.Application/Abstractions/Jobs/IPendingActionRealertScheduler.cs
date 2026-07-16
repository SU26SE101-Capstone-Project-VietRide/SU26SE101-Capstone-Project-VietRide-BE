namespace VietRide.Booking.Application.Abstractions.Jobs;

public interface IPendingActionRealertScheduler
{
    void EnsureScheduled(Guid pendingActionId, DateTimeOffset scheduledAt);
}
