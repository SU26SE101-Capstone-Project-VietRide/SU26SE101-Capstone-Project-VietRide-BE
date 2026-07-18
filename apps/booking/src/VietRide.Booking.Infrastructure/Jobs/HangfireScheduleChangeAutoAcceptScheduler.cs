using Hangfire;
using VietRide.Booking.Application.Abstractions.Jobs;

namespace VietRide.Booking.Infrastructure.Jobs;

public sealed class HangfireScheduleChangeAutoAcceptScheduler(IBackgroundJobClient backgroundJobs)
    : IScheduleChangeAutoAcceptScheduler
{
    public void EnsureScheduled(Guid pendingActionId, DateTimeOffset scheduledAt)
    {
        if (pendingActionId == Guid.Empty)
        {
            throw new ArgumentException("Pending-action id must be non-empty.", nameof(pendingActionId));
        }

        backgroundJobs.Schedule<ScheduleChangeAutoAcceptJob>(
            job => job.ExecuteAsync(pendingActionId, CancellationToken.None),
            scheduledAt);
    }
}
