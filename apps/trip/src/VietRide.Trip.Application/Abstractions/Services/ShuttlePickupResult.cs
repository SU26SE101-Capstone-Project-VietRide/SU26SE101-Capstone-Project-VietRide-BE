namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record ShuttlePickupResult(
    Guid ShuttleTripId,
    int PickupOrder,
    int PickedUpPassengerCount,
    DateTimeOffset PickedUpAt);
