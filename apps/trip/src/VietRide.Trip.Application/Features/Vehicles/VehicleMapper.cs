using System.Text.Json;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public static class VehicleMapper
{
    public static VehicleDto ToDto(Vehicle vehicle)
    {
        var layout = vehicle.SeatLayoutJson.Deserialize<SeatLayoutDto>()
            ?? throw new InvalidOperationException("Stored vehicle seat layout is invalid.");

        return new VehicleDto(
            vehicle.Id,
            vehicle.OperatorId,
            vehicle.VehicleTypeId,
            vehicle.LicensePlate,
            layout,
            vehicle.TotalSeats,
            SeatLayoutMetrics.CountUsablePassengerSeats(layout),
            vehicle.MaxCargoWeightKg,
            vehicle.MaxCargoVolumeM3,
            vehicle.ImageUrls,
            (VehicleStatusDto)vehicle.Status,
            vehicle.IsActive,
            vehicle.CreatedAt,
            vehicle.UpdatedAt);
    }
}
