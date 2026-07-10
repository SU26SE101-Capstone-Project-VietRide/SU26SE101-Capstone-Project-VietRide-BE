using MediatR;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record UpdateVehicleCommand(
    Guid OperatorId,
    Guid VehicleId,
    Guid? VehicleTypeId,
    string? LicensePlate,
    SeatLayoutDto? SeatLayoutJson,
    bool HasSeatLayoutJson,
    int? TotalSeats,
    decimal? MaxCargoWeightKg,
    bool HasMaxCargoWeightKg,
    decimal? MaxCargoVolumeM3,
    bool HasMaxCargoVolumeM3,
    VehicleStatusDto? Status,
    bool? IsActive,
    IReadOnlyCollection<string>? ImageUrls = null,
    bool HasImageUrls = false)
    : IRequest<VehicleDto>;
