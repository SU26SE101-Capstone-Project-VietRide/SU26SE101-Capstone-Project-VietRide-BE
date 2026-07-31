namespace VietRide.Trip.Application.Features.Trips.ListOperatorTrips;

public sealed record OperatorTripVehicleDto(
    Guid VehicleId,
    string LicensePlate,
    string Status);
