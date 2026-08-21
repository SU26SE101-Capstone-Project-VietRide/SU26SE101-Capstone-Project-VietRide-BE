namespace VietRide.Parcel.Application.Features.Reliability.ReadModels;

public sealed record ReliabilityVehicleResponse(
    Guid VehicleId,
    string LicensePlate,
    string? Status);
