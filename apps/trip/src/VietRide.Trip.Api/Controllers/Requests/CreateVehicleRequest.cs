using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateVehicleRequest(
    Guid VehicleTypeId,
    string? LicensePlate,
    SeatLayoutDto? SeatLayoutJson,
    int TotalSeats,
    decimal? MaxCargoWeightKg,
    decimal? MaxCargoVolumeM3);
