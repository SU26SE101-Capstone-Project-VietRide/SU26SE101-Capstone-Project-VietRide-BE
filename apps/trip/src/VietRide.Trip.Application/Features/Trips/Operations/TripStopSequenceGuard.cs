using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

internal static class TripStopSequenceGuard
{
    public static void EnsureCanArriveStop(
        IReadOnlyCollection<TripStop> stops,
        TripStop targetStop)
    {
        var blockingStop = stops
            .Where(stop => stop.OrderIndex < targetStop.OrderIndex)
            .Where(IsBlocking)
            .OrderBy(stop => stop.OrderIndex)
            .ThenBy(stop => stop.StopId)
            .FirstOrDefault();
        if (blockingStop is not null)
            ThrowSequenceViolation(blockingStop, $"STOP:{targetStop.StopId:D}");
    }

    public static void EnsureCanArriveDestination(IReadOnlyCollection<TripStop> stops)
    {
        var blockingStop = stops
            .Where(IsBlocking)
            .OrderBy(stop => stop.OrderIndex)
            .ThenBy(stop => stop.StopId)
            .FirstOrDefault();
        if (blockingStop is not null)
            ThrowSequenceViolation(blockingStop, "DESTINATION");
    }

    private static bool IsBlocking(TripStop stop) =>
        stop.Status != TripStopStatus.SKIPPED && stop.ActualDepartureTime is null;

    private static void ThrowSequenceViolation(TripStop blockingStop, string target)
    {
        throw new CodedConflictException(
            "TRIP_STOP_SEQUENCE_VIOLATION",
            "A prior Trip stop must be departed before recording this arrival.",
            [
                new ValidationError("blockingStopId", blockingStop.StopId.ToString("D")),
                new ValidationError("target", target),
                new ValidationError("requiredAction", "DEPART_BLOCKING_STOP"),
            ]);
    }
}
