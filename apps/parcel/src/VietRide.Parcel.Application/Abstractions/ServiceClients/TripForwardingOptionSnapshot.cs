namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripForwardingOptionSnapshot(
    TripSummarySnapshot Trip,
    Guid PickupLocationId,
    string PickupLocationType,
    string PickupLocationName,
    Guid TargetDropoffId,
    string TargetDropoffType,
    string TargetDropoffName,
    DateTimeOffset PickupAt,
    DateTimeOffset Eta,
    bool CanReserve,
    string? UnavailableReason);
