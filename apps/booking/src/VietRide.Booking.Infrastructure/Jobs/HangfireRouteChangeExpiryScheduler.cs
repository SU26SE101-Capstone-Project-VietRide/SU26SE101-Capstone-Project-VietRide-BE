using Hangfire;
using VietRide.Booking.Application.Abstractions.Jobs;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class HangfireRouteChangeExpiryScheduler(IBackgroundJobClient backgroundJobs)
    : IRouteChangeExpiryScheduler
{
    public void EnsureScheduled(Guid pendingActionId, DateTimeOffset executeAt)
    {
        backgroundJobs.Schedule<RouteChangeExpiryJob>(
            job => job.ExecuteAsync(pendingActionId, CancellationToken.None),
            executeAt);
    }
}
