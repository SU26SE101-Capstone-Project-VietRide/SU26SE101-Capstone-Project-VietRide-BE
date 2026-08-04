namespace VietRide.Trip.Application.Abstractions.Services;

public interface IRouteChangeProposalLifecycleService
{
    Task SupersedePendingAsync(
        Guid tripId,
        Guid? actorUserId,
        Guid? approvedProposalId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task ExpirePendingForSourceAsync(
        Guid sourceAlternativeRouteId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task ExpirePendingForTripAsync(
        Guid tripId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
