using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public static class ResourceAvailabilityConflictGuard
{
    public static void EnsureAvailable(ResourceAvailabilityResult result, AssignmentSourceType sourceType)
    {
        if (result.Available)
        {
            return;
        }

        var conflict = result.Conflicts[0];
        var isVehicle = string.Equals(
            conflict.ResourceRole,
            ResourceReservationRole.VEHICLE.ToString(),
            StringComparison.Ordinal);
        var code = sourceType == AssignmentSourceType.SHUTTLE_TRIP
            ? isVehicle ? "SHUTTLE_VEHICLE_CONFLICT" : "SHUTTLE_DRIVER_CONFLICT"
            : isVehicle ? "TRIP_VEHICLE_CONFLICT" : "TRIP_DRIVER_CONFLICT";

        throw new CodedConflictException(
            code,
            $"{conflict.ResourceRole} has an unavailable assignment window.",
            [
                new ValidationError("conflictReason", conflict.Reason),
                new ValidationError("resourceRole", conflict.ResourceRole),
                new ValidationError("resourceId", conflict.ResourceId.ToString("D")),
                new ValidationError("conflictingSourceType", conflict.ConflictingSourceType),
                new ValidationError("conflictingSourceId", conflict.ConflictingSourceId.ToString("D")),
                new ValidationError("blockingUntil", conflict.BlockingUntil.ToString("O")),
            ]);
    }
}
