using VietRide.Trip.Application.Abstractions.Jobs;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class DisabledTripGenerationJobScheduler : ITripGenerationJobScheduler
{
    public string EnqueueScheduleGeneration(Guid driverScheduleId) =>
        $"disabled:{driverScheduleId:N}";
}
