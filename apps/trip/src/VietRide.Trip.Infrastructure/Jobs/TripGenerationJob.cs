using Hangfire;
using MediatR;
using VietRide.Trip.Application.Features.TripGeneration;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class TripGenerationJob
{
    private readonly ISender sender;

    public TripGenerationJob(ISender sender)
    {
        this.sender = sender;
    }

    [Queue("trip")]
    [DisableConcurrentExecution(600)]
    public Task GenerateForScheduleAsync(Guid driverScheduleId, CancellationToken cancellationToken = default)
    {
        return ExecuteWithGenerationLockAsync(
            () => sender.Send(new GenerateTripsForScheduleCommand(driverScheduleId), cancellationToken));
    }

    [Queue("trip")]
    [DisableConcurrentExecution(600)]
    public Task GenerateForActiveSchedulesAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithGenerationLockAsync(
            () => sender.Send(new GenerateTripsForScheduleCommand(), cancellationToken));
    }

    private static async Task ExecuteWithGenerationLockAsync(Func<Task> action)
    {
        using var connection = JobStorage.Current.GetConnection();
        using var generationLock = connection.AcquireDistributedLock("trip-generation", TimeSpan.FromMinutes(10));
        await action();
    }
}
