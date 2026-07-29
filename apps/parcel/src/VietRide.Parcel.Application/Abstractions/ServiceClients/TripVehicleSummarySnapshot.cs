namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripVehicleSummarySnapshot(
    Guid VehicleId,
    string LicensePlate,
    string Status);
