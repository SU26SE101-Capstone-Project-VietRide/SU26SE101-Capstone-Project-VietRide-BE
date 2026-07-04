namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record SubstituteVehicleResponse(
    Guid OldTripId,
    Guid NewTripId,
    string Status);
