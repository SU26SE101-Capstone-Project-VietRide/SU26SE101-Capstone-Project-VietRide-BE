namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed record OperatorParcelVehicleResponse(
    Guid VehicleId,
    string LicensePlate);
