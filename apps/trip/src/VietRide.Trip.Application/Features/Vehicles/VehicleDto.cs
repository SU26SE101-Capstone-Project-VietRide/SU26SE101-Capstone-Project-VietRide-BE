namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record VehicleDto(
    Guid Id,
    Guid OperatorId,
    Guid VehicleTypeId,
    string LicensePlate,
    SeatLayoutDto SeatLayoutJson,
    int TotalSeats,
    decimal? MaxCargoWeightKg,
    decimal? MaxCargoVolumeM3,
    IReadOnlyCollection<string>? ImageUrls,
    VehicleStatusDto Status,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
