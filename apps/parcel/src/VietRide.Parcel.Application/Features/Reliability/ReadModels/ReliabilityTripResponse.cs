namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityTripResponse(
    Guid TripId,
    string? Status,
    DateTimeOffset? DepartureAt,
    DateTimeOffset? Eta,
    ReliabilityRouteResponse? Route,
    ReliabilityVehicleResponse? Vehicle,
    IReadOnlyList<ReliabilityTripStopResponse> Stops);
