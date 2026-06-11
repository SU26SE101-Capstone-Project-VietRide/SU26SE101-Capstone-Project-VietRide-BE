using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.VehicleTypes;

public static class VehicleTypeMapper
{
    public static VehicleTypeDto ToDto(VehicleType vehicleType)
        => new(
            vehicleType.Id,
            vehicleType.Code,
            vehicleType.DisplayName,
            vehicleType.EstimatedPassengerLuggageKgPerSeat,
            vehicleType.DefaultSeatCount,
            vehicleType.IsSystemDefined,
            vehicleType.IsActive,
            vehicleType.CreatedAt,
            vehicleType.UpdatedAt);
}
