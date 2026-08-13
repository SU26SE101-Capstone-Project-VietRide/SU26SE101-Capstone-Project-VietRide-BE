using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Vehicles;

public static class VehicleMapper
{
    public static VehicleDto ToDto(
        Vehicle vehicle,
        VehicleAssignmentProjection? currentAssignment = null,
        VehicleAssignmentProjection? nextAssignment = null)
    {
        var layout = SeatLayoutJsonSerializer.Deserialize(vehicle.SeatLayoutJson);

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
            vehicle.UpdatedAt,
            ToAssignmentDto(currentAssignment),
            ToAssignmentDto(nextAssignment));
    }

    private static VehicleAssignmentDto? ToAssignmentDto(VehicleAssignmentProjection? assignment) =>
        assignment is null
            ? null
            : new VehicleAssignmentDto(
                assignment.SourceType,
                assignment.SourceType == AssignmentSourceType.TRIP.ToString() ? assignment.SourceId : null,
                assignment.SourceType == AssignmentSourceType.SHUTTLE_TRIP.ToString() ? assignment.SourceId : null,
                assignment.DriverUserId,
                assignment.StartsAt,
                assignment.EndsAt,
                assignment.Status,
                assignment.StartStationId,
                assignment.EndStationId);
}
