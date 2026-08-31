using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

internal static class TripCompletionDestinationGuard
{
    public static void EnsureManualCompletionAllowed(Domain.Entities.Trip trip)
    {
        if (trip.DestinationArrivedAt.HasValue)
            return;

        throw new CodedConflictException(
            "TRIP_DESTINATION_NOT_ARRIVED",
            "Trip destination arrival must be recorded before manual completion.",
            [new ValidationError("requiredAction", "ARRIVE_DESTINATION_BEFORE_COMPLETION")]);
    }
}
