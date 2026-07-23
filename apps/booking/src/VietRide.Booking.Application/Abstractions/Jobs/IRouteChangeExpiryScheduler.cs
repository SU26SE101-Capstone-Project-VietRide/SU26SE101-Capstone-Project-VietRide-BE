namespace VietRide.Booking.Application.Abstractions.Jobs;

public interface IRouteChangeExpiryScheduler
{
    void EnsureScheduled(Guid pendingActionId, DateTimeOffset executeAt);
}
