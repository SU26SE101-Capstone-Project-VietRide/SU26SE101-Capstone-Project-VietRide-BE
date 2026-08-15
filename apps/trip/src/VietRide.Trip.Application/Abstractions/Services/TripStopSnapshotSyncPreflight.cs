namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record TripStopSnapshotSyncPreflight(
    Guid RouteId,
    Guid OperatorId,
    IReadOnlyList<Guid> EligibleTripIds);
