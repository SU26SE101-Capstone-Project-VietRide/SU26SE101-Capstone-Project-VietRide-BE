namespace VietRide.Trip.Application.Abstractions.Repositories;

public sealed record ForwardingTripCandidate(
    Guid TripId,
    string PickupName,
    string TargetDropoffName,
    DateTimeOffset PickupAt,
    DateTimeOffset Eta,
    bool HasCargoCapacity);
