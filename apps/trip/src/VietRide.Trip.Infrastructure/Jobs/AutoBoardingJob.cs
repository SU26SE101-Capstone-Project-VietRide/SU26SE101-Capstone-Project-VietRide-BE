using Hangfire;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Infrastructure.Jobs;

public sealed class AutoBoardingJob
{
    private readonly ITripRepository tripRepository;
    private readonly ITripBoardingTransitionCoordinator coordinator;
    private readonly IClock clock;

    public AutoBoardingJob(
        ITripRepository tripRepository,
        ITripBoardingTransitionCoordinator coordinator,
        IClock clock)
    {
        this.tripRepository = tripRepository;
        this.coordinator = coordinator;
        this.clock = clock;
    }

    [Queue("trip")]
    [DisableConcurrentExecution(900)]
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tripIds = await tripRepository.ListScheduledForAutoBoardingAsync(
            now.AddMinutes(30), cancellationToken);
        foreach (var tripId in tripIds)
        {
            await coordinator.TryStartAutomaticAsync(tripId, now, cancellationToken);
        }
    }
}
