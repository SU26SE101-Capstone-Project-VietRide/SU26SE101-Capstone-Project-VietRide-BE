using MediatR;

namespace VietRide.Trip.Application.Features.Vehicles;

public sealed record CreateVehicleCommand(
    Guid OperatorId,
    Guid VehicleTypeId,
    string? LicensePlate,
    SeatLayoutDto? SeatLayoutJson,
    int TotalSeats,
    decimal? MaxCargoWeightKg,
    decimal? MaxCargoVolumeM3,
    IReadOnlyCollection<string>? ImageUrls = null)
    : IRequest<VehicleDto>;
