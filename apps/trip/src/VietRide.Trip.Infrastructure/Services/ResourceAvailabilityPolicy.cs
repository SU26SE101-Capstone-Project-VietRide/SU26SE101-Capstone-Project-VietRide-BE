using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Infrastructure.Services;

internal static class ResourceAvailabilityPolicy
{
    public const int TurnaroundMinutes = 30;

    public static ResourceAvailabilityConflict? Compare(
        DateTimeOffset candidateStart,
        DateTimeOffset candidateEnd,
        DateTimeOffset existingStart,
        DateTimeOffset existingEnd,
        AvailabilityResource resource,
        AssignmentSourceType conflictingSourceType,
        Guid conflictingSourceId,
        int travelMinutes)
    {
        if (candidateStart < existingEnd && existingStart < candidateEnd)
        {
            var blockingUntil = existingEnd.AddMinutes(TurnaroundMinutes + travelMinutes);
            return Conflict(
                AvailabilityConflictReason.TIME_OVERLAP,
                blockingUntil,
                blockingUntil,
                travelMinutes);
        }

        if (existingEnd <= candidateStart)
        {
            var blockingUntil = existingEnd.AddMinutes(TurnaroundMinutes + travelMinutes);
            if (candidateStart < blockingUntil)
            {
                return Conflict(
                    travelMinutes == 0
                        ? AvailabilityConflictReason.TURNAROUND_REQUIRED
                        : AvailabilityConflictReason.REPOSITION_REQUIRED,
                    blockingUntil,
                    blockingUntil,
                    travelMinutes);
            }

            return null;
        }

        var requiredExistingStart = candidateEnd.AddMinutes(TurnaroundMinutes + travelMinutes);
        if (existingStart < requiredExistingStart)
        {
            return Conflict(
                travelMinutes == 0
                    ? AvailabilityConflictReason.TURNAROUND_REQUIRED
                    : AvailabilityConflictReason.REPOSITION_REQUIRED,
                requiredExistingStart,
                earliestFeasibleStartAt: null,
                travelMinutes);
        }

        return null;

        ResourceAvailabilityConflict Conflict(
            AvailabilityConflictReason reason,
            DateTimeOffset blockingUntil,
            DateTimeOffset? earliestFeasibleStartAt,
            int requiredTravelMinutes) =>
            new(
                resource.ResourceRole.ToString(),
                resource.ResourceId,
                reason.ToString(),
                conflictingSourceType.ToString(),
                conflictingSourceId,
                candidateStart,
                blockingUntil,
                earliestFeasibleStartAt,
                requiredTravelMinutes,
                TurnaroundMinutes);
    }

    public static bool CanFitBeforeNext(
        DateTimeOffset earliestFeasibleStartAt,
        TimeSpan candidateDuration,
        int travelMinutesToNext,
        DateTimeOffset nextStartAt) =>
        earliestFeasibleStartAt
            .Add(candidateDuration)
            .AddMinutes(TurnaroundMinutes + travelMinutesToNext)
            <= nextStartAt;
}
