namespace VietRide.Trip.Application.Abstractions.Services;

public interface ITripBoardingTransitionCoordinator
{
    Task<TripBoardingTransitionResult> StartManualAsync(
        Guid tripId,
        Guid actorUserId,
        string actorRole,
        Guid? operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> TryStartAutomaticAsync(
        Guid tripId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
