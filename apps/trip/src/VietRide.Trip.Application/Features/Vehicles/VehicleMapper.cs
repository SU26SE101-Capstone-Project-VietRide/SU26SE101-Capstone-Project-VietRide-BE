using System.Text.Json;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public static class VehicleMapper
{
    public static VehicleDto ToDto(Vehicle vehicle)
        => new(
            vehicle.Id,
            vehicle.OperatorId,
            vehicle.VehicleTypeId,
            vehicle.LicensePlate,
            vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>()
                ?? throw new InvalidOperationException("Stored vehicle seat layout is invalid."),
            vehicle.TotalSeats,
            vehicle.MaxCargoWeightKg,
            vehicle.MaxCargoVolumeM3,
            vehicle.ImageUrls,
            (VehicleStatusDto)vehicle.Status,
            vehicle.IsActive,
            vehicle.CreatedAt,
            vehicle.UpdatedAt);
}
