using VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

namespace VietRide.Trip.Application.Features.Internal.Trips.ForwardingOptions;

public sealed record InternalForwardingOptionDto(
    InternalTripSummaryDto Trip,
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
