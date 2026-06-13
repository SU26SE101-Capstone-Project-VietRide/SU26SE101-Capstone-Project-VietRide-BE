using Hangfire;
using VietRide.Trip.Application.Abstractions.Jobs;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class HangfireTripGenerationJobScheduler : ITripGenerationJobScheduler
{
    private readonly IBackgroundJobClient backgroundJobClient;

    public HangfireTripGenerationJobScheduler(IBackgroundJobClient backgroundJobClient)
    {
        this.backgroundJobClient = backgroundJobClient;
    }

    public string EnqueueScheduleGeneration(Guid driverScheduleId)
    {
        return backgroundJobClient.Enqueue<TripGenerationJob>(job =>
            job.GenerateForScheduleAsync(driverScheduleId, CancellationToken.None));
    }
}
