namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed record InternalTripVehicleSummaryDto(
    Guid VehicleId,
    string LicensePlate,
    string Status,
    InternalTripVehicleTypeSummaryDto VehicleType);
