namespace VietRide.Trip.Application.Abstractions.Jobs;

public interface ITripGenerationJobScheduler
{
    string EnqueueScheduleGeneration(Guid driverScheduleId);
}
