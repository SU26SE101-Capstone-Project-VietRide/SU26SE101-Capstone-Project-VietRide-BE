namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityRouteResponse(
    Guid RouteId,
    string Name,
    ReliabilityLocationResponse Origin,
    ReliabilityLocationResponse Destination);
