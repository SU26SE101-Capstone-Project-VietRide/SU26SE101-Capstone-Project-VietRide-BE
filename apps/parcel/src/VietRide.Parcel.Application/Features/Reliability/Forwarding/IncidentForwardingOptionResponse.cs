using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Reliability.Forwarding;

public sealed record IncidentForwardingOptionResponse(
    ReliabilityTripResponse Trip,
    ReliabilityRouteResponse? Route,
    ReliabilityVehicleResponse? Vehicle,
    ReliabilityLocationResponse PickupLocation,
    ReliabilityLocationResponse TargetDropoff,
    DateTimeOffset DepartureAt,
    DateTimeOffset Eta,
    bool CanReserve,
    string? UnavailableReason);
