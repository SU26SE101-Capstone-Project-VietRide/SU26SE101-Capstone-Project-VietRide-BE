namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record ShuttleLifecycleResult(
    Guid ShuttleTripId,
    string Status,
    int ChangedPassengerCount = 0,
    DateTimeOffset? TransitionedAt = null);
